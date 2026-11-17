using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DamTwin
{
    /// <summary>
    /// Writes a CSV with every report (S), command (A), user mark (MARK) and settling
    /// event (SETTLED). Files go to &lt;project&gt;/ExperimentLogs (or next to the build).
    /// </summary>
    [DisallowMultipleComponent]
    public class ExperimentLogger : MonoBehaviour
    {
        public TwinController twin;
        public ReservoirModel reservoir;
        public StabilityMonitor stability;

        public bool IsLogging => writer != null;
        public string FilePath { get; private set; } = string.Empty;
        public int RowCount { get; private set; }
        public int MarkCount { get; private set; }

        private StreamWriter writer;
        private float startTime;
        private float lastFlush;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string LogDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExperimentLogs"));

        private void OnEnable()
        {
            if (twin != null)
            {
                twin.StateReceived += OnState;
                twin.CommandSent += OnCommand;
            }
            if (stability != null) stability.Settled += OnSettled;
        }

        private void OnDisable()
        {
            if (twin != null)
            {
                twin.StateReceived -= OnState;
                twin.CommandSent -= OnCommand;
            }
            if (stability != null) stability.Settled -= OnSettled;
            StopLogging();
        }

        public void StartLogging()
        {
            if (IsLogging) return;
            try
            {
                Directory.CreateDirectory(LogDirectory);
                FilePath = Path.Combine(LogDirectory, "dam_twin_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv) + ".csv");
                writer = new StreamWriter(FilePath, false, new UTF8Encoding(false));
                writer.WriteLine("time_s,event,detail,reported_angle_deg,source,display_angle_deg,last_command_deg," +
                                 "opening_pct,level_m,gate_flow_m3s,spill_flow_m3s,settle_time_s");
                startTime = Time.unscaledTime;
                RowCount = 0;
                MarkCount = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError("Cannot create log file: " + ex.Message);
                writer = null;
            }
        }

        public void StopLogging()
        {
            if (writer == null) return;
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        public void Mark()
        {
            MarkCount++;
            Write("MARK", MarkCount.ToString(Inv), float.NaN);
        }

        private void OnState(StateReport report) => Write("S", string.Empty, float.NaN);
        private void OnCommand(int angle) => Write("A", "A:" + angle.ToString(Inv), float.NaN);
        private void OnSettled(string label, float seconds, int angle) => Write("SETTLED", label, seconds);

        private void Write(string evt, string detail, float settle)
        {
            if (writer == null) return;
            var sb = new StringBuilder(160);
            sb.Append((Time.unscaledTime - startTime).ToString("0.000", Inv)).Append(',');
            sb.Append(evt).Append(',');
            sb.Append(detail.Replace(',', ';')).Append(',');
            sb.Append(twin.HasState ? twin.ReportedAngle.ToString(Inv) : "").Append(',');
            sb.Append(twin.Source == ControlSource.Digital ? "DIG" : twin.Source == ControlSource.Physical ? "POT" : "").Append(',');
            sb.Append(twin.DisplayAngle.ToString("0.0", Inv)).Append(',');
            sb.Append(twin.LastCommandAngle >= 0 ? twin.LastCommandAngle.ToString(Inv) : "").Append(',');
            sb.Append((twin.ReportedOpening01 * 100f).ToString("0.0", Inv)).Append(',');
            sb.Append(reservoir != null ? reservoir.Level.ToString("0.000", Inv) : "").Append(',');
            sb.Append(reservoir != null ? reservoir.GateFlow.ToString("0.000", Inv) : "").Append(',');
            sb.Append(reservoir != null ? reservoir.SpillwayFlow.ToString("0.000", Inv) : "").Append(',');
            sb.Append(float.IsNaN(settle) ? "" : settle.ToString("0.000", Inv));
            writer.WriteLine(sb.ToString());
            RowCount++;

            if (Time.unscaledTime - lastFlush > 1f)
            {
                writer.Flush();
                lastFlush = Time.unscaledTime;
            }
        }
    }
}
