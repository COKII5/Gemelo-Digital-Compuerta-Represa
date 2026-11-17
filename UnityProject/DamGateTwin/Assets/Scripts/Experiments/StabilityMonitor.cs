using System;
using UnityEngine;

namespace DamTwin
{
    /// <summary>
    /// Experiment B helper. A measurement starts when control changes hands
    /// (a digital command is sent, or the reports switch DIG -> POT). It ends when the
    /// reported angle stays inside +/- angleTolerance for holdTime seconds.
    /// Settling time = instant of the last change - instant of the triggering event.
    /// </summary>
    [DisallowMultipleComponent]
    public class StabilityMonitor : MonoBehaviour
    {
        public TwinController twin;
        [Tooltip("Tolerance used to decide that the angle no longer changes (deg).")]
        public float angleTolerance = 1f;
        [Tooltip("Time the angle must stay inside the tolerance to be considered stable (s).")]
        public float holdTime = 0.5f;

        public bool IsMeasuring { get; private set; }
        public string CurrentLabel { get; private set; } = string.Empty;
        public float LastSettleTime { get; private set; } = -1f;
        public string LastLabel { get; private set; } = string.Empty;
        public int LastFinalAngle { get; private set; }
        public int CompletedCount { get; private set; }

        /// <summary>(label, settle time in s, final angle).</summary>
        public event Action<string, float, int> Settled;

        private float eventTime;
        private float lastChangeTime;
        private int anchorAngle;

        private void OnEnable()
        {
            if (twin == null) return;
            twin.CommandSent += OnCommandSent;
            twin.StateReceived += OnStateReceived;
            twin.SourceChanged += OnSourceChanged;
        }

        private void OnDisable()
        {
            if (twin == null) return;
            twin.CommandSent -= OnCommandSent;
            twin.StateReceived -= OnStateReceived;
            twin.SourceChanged -= OnSourceChanged;
        }

        private void OnCommandSent(int angle)
        {
            string from = twin.Source == ControlSource.Physical ? "POT" : "DIG";
            Begin(from + "->DIG (A:" + angle + ")");
        }

        private void OnSourceChanged(ControlSource previous, ControlSource current)
        {
            if (current == ControlSource.Physical && previous == ControlSource.Digital)
                Begin("DIG->POT");
            // POT->DIG is already covered by the command that caused it.
        }

        private void Begin(string label)
        {
            IsMeasuring = true;
            CurrentLabel = label;
            eventTime = Time.unscaledTime;
            lastChangeTime = eventTime;
            anchorAngle = twin.ReportedAngle;
        }

        private void OnStateReceived(StateReport report)
        {
            if (!IsMeasuring) return;
            if (Mathf.Abs(report.Angle - anchorAngle) > angleTolerance)
            {
                anchorAngle = report.Angle;
                lastChangeTime = Time.unscaledTime;
            }
        }

        private void Update()
        {
            if (!IsMeasuring) return;
            if (Time.unscaledTime - lastChangeTime < holdTime) return;

            IsMeasuring = false;
            LastSettleTime = lastChangeTime - eventTime;
            LastLabel = CurrentLabel;
            LastFinalAngle = twin.ReportedAngle;
            CompletedCount++;
            Settled?.Invoke(LastLabel, LastSettleTime, LastFinalAngle);
        }
    }
}
