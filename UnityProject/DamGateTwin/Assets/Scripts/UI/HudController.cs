using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DamTwin
{
    /// <summary>
    /// Runtime HUD: connection panel, digital control (slider -> A:), virtual pot for the
    /// emulator, experiment logging, twin state and hydraulic readouts.
    /// </summary>
    [DisallowMultipleComponent]
    public class HudController : MonoBehaviour
    {
        public TwinController twin;
        public ReservoirModel reservoir;
        public StabilityMonitor stability;
        public ExperimentLogger logger;

        private static readonly Color PotColor = new Color(0.98f, 0.62f, 0.20f);
        private static readonly Color DigColor = new Color(0.25f, 0.70f, 1.00f);
        private static readonly Color OkColor = new Color(0.35f, 0.85f, 0.45f);
        private static readonly Color ErrorColor = new Color(1.00f, 0.40f, 0.35f);

        // Connection
        private Text portLabel;
        private Text connectLabel;
        private Text emulatorLabel;
        private Text statusText;
        private string[] ports = new string[0];
        private int portIndex;

        // Digital control
        private Slider commandSlider;
        private PointerHoldTracker commandHold;
        private Text commandText;

        // Emulator
        private RectTransform emulatorGroup;
        private Slider potSlider;
        private Text potText;
        private Text policyLabel;

        // Experiments
        private Text logButtonLabel;
        private Text logText;
        private Text stabilityText;

        // Right panel
        private Text angleText;
        private Text sourceText;
        private Text openingText;
        private Text hydraulicsText;
        private Text alertText;
        private RectTransform levelFill;
        private RectTransform equilibriumMarker;

        private float textTimer;
        private readonly StringBuilder sb = new StringBuilder(512);

        private void Start()
        {
            BuildUi();
            RefreshPorts();
        }

        // =====================================================================================
        // UI construction
        // =====================================================================================
        private void BuildUi()
        {
            Canvas canvas = UiFactory.CreateCanvas("HUD");
            BuildLeftPanel(canvas.transform);
            BuildRightPanel(canvas.transform);
        }

        private void BuildLeftPanel(Transform canvas)
        {
            RectTransform panel = UiFactory.CreatePanel(canvas, "ControlPanel", new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(440f, 810f));
            float y = 14f;
            const float x = 16f, w = 408f;

            Section(panel, "GEMELO DIGITAL · COMPUERTA DE REPRESA", ref y, 21, UiFactory.TextColor);
            y += 26f; // the title wraps to two lines
            Label(panel, "Situación 4 · Apertura de la compuerta controlada por el servomotor", ref y, 14, UiFactory.MutedText, 38f);
            y += 4f;

            // ---- Connection -------------------------------------------------------------
            Section(panel, "Conexión", ref y, 17, UiFactory.AccentColor);
            UiFactory.Place((RectTransform)UiFactory.CreateButton(panel, "PrevPort", "<", () => CyclePort(-1), out _).transform, x, y, 40f, 34f);
            var portBg = UiFactory.CreateImage(panel, "PortBox", new Color(0.12f, 0.14f, 0.18f, 1f));
            UiFactory.Place((RectTransform)portBg.transform, x + 46f, y, 150f, 34f);
            portLabel = UiFactory.CreateText(portBg.transform, "Port", "COM3", 17, UiFactory.TextColor, TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)portLabel.transform);
            UiFactory.Place((RectTransform)UiFactory.CreateButton(panel, "NextPort", ">", () => CyclePort(1), out _).transform, x + 202f, y, 40f, 34f);
            UiFactory.Place((RectTransform)UiFactory.CreateButton(panel, "Refresh", "Actualizar", RefreshPorts, out _).transform, x + 248f, y, 160f, 34f);
            y += 42f;

            UiFactory.Place((RectTransform)UiFactory.CreateButton(panel, "Connect", "Conectar", ToggleSerial, out connectLabel).transform, x, y, 200f, 36f);
            UiFactory.Place((RectTransform)UiFactory.CreateButton(panel, "Emulator", "Emulador (sin HW)", ToggleEmulator, out emulatorLabel).transform, x + 208f, y, 200f, 36f);
            y += 44f;

            statusText = UiFactory.CreateText(panel, "Status", "", 14, UiFactory.MutedText);
            UiFactory.Place((RectTransform)statusText.transform, x, y, w, 58f);
            y += 62f;

            // ---- Digital control ----------------------------------------------------------
            Section(panel, "Mando digital  (Unity -> Arduino, A:<ángulo>)", ref y, 17, UiFactory.AccentColor);
            commandSlider = UiFactory.CreateSlider(panel, "CommandSlider", twin.closedAngle, twin.openAngle, twin.closedAngle, true, DigColor);
            UiFactory.Place((RectTransform)commandSlider.transform, x, y, w, 30f);
            commandHold = commandSlider.gameObject.AddComponent<PointerHoldTracker>();
            // Only user interaction reaches this listener: device updates use SetValueWithoutNotify.
            commandSlider.onValueChanged.AddListener(v => twin.RequestDigitalAngle(Mathf.RoundToInt(v)));
            y += 34f;
            commandText = UiFactory.CreateText(panel, "CommandText", "", 15, UiFactory.TextColor);
            UiFactory.Place((RectTransform)commandText.transform, x, y, w, 40f);
            y += 44f;

            // ---- Emulator ------------------------------------------------------------------
            emulatorGroup = new GameObject("EmulatorGroup", typeof(RectTransform)).GetComponent<RectTransform>();
            emulatorGroup.SetParent(panel, false);
            UiFactory.Place(emulatorGroup, 0f, y, 440f, 150f);
            float ey = 0f;
            Section(emulatorGroup, "Mando físico emulado (potenciómetro virtual)", ref ey, 17, PotColor);
            potSlider = UiFactory.CreateSlider(emulatorGroup, "VirtualPot", twin.closedAngle, twin.openAngle, twin.closedAngle, false, PotColor);
            UiFactory.Place((RectTransform)potSlider.transform, x, ey, w, 30f);
            potSlider.onValueChanged.AddListener(v => { if (twin.Emulator != null) twin.Emulator.VirtualPotAngle = v; });
            ey += 34f;
            potText = UiFactory.CreateText(emulatorGroup, "PotText", "", 14, UiFactory.MutedText);
            UiFactory.Place((RectTransform)potText.transform, x, ey, w, 20f);
            ey += 24f;
            UiFactory.Place((RectTransform)UiFactory.CreateButton(emulatorGroup, "Policy", "", CyclePolicy, out policyLabel).transform, x, ey, w, 32f);
            y += 160f;

            // ---- Experiments ---------------------------------------------------------------
            Section(panel, "Experimentos", ref y, 17, UiFactory.AccentColor);
            UiFactory.Place((RectTransform)UiFactory.CreateButton(panel, "Log", "Iniciar registro CSV", ToggleLogging, out logButtonLabel).transform, x, y, 200f, 34f);
            UiFactory.Place((RectTransform)UiFactory.CreateButton(panel, "Mark", "Marcar evento", () => logger.Mark(), out _).transform, x + 208f, y, 200f, 34f);
            y += 40f;
            logText = UiFactory.CreateText(panel, "LogText", "", 13, UiFactory.MutedText);
            UiFactory.Place((RectTransform)logText.transform, x, y, w, 36f);
            y += 40f;
            stabilityText = UiFactory.CreateText(panel, "Stability", "", 15, UiFactory.TextColor);
            UiFactory.Place((RectTransform)stabilityText.transform, x, y, w, 44f);
            y += 48f;

            var help = UiFactory.CreateText(panel, "Help",
                "Clic derecho + arrastrar: orbitar · Rueda: zoom · 1/2/3: vistas · R: reiniciar vista",
                13, UiFactory.MutedText);
            UiFactory.Place((RectTransform)help.transform, x, y, w, 36f);
        }

        private void BuildRightPanel(Transform canvas)
        {
            RectTransform panel = UiFactory.CreatePanel(canvas, "StatePanel", new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(420f, 760f));
            float y = 14f;
            const float x = 16f, w = 388f;

            Section(panel, "Estado del gemelo (Arduino -> Unity, S:)", ref y, 17, UiFactory.AccentColor);
            angleText = UiFactory.CreateText(panel, "Angle", "--", 40, UiFactory.TextColor, TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place((RectTransform)angleText.transform, x, y, w, 50f);
            y += 52f;
            sourceText = UiFactory.CreateText(panel, "Source", "", 18, UiFactory.TextColor, TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place((RectTransform)sourceText.transform, x, y, w, 26f);
            y += 28f;
            openingText = UiFactory.CreateText(panel, "Opening", "", 16, UiFactory.TextColor);
            UiFactory.Place((RectTransform)openingText.transform, x, y, w, 46f);
            y += 54f;

            Section(panel, "Hidráulica del embalse", ref y, 17, UiFactory.AccentColor);

            // Vertical level gauge
            var gaugeBg = UiFactory.CreateImage(panel, "LevelGauge", new Color(0.12f, 0.14f, 0.18f, 1f));
            UiFactory.Place((RectTransform)gaugeBg.transform, x, y, 36f, 220f);
            var fill = UiFactory.CreateImage(gaugeBg.transform, "Fill", DigColor);
            levelFill = (RectTransform)fill.transform;
            levelFill.anchorMin = Vector2.zero;
            levelFill.anchorMax = new Vector2(1f, 0.5f);
            levelFill.offsetMin = levelFill.offsetMax = Vector2.zero;
            var crest = UiFactory.CreateImage(gaugeBg.transform, "Crest", ErrorColor);
            var crestRt = (RectTransform)crest.transform;
            float crestN = reservoir.spillwayCrest / reservoir.damCrest;
            crestRt.anchorMin = new Vector2(-0.15f, crestN);
            crestRt.anchorMax = new Vector2(1.15f, crestN);
            crestRt.sizeDelta = new Vector2(0f, 3f);
            var eq = UiFactory.CreateImage(gaugeBg.transform, "Equilibrium", Color.white);
            equilibriumMarker = (RectTransform)eq.transform;
            equilibriumMarker.anchorMin = new Vector2(1f, 0.5f);
            equilibriumMarker.anchorMax = new Vector2(1.35f, 0.5f);
            equilibriumMarker.sizeDelta = new Vector2(0f, 3f);

            hydraulicsText = UiFactory.CreateText(panel, "Hydraulics", "", 16, UiFactory.TextColor);
            hydraulicsText.lineSpacing = 1.15f;
            UiFactory.Place((RectTransform)hydraulicsText.transform, x + 58f, y, w - 58f, 220f);
            y += 228f;

            alertText = UiFactory.CreateText(panel, "Alert", "", 17, ErrorColor, TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place((RectTransform)alertText.transform, x, y, w, 24f);
            y += 28f;

            Section(panel, "Historial (90 s)", ref y, 15, UiFactory.MutedText);
            var graphGo = new GameObject("LevelGraph", typeof(RectTransform), typeof(RawImage));
            graphGo.transform.SetParent(panel, false);
            UiFactory.Place((RectTransform)graphGo.transform, x, y, w, 140f);
            var graph = graphGo.AddComponent<LevelHistoryGraph>();
            graph.reservoir = reservoir;
            graph.twin = twin;
            y += 144f;
            var legend = UiFactory.CreateText(panel, "Legend",
                "<color=#46A5F5>— nivel h (0–11 m)</color>   <color=#F5A032>— apertura (0–100 %)</color>   <color=#DC3C32>— cresta</color>",
                13, UiFactory.MutedText);
            UiFactory.Place((RectTransform)legend.transform, x, y, w, 20f);
        }

        private static void Section(RectTransform parent, string title, ref float y, int size, Color color)
        {
            var t = UiFactory.CreateText(parent, "Section", title, size, color, TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place((RectTransform)t.transform, 16f, y, parent.rect.width - 32f, size + 8f);
            y += size + 12f;
        }

        private static void Label(RectTransform parent, string content, ref float y, int size, Color color, float height)
        {
            var t = UiFactory.CreateText(parent, "Label", content, size, color);
            UiFactory.Place((RectTransform)t.transform, 16f, y, 408f, height);
            y += height;
        }

        // =====================================================================================
        // Actions
        // =====================================================================================
        private void RefreshPorts()
        {
            ports = SerialDeviceLink.GetPortNames();
            portIndex = Mathf.Max(0, System.Array.IndexOf(ports, twin.portName));
            UpdatePortLabel();
        }

        private void CyclePort(int step)
        {
            if (ports.Length == 0) return;
            portIndex = (portIndex + step + ports.Length) % ports.Length;
            UpdatePortLabel();
        }

        private void UpdatePortLabel()
        {
            if (portLabel == null) return;
            portLabel.text = ports.Length > 0 ? ports[portIndex] : twin.portName + " (?)";
        }

        private string SelectedPort => ports.Length > 0 ? ports[portIndex] : twin.portName;

        private void ToggleSerial()
        {
            if (twin.Mode == LinkMode.Serial) twin.Disconnect();
            else twin.ConnectSerial(SelectedPort);
        }

        private void ToggleEmulator()
        {
            if (twin.Mode == LinkMode.Emulator)
            {
                twin.Disconnect();
                return;
            }
            twin.StartEmulator();
            if (twin.Emulator != null) potSlider.SetValueWithoutNotify(twin.Emulator.VirtualPotAngle);
        }

        private void CyclePolicy()
        {
            twin.emulatorPolicy = (ArbitrationPolicy)(((int)twin.emulatorPolicy + 1) % 3);
            if (twin.Emulator != null) twin.Emulator.Policy = twin.emulatorPolicy;
        }

        private void ToggleLogging()
        {
            if (logger.IsLogging) logger.StopLogging();
            else logger.StartLogging();
        }

        // =====================================================================================
        // Refresh
        // =====================================================================================
        private void Update()
        {
            if (commandSlider == null) return;

            // Physical -> digital: mirror the reported angle in the slider unless the user holds it.
            // SetValueWithoutNotify guarantees that device updates are never re-sent as A: commands.
            if (twin.HasState && !commandHold.IsHeld)
                commandSlider.SetValueWithoutNotify(twin.ReportedAngle);

            if (levelFill != null)
            {
                levelFill.anchorMax = new Vector2(1f, Mathf.Clamp01(reservoir.Level / reservoir.damCrest));
                float eq = reservoir.ComputeEquilibriumLevel(reservoir.GateOpening01) / reservoir.damCrest;
                equilibriumMarker.anchorMin = new Vector2(1f, eq);
                equilibriumMarker.anchorMax = new Vector2(1.35f, eq);
            }

            textTimer -= Time.unscaledDeltaTime;
            if (textTimer > 0f) return;
            textTimer = 0.08f;
            RefreshTexts();
        }

        private void RefreshTexts()
        {
            // ---- Connection --------------------------------------------------------------------
            connectLabel.text = twin.Mode == LinkMode.Serial ? "Desconectar" : "Conectar";
            emulatorLabel.text = twin.Mode == LinkMode.Emulator ? "Detener emulador" : "Emulador (sin HW)";

            sb.Clear();
            bool error = twin.StatusMessage.StartsWith("Error") || twin.StatusMessage.StartsWith("Conexión perdida");
            sb.Append("<color=").Append(error ? "#FF6659" : twin.Mode != LinkMode.Disconnected ? "#59D973" : "#9EADBF").Append('>')
              .Append(twin.StatusMessage).Append("</color>\n");
            if (twin.Mode != LinkMode.Disconnected)
            {
                float age = twin.LastReportTime < 0f ? -1f : Time.unscaledTime - twin.LastReportTime;
                sb.Append("Último: ").Append(string.IsNullOrEmpty(twin.LastRawLine) ? "—" : twin.LastRawLine);
                if (age >= 0f) sb.Append($"  (hace {age:0.00} s)");
                sb.Append($"  ·  {twin.ReportRateHz:0.0} Hz");
                if (twin.MalformedLines > 0) sb.Append($"  ·  descartadas: {twin.MalformedLines}");
            }
            else if (!SerialDeviceLink.IsSupported)
            {
                sb.Append("<color=#FF6659>Serial no disponible: Project Settings > Player > Api Compatibility Level = .NET Framework</color>");
            }
            statusText.text = sb.ToString();

            // ---- Digital command -----------------------------------------------------------------
            sb.Clear();
            int sliderAngle = Mathf.RoundToInt(commandSlider.value);
            sb.Append($"Posición del mando: {sliderAngle}°  ({Mathf.InverseLerp(twin.closedAngle, twin.openAngle, sliderAngle) * 100f:0}% de apertura)\n");
            sb.Append(twin.LastCommandAngle >= 0
                ? $"<color=#9EADBF>Último comando enviado: A:{twin.LastCommandAngle}  (hace {Time.unscaledTime - twin.LastCommandTime:0.0} s)</color>"
                : "<color=#9EADBF>Aún no se ha enviado ningún comando</color>");
            commandText.text = sb.ToString();

            // ---- Emulator ---------------------------------------------------------------------
            bool emulator = twin.Mode == LinkMode.Emulator;
            emulatorGroup.gameObject.SetActive(emulator);
            if (emulator)
            {
                potText.text = $"Perilla virtual: {potSlider.value:0.0}°  (ruido ±{twin.emulatorPotNoiseDeg:0.0}°)";
                policyLabel.text = "Política de arbitraje: " + PolicyName(twin.emulatorPolicy) + "  (cambiar)";
            }

            // ---- Experiments ------------------------------------------------------------------
            logButtonLabel.text = logger.IsLogging ? "Detener registro" : "Iniciar registro CSV";
            logText.text = logger.IsLogging
                ? $"Grabando: {System.IO.Path.GetFileName(logger.FilePath)}  ·  filas: {logger.RowCount}  ·  marcas: {logger.MarkCount}"
                : (string.IsNullOrEmpty(logger.FilePath) ? "Carpeta: " + ExperimentLogger.LogDirectory : "Último archivo: " + logger.FilePath);

            sb.Clear();
            if (stability.IsMeasuring) sb.Append("<color=#F5C342>Estabilizando… ").Append(stability.CurrentLabel).Append("</color>\n");
            else sb.Append("<color=#59D973>Estado estable</color>\n");
            if (stability.LastSettleTime >= 0f)
                sb.Append($"Última transición: {stability.LastLabel} -> {stability.LastSettleTime:0.00} s  (final {stability.LastFinalAngle}°, n={stability.CompletedCount})");
            stabilityText.text = sb.ToString();

            // ---- Twin state ---------------------------------------------------------------------
            if (twin.HasState)
            {
                angleText.text = $"Ángulo servo: {twin.ReportedAngle}°";
                bool dig = twin.Source == ControlSource.Digital;
                sourceText.text = dig ? "Fuente: DIG · mando digital (Unity)" : "Fuente: POT · mando físico (potenciómetro)";
                sourceText.color = dig ? DigColor : PotColor;
            }
            else
            {
                angleText.text = "Ángulo servo: --";
                sourceText.text = "Sin reportes del dispositivo";
                sourceText.color = UiFactory.MutedText;
            }
            string leds = !twin.HasState ? "<color=#9EADBF>LED: apagados</color>"
                : twin.ReportedAngle <= twin.closedAngle
                    ? "LED: <color=#FF4A3D><b>ROJO</b></color> (compuerta cerrada)"
                    : "LED: <color=#3BE05A><b>VERDE</b></color> (compuerta abierta)";
            openingText.text = $"Apertura de la compuerta: {twin.ReportedOpening01 * 100f:0} %  ({twin.ReportedOpening01 * reservoir.gateMaxOpening:0.00} m de {reservoir.gateMaxOpening:0.0} m)\n{leds}";

            // ---- Hydraulics -------------------------------------------------------------------------
            float rate = reservoir.LevelRate * 60f; // m per simulated minute
            string trend = Mathf.Abs(reservoir.LevelRateVisible) < 0.002f ? "estable" : reservoir.LevelRate > 0f ? "SUBE" : "BAJA";
            float eqLevel = reservoir.ComputeEquilibriumLevel(reservoir.GateOpening01);

            sb.Clear();
            sb.Append($"<size=22><b>Nivel h = {reservoir.Level:0.00} m</b></size>\n");
            sb.Append($"Tendencia: <b>{trend}</b>  ({rate:+0.00;-0.00;0.00} m/min)\n");
            sb.Append($"Caudal de entrada Qin: {reservoir.inflow:0.0} m³/s\n");
            sb.Append($"Caudal compuerta Qc: <color=#46A5F5>{reservoir.GateFlow:0.00} m³/s</color>\n");
            sb.Append($"Caudal vertedero Qv: {reservoir.SpillwayFlow:0.00} m³/s\n");
            sb.Append($"Velocidad del chorro: {reservoir.JetVelocity:0.0} m/s\n");
            sb.Append(eqLevel >= reservoir.damCrest - 0.01f
                ? "Nivel de equilibrio: <color=#FF6659>desborde</color>\n"
                : $"Nivel de equilibrio: {eqLevel:0.00} m\n");
            sb.Append($"<color=#9EADBF>Tiempo simulado ×{reservoir.timeScale:0}: {reservoir.SimulatedTime / 60f:0.0} min</color>");
            hydraulicsText.text = sb.ToString();

            if (reservoir.Paused) alertText.text = "SIMULACIÓN EN PAUSA: esperando reportes S:";
            else if (reservoir.IsOvertopping) alertText.text = "¡DESBORDE DE LA PRESA!";
            else if (reservoir.SpillwayFlow > 0.001f) alertText.text = "VERTEDERO ACTIVO (nivel > 9 m)";
            else if (reservoir.Level < 0.3f) alertText.text = "EMBALSE CASI VACÍO";
            else alertText.text = "";
        }

        private static string PolicyName(ArbitrationPolicy policy)
        {
            switch (policy)
            {
                case ArbitrationPolicy.PotAlways: return "Potenciómetro siempre (base)";
                case ArbitrationPolicy.LastMover: return "Último en moverse";
                default: return "Recogida (pickup)";
            }
        }
    }
}
