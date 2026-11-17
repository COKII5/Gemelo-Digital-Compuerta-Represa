using System.Globalization;

namespace DamTwin
{
    /// <summary>Which control fixed the current target angle on the device.</summary>
    public enum ControlSource
    {
        Unknown,
        Physical, // POT
        Digital   // DIG
    }

    /// <summary>A parsed "S:&lt;angle&gt;,&lt;source&gt;" message.</summary>
    public readonly struct StateReport
    {
        public readonly int Angle;
        public readonly ControlSource Source;

        public StateReport(int angle, ControlSource source)
        {
            Angle = angle;
            Source = source;
        }
    }

    /// <summary>
    /// Shared text protocol (9600 baud, '\n' terminated):
    ///   Unity  -> Arduino : A:&lt;angle&gt;            (0..180)
    ///   Arduino -> Unity  : S:&lt;angle&gt;,&lt;POT|DIG&gt;
    /// </summary>
    public static class Protocol
    {
        public const int MinAngle = 0;
        public const int MaxAngle = 180;
        public const string PhysicalTag = "POT";
        public const string DigitalTag = "DIG";

        public static string FormatCommand(int angle)
        {
            if (angle < MinAngle) angle = MinAngle;
            if (angle > MaxAngle) angle = MaxAngle;
            return "A:" + angle.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatState(int angle, ControlSource source)
        {
            return "S:" + angle.ToString(CultureInfo.InvariantCulture) + "," +
                   (source == ControlSource.Digital ? DigitalTag : PhysicalTag);
        }

        /// <summary>Parses "A:&lt;0..180&gt;". Returns false for malformed or out-of-range commands.</summary>
        public static bool TryParseCommand(string line, out int angle)
        {
            angle = 0;
            if (string.IsNullOrEmpty(line)) return false;
            line = line.Trim();
            if (!line.StartsWith("A:", System.StringComparison.Ordinal)) return false;

            string number = line.Substring(2);
            if (number.Length == 0 || number.Length > 3) return false;
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out angle)) return false;
            return angle >= MinAngle && angle <= MaxAngle;
        }

        /// <summary>Parses "S:&lt;angle&gt;,&lt;POT|DIG&gt;". Returns false for malformed messages.</summary>
        public static bool TryParseState(string line, out StateReport report)
        {
            report = default;
            if (string.IsNullOrEmpty(line)) return false;
            line = line.Trim();
            if (!line.StartsWith("S:", System.StringComparison.Ordinal)) return false;

            string[] parts = line.Substring(2).Split(',');
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int angle)) return false;
            if (angle < MinAngle || angle > MaxAngle) return false;

            ControlSource source;
            switch (parts[1])
            {
                case PhysicalTag: source = ControlSource.Physical; break;
                case DigitalTag: source = ControlSource.Digital; break;
                default: return false;
            }

            report = new StateReport(angle, source);
            return true;
        }
    }
}
