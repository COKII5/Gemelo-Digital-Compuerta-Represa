// System.IO.Ports is only available with the ".NET Framework" API compatibility
// level. ProjectSetup (Editor) switches the project to it automatically; until
// then this file compiles a stub so the rest of the project still builds.
#if NET_4_6 || NET_UNITY_4_8
#define DAMTWIN_SERIAL_AVAILABLE
#endif

using System;
using System.Collections.Concurrent;
using System.Threading;
#if DAMTWIN_SERIAL_AVAILABLE
using System.IO.Ports;
#endif

namespace DamTwin
{
    /// <summary>
    /// Serial link that reads on a background thread. The main thread only
    /// dequeues complete lines, so a slow or silent port never blocks Unity.
    /// </summary>
    public sealed class SerialDeviceLink : IDeviceLink
    {
        private const int MaxQueuedLines = 512;
        private const int ReadTimeoutMs = 50;
        private const int WriteTimeoutMs = 100;

        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private Thread worker;
        private volatile bool running;
        private volatile string lastError = string.Empty;

#if DAMTWIN_SERIAL_AVAILABLE
        private SerialPort port;
#endif

        public string PortName { get; private set; } = string.Empty;
        public int BaudRate { get; private set; }

        public static bool IsSupported
        {
            get
            {
#if DAMTWIN_SERIAL_AVAILABLE
                return true;
#else
                return false;
#endif
            }
        }

        public bool IsOpen
        {
            get
            {
#if DAMTWIN_SERIAL_AVAILABLE
                return running && port != null && port.IsOpen;
#else
                return false;
#endif
            }
        }

        public string Description => IsOpen ? $"Serial {PortName} @ {BaudRate}" : "Serial (cerrado)";
        public string LastError => lastError;

        public static string[] GetPortNames()
        {
#if DAMTWIN_SERIAL_AVAILABLE
            try
            {
                string[] names = SerialPort.GetPortNames();
                Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                return names;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
#else
            return Array.Empty<string>();
#endif
        }

        public bool Open(string portName, int baudRate)
        {
            Dispose();
            PortName = portName;
            BaudRate = baudRate;
            lastError = string.Empty;

#if DAMTWIN_SERIAL_AVAILABLE
            try
            {
                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    NewLine = "\n",
                    ReadTimeout = ReadTimeoutMs,
                    WriteTimeout = WriteTimeoutMs,
                    DtrEnable = true, // most boards reset on open; Leonardo/Micro need DTR to transmit
                    RtsEnable = true
                };
                port.Open();
                port.DiscardInBuffer();

                running = true;
                worker = new Thread(WorkerLoop) { IsBackground = true, Name = "DamTwin.Serial" };
                worker.Start();
                return true;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                ClosePort();
                return false;
            }
#else
            lastError = "System.IO.Ports no disponible: use API Compatibility Level = .NET Framework.";
            return false;
#endif
        }

        public bool TryReadLine(out string line) => incoming.TryDequeue(out line);

        public void SendLine(string line)
        {
            if (IsOpen) outgoing.Enqueue(line);
        }

        public void Tick(float deltaTime) { }

#if DAMTWIN_SERIAL_AVAILABLE
        private void WorkerLoop()
        {
            while (running)
            {
                try
                {
                    while (outgoing.TryDequeue(out string message))
                    {
                        port.Write(message + "\n");
                    }

                    string line = port.ReadLine(); // blocks this worker thread only
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    incoming.Enqueue(line);
                    while (incoming.Count > MaxQueuedLines && incoming.TryDequeue(out _)) { }
                }
                catch (TimeoutException)
                {
                    // No complete line during this window: loop again to flush pending writes.
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    running = false;
                }
            }
        }
#endif

        private void ClosePort()
        {
#if DAMTWIN_SERIAL_AVAILABLE
            try
            {
                if (port != null && port.IsOpen) port.Close();
            }
            catch (Exception)
            {
                // Port already gone (e.g., cable unplugged).
            }
            port?.Dispose();
            port = null;
#endif
        }

        public void Dispose()
        {
            running = false;
            if (worker != null && worker.IsAlive) worker.Join(ReadTimeoutMs * 4);
            worker = null;
            ClosePort();
            while (incoming.TryDequeue(out _)) { }
            while (outgoing.TryDequeue(out _)) { }
        }
    }
}
