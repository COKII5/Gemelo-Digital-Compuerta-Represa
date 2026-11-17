using System;

namespace DamTwin
{
    /// <summary>
    /// Line-oriented link to the physical device (or to its emulator).
    /// All methods are called from the Unity main thread and never block it.
    /// </summary>
    public interface IDeviceLink : IDisposable
    {
        bool IsOpen { get; }
        string Description { get; }
        string LastError { get; }

        /// <summary>Returns the next received line, if any, without blocking.</summary>
        bool TryReadLine(out string line);

        /// <summary>Queues a line to be sent (the '\n' terminator is appended by the link).</summary>
        void SendLine(string line);

        /// <summary>Called once per frame. Real links ignore it; the emulator runs its firmware model here.</summary>
        void Tick(float deltaTime);
    }
}
