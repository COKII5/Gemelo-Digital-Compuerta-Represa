using System;
using UnityEngine;

namespace DamTwin
{
    public enum LinkMode
    {
        Disconnected,
        Serial,
        Emulator
    }

    /// <summary>
    /// Owns the device link and keeps the twin state.
    ///  - Physical -> digital: every "S:" report updates the reported angle (and the scene).
    ///  - Digital -> physical: only user actions call RequestDigitalAngle(), which sends "A:".
    ///    Device reports never generate commands, so there is no echo loop.
    /// </summary>
    [DisallowMultipleComponent]
    public class TwinController : MonoBehaviour
    {
        [Header("Serial port")]
        [Tooltip("Default port. It can be changed from the HUD at runtime.")]
        public string portName = "COM3";
        public int baudRate = 9600;
        [Tooltip("Connect automatically to the serial port on Play.")]
        public bool autoConnectSerial = false;

        [Header("Useful angular range (gate closed -> fully open)")]
        [Range(0, 180)] public int closedAngle = 0;
        [Range(0, 180)] public int openAngle = 90;

        [Header("Digital command")]
        [Tooltip("Minimum time between A: commands while the user drags the slider (s).")]
        public float minCommandInterval = 0.05f;

        [Header("Visual interpolation")]
        [Tooltip("The reported angle arrives at 20 Hz; the scene interpolates between reports at this speed (deg/s). Must exceed the servo max speed.")]
        public float displayFollowSpeed = 240f;

        [Header("Firmware emulator (no hardware)")]
        public ArbitrationPolicy emulatorPolicy = ArbitrationPolicy.LastMover;
        public float emulatorMaxSpeed = 60f;
        public float emulatorPotNoiseDeg = 0.6f;

        // ---- Live state -------------------------------------------------------------
        public LinkMode Mode { get; private set; } = LinkMode.Disconnected;
        public IDeviceLink Link => link;
        public EmulatedDeviceLink Emulator => link as EmulatedDeviceLink;

        public bool HasState { get; private set; }
        public int ReportedAngle { get; private set; }
        public ControlSource Source { get; private set; } = ControlSource.Unknown;
        public float DisplayAngle { get; private set; }
        public string LastRawLine { get; private set; } = string.Empty;
        public float LastReportTime { get; private set; } = -1f;
        public float ReportRateHz { get; private set; }
        public int MalformedLines { get; private set; }
        public int LastCommandAngle { get; private set; } = -1;
        public float LastCommandTime { get; private set; } = -1f;
        public string StatusMessage { get; private set; } = "Desconectado";

        /// <summary>Gate opening 0..1 from the reported angle (what the device says).</summary>
        public float ReportedOpening01 => Mathf.InverseLerp(closedAngle, openAngle, ReportedAngle);

        /// <summary>Gate opening 0..1 used by the scene (interpolated between reports).</summary>
        public float DisplayOpening01 => Mathf.InverseLerp(closedAngle, openAngle, DisplayAngle);

        public event Action<StateReport> StateReceived;
        public event Action<ControlSource, ControlSource> SourceChanged;
        public event Action<int> CommandSent;
        public event Action<LinkMode> ModeChanged;

        private IDeviceLink link;
        private int pendingCommand = -1;
        private float lastSendTime = -10f;
        private int reportsInWindow;
        private float rateWindowStart;

        private void Awake()
        {
            Application.runInBackground = true; // keep reading the port when the window loses focus
            DisplayAngle = closedAngle;
        }

        private void Start()
        {
            if (autoConnectSerial) ConnectSerial(portName);
        }

        // ---- Connection management -------------------------------------------------------
        public bool ConnectSerial(string port)
        {
            CloseLink();
            portName = port;
            var serial = new SerialDeviceLink();
            if (!serial.Open(port, baudRate))
            {
                StatusMessage = "Error al abrir " + port + ": " + serial.LastError;
                SetMode(LinkMode.Disconnected);
                return false;
            }

            link = serial;
            StatusMessage = "Conectado a " + port + " @ " + baudRate + " (esperando reportes...)";
            SetMode(LinkMode.Serial);
            return true;
        }

        public void StartEmulator()
        {
            CloseLink();
            float initial = HasState ? ReportedAngle : closedAngle + 0.3f * (openAngle - closedAngle);
            link = new EmulatedDeviceLink(closedAngle, openAngle, initial,
                                          emulatorPolicy, emulatorMaxSpeed, emulatorPotNoiseDeg);
            StatusMessage = "Emulador de firmware activo (sin hardware)";
            SetMode(LinkMode.Emulator);
        }

        public void Disconnect()
        {
            CloseLink();
            StatusMessage = "Desconectado";
            SetMode(LinkMode.Disconnected);
        }

        private void CloseLink()
        {
            link?.Dispose();
            link = null;
            pendingCommand = -1;
            reportsInWindow = 0;
            ReportRateHz = 0f;
        }

        private void SetMode(LinkMode mode)
        {
            Mode = mode;
            ModeChanged?.Invoke(mode);
        }

        // ---- Digital control (user only) ---------------------------------------------------
        /// <summary>Called ONLY by user interaction in the scene. Rate-limited, last value always sent.</summary>
        public void RequestDigitalAngle(int angle)
        {
            pendingCommand = Mathf.Clamp(angle, closedAngle, openAngle);
        }

        private void FlushPendingCommand()
        {
            if (pendingCommand < 0 || link == null || !link.IsOpen) return;
            if (Time.unscaledTime - lastSendTime < minCommandInterval) return;

            int angle = pendingCommand;
            pendingCommand = -1;
            if (angle == LastCommandAngle && Time.unscaledTime - LastCommandTime < 0.5f) return;

            link.SendLine(Protocol.FormatCommand(angle));
            lastSendTime = Time.unscaledTime;
            LastCommandAngle = angle;
            LastCommandTime = Time.unscaledTime;
            CommandSent?.Invoke(angle);
        }

        // ---- Main loop ----------------------------------------------------------------------
        private void Update()
        {
            if (link != null)
            {
                link.Tick(Time.unscaledDeltaTime);

                if (!link.IsOpen && Mode == LinkMode.Serial)
                {
                    StatusMessage = "Conexión perdida: " + link.LastError;
                    CloseLink();
                    SetMode(LinkMode.Disconnected);
                }
                else
                {
                    DrainIncoming();
                    FlushPendingCommand();
                }
            }

            UpdateRate();
            DisplayAngle = Mathf.MoveTowards(DisplayAngle, HasState ? ReportedAngle : DisplayAngle,
                                             displayFollowSpeed * Time.unscaledDeltaTime);
        }

        private void DrainIncoming()
        {
            // Bounded per frame so a flood of data can never stall the frame.
            for (int i = 0; i < 200 && link.TryReadLine(out string line); i++)
            {
                LastRawLine = line;
                if (!Protocol.TryParseState(line, out StateReport report))
                {
                    MalformedLines++;
                    continue;
                }
                ApplyReport(report);
            }
        }

        private void ApplyReport(StateReport report)
        {
            ControlSource previous = Source;
            bool first = !HasState;

            ReportedAngle = report.Angle;
            Source = report.Source;
            HasState = true;
            LastReportTime = Time.unscaledTime;
            reportsInWindow++;

            if (first)
            {
                DisplayAngle = report.Angle;
                StatusMessage = link.Description;
            }

            StateReceived?.Invoke(report);
            if (previous != report.Source) SourceChanged?.Invoke(previous, report.Source);
        }

        private void UpdateRate()
        {
            float elapsed = Time.unscaledTime - rateWindowStart;
            if (elapsed < 1f) return;
            ReportRateHz = reportsInWindow / elapsed;
            reportsInWindow = 0;
            rateWindowStart = Time.unscaledTime;
        }

        private void OnDisable() => CloseLink();
        private void OnApplicationQuit() => CloseLink();
    }
}
