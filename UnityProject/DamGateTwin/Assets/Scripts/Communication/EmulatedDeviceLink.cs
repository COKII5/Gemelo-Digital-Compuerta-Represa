using System;
using System.Collections.Generic;

namespace DamTwin
{
    public enum ArbitrationPolicy
    {
        PotAlways = 0,  // baseline: pot applied every cycle
        LastMover = 1,  // last control that moves wins (deadband on the pot)
        Pickup = 2      // pot regains control only after catching the target
    }

    /// <summary>
    /// Software model of the Arduino firmware (same timing, ramp, parser and
    /// arbitration). It lets the scene be tested and demonstrated without
    /// hardware. The "physical" knob is a virtual pot with analog noise.
    /// </summary>
    public sealed class EmulatedDeviceLink : IDeviceLink
    {
        // Same constants as DamGateTwin.ino
        private const float PotPeriod = 0.010f;
        private const float MotionPeriod = 0.020f;
        private const float ReportPeriod = 0.050f;
        private const float PotFilterAlpha = 0.20f;
        private const float PotJitterDeg = 1.0f;
        private const float PotTakeoverDeg = 4.0f;
        private const float PotNoiseDeg = 1.5f;
        private const float PickupWindowDeg = 2.0f;

        private readonly Queue<string> incoming = new Queue<string>();
        private readonly Queue<string> outgoing = new Queue<string>();
        private readonly Random random = new Random();

        private readonly int usefulMin;
        private readonly int usefulMax;

        private float potAngle;
        private float potLatchedAngle;
        private float targetAngle;
        private float currentAngle;
        private ControlSource source = ControlSource.Physical;

        private double clock;
        private double nextPot;
        private double nextMotion;
        private double nextReport;
        private double lastMotion;
        private bool disposed;

        public ArbitrationPolicy Policy { get; set; }
        public float MaxSpeedDegPerSecond { get; set; }

        /// <summary>Peak analog noise of the virtual pot, in degrees.</summary>
        public float PotNoiseAmplitudeDeg { get; set; }

        /// <summary>Position of the virtual knob in degrees (useful range).</summary>
        public float VirtualPotAngle { get; set; }

        public bool IsOpen => !disposed;
        public string Description => "Emulador de firmware (" + Policy + ")";
        public string LastError => string.Empty;

        public EmulatedDeviceLink(int usefulMinDeg, int usefulMaxDeg, float initialAngle,
                                  ArbitrationPolicy policy, float maxSpeedDegPerSecond, float potNoiseDeg)
        {
            usefulMin = usefulMinDeg;
            usefulMax = usefulMaxDeg;
            Policy = policy;
            MaxSpeedDegPerSecond = maxSpeedDegPerSecond;
            PotNoiseAmplitudeDeg = potNoiseDeg;

            VirtualPotAngle = initialAngle;
            potAngle = initialAngle;
            targetAngle = (float)Math.Round(initialAngle);
            currentAngle = targetAngle;
        }

        public bool TryReadLine(out string line)
        {
            if (incoming.Count > 0)
            {
                line = incoming.Dequeue();
                return true;
            }
            line = null;
            return false;
        }

        public void SendLine(string line)
        {
            if (!disposed) outgoing.Enqueue(line);
        }

        public void Tick(float deltaTime)
        {
            if (disposed) return;
            double end = clock + Math.Min(deltaTime, 0.25f);

            // Step the firmware "loop" at 1 ms resolution like millis().
            while (clock < end)
            {
                clock = Math.Min(clock + 0.001, end);

                while (outgoing.Count > 0) HandleMessage(outgoing.Dequeue());

                if (clock >= nextPot)
                {
                    nextPot = clock + PotPeriod;
                    SamplePot();
                    Arbitrate();
                }
                if (clock >= nextMotion)
                {
                    float dt = (float)(clock - lastMotion);
                    lastMotion = clock;
                    nextMotion = clock + MotionPeriod;
                    UpdateMotion(dt);
                }
                if (clock >= nextReport)
                {
                    nextReport = clock + ReportPeriod;
                    incoming.Enqueue(Protocol.FormatState(RoundToInt(currentAngle), source));
                }
            }
        }

        private void HandleMessage(string message)
        {
            if (!Protocol.TryParseCommand(message, out int angle)) return; // discard silently
            targetAngle = Clamp(angle, usefulMin, usefulMax);
            source = ControlSource.Digital;
            potLatchedAngle = potAngle;
        }

        private void SamplePot()
        {
            float noise = (float)(random.NextDouble() * 2.0 - 1.0) * PotNoiseAmplitudeDeg;
            float reading = Clamp(VirtualPotAngle + noise, usefulMin, usefulMax);
            potAngle += PotFilterAlpha * (reading - potAngle);
        }

        private void FollowPot(bool force)
        {
            if (force || Math.Abs(potAngle - targetAngle) >= PotJitterDeg)
                targetAngle = RoundToInt(potAngle);
        }

        private void Arbitrate()
        {
            switch (Policy)
            {
                case ArbitrationPolicy.PotAlways:
                {
                    bool wasDigital = source == ControlSource.Digital;
                    source = ControlSource.Physical;
                    FollowPot(wasDigital);
                    break;
                }
                case ArbitrationPolicy.LastMover:
                {
                    if (source == ControlSource.Digital)
                    {
                        if (Math.Abs(potAngle - potLatchedAngle) < PotTakeoverDeg) return;
                        source = ControlSource.Physical;
                        FollowPot(true);
                    }
                    else FollowPot(false);
                    break;
                }
                case ArbitrationPolicy.Pickup:
                {
                    if (source == ControlSource.Digital)
                    {
                        bool moved = Math.Abs(potAngle - potLatchedAngle) >= PotNoiseDeg;
                        bool near = Math.Abs(potAngle - targetAngle) <= PickupWindowDeg;
                        bool crossed = (potLatchedAngle - targetAngle) * (potAngle - targetAngle) < 0f;
                        if (!(moved && (near || crossed))) return;
                        source = ControlSource.Physical;
                        FollowPot(true);
                    }
                    else FollowPot(false);
                    break;
                }
            }
        }

        private void UpdateMotion(float dt)
        {
            float maxStep = MaxSpeedDegPerSecond * dt;
            float error = targetAngle - currentAngle;
            if (Math.Abs(error) <= maxStep) currentAngle = targetAngle;
            else currentAngle += error > 0f ? maxStep : -maxStep;
        }

        private static float Clamp(float value, float low, float high) =>
            value < low ? low : (value > high ? high : value);

        private static int RoundToInt(float value) => (int)Math.Floor(value + 0.5f);

        public void Dispose()
        {
            disposed = true;
            incoming.Clear();
            outgoing.Clear();
        }
    }
}
