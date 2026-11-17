using UnityEngine;

namespace DamTwin
{
    /// <summary>
    /// Drives every moving part of the scene from the twin state:
    /// gate plate, hoist wheel (servo angle), reservoir water, outflow jet,
    /// downstream channel, spillway overflow and alarm lamp.
    /// References are assigned by the scene builder (Digital Twin > Rebuild Dam Scene).
    /// </summary>
    [DisallowMultipleComponent]
    public class DamVisuals : MonoBehaviour
    {
        [Header("Model")]
        public TwinController twin;
        public ReservoirModel reservoir;

        [Header("Reservoir")]
        public Transform reservoirWater;
        public float waterBottom = 0.02f;
        public TextMesh levelLabel;
        public Transform levelMarker;

        [Header("Gate and hoist")]
        public Transform gatePlate;
        public float gatePlateHeight = 2.4f;
        public float gateSillY = 0f;
        public Transform gateStem;
        public float hoistAxleY = 12.2f;
        public Transform hoistWheel;
        public TextMesh gateLabel;

        [Header("Outflow")]
        public ParticleSystem gateJet;
        public float jetParticlesPerCubicMeter = 30f;
        public float jetVisualSpeedFactor = 0.55f;
        public Transform channelWater;
        public float channelFloorY = 0.02f;
        public float channelWidth = 5.8f;
        public ParticleSystem channelFoam;
        public float channelLength = 40f;

        [Header("Inflow")]
        public ParticleSystem inflowStream;
        public float inflowOutletY = 10.5f;
        public float inflowParticlesPerCubicMeter = 25f;

        [Header("Spillway")]
        public ParticleSystem spillwayFlow;
        public float spillParticlesPerCubicMeter = 60f;

        [Header("Alarm lamp")]
        public Renderer alarmLamp;
        public Color normalColor = new Color(0.15f, 0.85f, 0.3f);
        public Color warningColor = new Color(1f, 0.7f, 0.1f);
        public Color alarmColor = new Color(1f, 0.15f, 0.1f);
        [Tooltip("Level (m) above which the lamp turns amber.")]
        public float warningLevel = 8.5f;

        [Header("Status LEDs (mirror of the physical green/red LEDs)")]
        [Tooltip("Created at runtime on the hoist if left empty.")]
        public Renderer greenLed;
        public Renderer redLed;
        public Color greenOnColor = new Color(0.1f, 1f, 0.25f);
        public Color redOnColor = new Color(1f, 0.1f, 0.08f);
        public Color ledOffColor = new Color(0.12f, 0.12f, 0.12f);

        /// <summary>Same rule as the firmware: red only when fully closed, green otherwise.</summary>
        public bool IsGateFullyClosed => twin != null && twin.HasState && twin.ReportedAngle <= twin.closedAngle;

        private MaterialPropertyBlock lampBlock;
        private float labelTimer;

        private void Awake()
        {
            lampBlock = new MaterialPropertyBlock();
            if (greenLed == null || redLed == null) CreateStatusLeds();
        }

        private void CreateStatusLeds()
        {
            Transform parent = hoistWheel != null ? hoistWheel.parent : transform;
            Material material = alarmLamp != null ? alarmLamp.sharedMaterial : null;
            greenLed = CreateLed("GreenLed", parent, new Vector3(2.1f, 12.75f, -0.25f), material);
            redLed = CreateLed("RedLed", parent, new Vector3(2.1f, 12.15f, -0.25f), material);
        }

        private static Renderer CreateLed(string name, Transform parent, Vector3 localPosition, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = Vector3.one * 0.38f;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            return renderer;
        }

        private void LateUpdate()
        {
            if (twin == null || reservoir == null) return;

            // The hydraulic model is driven by the state reported by the device.
            reservoir.GateOpening01 = twin.DisplayOpening01;
            reservoir.Paused = !twin.HasState;

            UpdateGate();
            UpdateReservoirWater();
            UpdateInflow();
            UpdateOutflow();
            UpdateSpillway();
            UpdateLamp();
            UpdateStatusLeds();
        }

        private void UpdateStatusLeds()
        {
            bool closed = IsGateFullyClosed;
            bool open = twin.HasState && !closed;
            SetLedColor(greenLed, open ? greenOnColor : ledOffColor, open);
            SetLedColor(redLed, closed ? redOnColor : ledOffColor, closed);
        }

        private void SetLedColor(Renderer led, Color color, bool on)
        {
            if (led == null) return;
            led.GetPropertyBlock(lampBlock);
            lampBlock.SetColor("_Color", color);
            lampBlock.SetColor("_EmissionColor", on ? color * 2.5f : Color.black);
            led.SetPropertyBlock(lampBlock);
        }

        private void UpdateGate()
        {
            float opening = reservoir.GateOpeningMeters;
            float bottom = gateSillY + opening;

            if (gatePlate != null)
            {
                Vector3 p = gatePlate.localPosition;
                p.y = bottom + 0.5f * gatePlateHeight;
                gatePlate.localPosition = p;
            }

            if (gateStem != null)
            {
                float top = bottom + gatePlateHeight;
                float length = Mathf.Max(0.05f, hoistAxleY - top);
                Vector3 p = gateStem.localPosition;
                p.y = top + 0.5f * length;
                gateStem.localPosition = p;
                Vector3 s = gateStem.localScale;
                s.y = length;
                gateStem.localScale = s;
            }

            if (hoistWheel != null)
            {
                // The wheel shows the servo shaft: same angle as reported by the device.
                hoistWheel.localRotation = Quaternion.Euler(twin.DisplayAngle, 0f, 0f);
            }

            if (gateLabel != null)
            {
                gateLabel.text = $"Compuerta  {twin.DisplayAngle:0}°  ({twin.DisplayOpening01 * 100f:0}%)";
            }
        }

        private void UpdateReservoirWater()
        {
            float level = Mathf.Max(0.01f, reservoir.Level);

            if (reservoirWater != null)
            {
                Vector3 s = reservoirWater.localScale;
                s.y = level;
                reservoirWater.localScale = s;
                Vector3 p = reservoirWater.localPosition;
                p.y = waterBottom + 0.5f * level;
                reservoirWater.localPosition = p;
            }

            if (levelMarker != null)
            {
                Vector3 p = levelMarker.localPosition;
                p.y = waterBottom + level;
                levelMarker.localPosition = p;
            }

            labelTimer -= Time.deltaTime;
            if (levelLabel != null && labelTimer <= 0f)
            {
                labelTimer = 0.1f;
                levelLabel.text = $"h = {reservoir.Level:0.00} m";
            }
        }

        private void UpdateInflow()
        {
            if (inflowStream == null) return;
            var emission = inflowStream.emission;
            emission.rateOverTime = reservoir.inflow * inflowParticlesPerCubicMeter;

            // Particles end where the falling stream meets the water surface.
            float drop = Mathf.Max(0.2f, inflowOutletY - (waterBottom + reservoir.Level));
            var main = inflowStream.main;
            main.startLifetime = Mathf.Sqrt(2f * drop / ReservoirModel.Gravity);
        }

        private void UpdateOutflow()
        {
            float q = reservoir.GateFlow;
            float opening = Mathf.Min(reservoir.GateOpeningMeters, reservoir.Level);

            if (gateJet != null)
            {
                var emission = gateJet.emission;
                emission.rateOverTime = q * jetParticlesPerCubicMeter;

                var main = gateJet.main;
                float v = Mathf.Max(0.5f, reservoir.JetVelocity * jetVisualSpeedFactor);
                main.startSpeed = new ParticleSystem.MinMaxCurve(v * 0.9f, v * 1.1f);

                var shape = gateJet.shape;
                float height = Mathf.Max(0.05f, opening);
                shape.scale = new Vector3(2.9f, height, 0.05f);
                Vector3 p = gateJet.transform.localPosition;
                p.y = gateSillY + 0.5f * height;
                gateJet.transform.localPosition = p;
            }

            // Downstream channel: normal-depth-like response (depth grows with Q^0.6).
            float qTotalInChannel = q;
            float depth = 0.04f + 0.32f * Mathf.Pow(Mathf.Max(0f, qTotalInChannel), 0.6f);
            if (channelWater != null)
            {
                Vector3 s = channelWater.localScale;
                s.y = depth;
                channelWater.localScale = s;
                Vector3 p = channelWater.localPosition;
                p.y = channelFloorY + 0.5f * depth;
                channelWater.localPosition = p;
            }

            if (channelFoam != null)
            {
                float velocity = qTotalInChannel / Mathf.Max(0.05f, channelWidth * depth);
                velocity = Mathf.Clamp(velocity, 0.3f, 8f);

                var main = channelFoam.main;
                main.startSpeed = velocity;
                main.startLifetime = Mathf.Min(30f, channelLength / velocity);

                var emission = channelFoam.emission;
                emission.rateOverTime = qTotalInChannel > 0.05f ? 6f + 2.5f * qTotalInChannel : 0f;

                Vector3 p = channelFoam.transform.localPosition;
                p.y = channelFloorY + depth + 0.03f;
                channelFoam.transform.localPosition = p;
            }
        }

        private void UpdateSpillway()
        {
            if (spillwayFlow == null) return;
            var emission = spillwayFlow.emission;
            emission.rateOverTime = reservoir.SpillwayFlow * spillParticlesPerCubicMeter;

            var main = spillwayFlow.main;
            float head = Mathf.Max(0f, reservoir.Level - reservoir.spillwayCrest);
            float v = 0.8f + 2.5f * Mathf.Sqrt(head);
            main.startSpeed = new ParticleSystem.MinMaxCurve(v * 0.8f, v * 1.2f);
        }

        private void UpdateLamp()
        {
            if (alarmLamp == null) return;
            Color c = normalColor;
            if (reservoir.SpillwayFlow > 0.001f || reservoir.IsOvertopping) c = alarmColor;
            else if (reservoir.Level >= warningLevel) c = warningColor;

            alarmLamp.GetPropertyBlock(lampBlock);
            lampBlock.SetColor("_Color", c);
            lampBlock.SetColor("_EmissionColor", c * 2f);
            alarmLamp.SetPropertyBlock(lampBlock);
        }
    }
}
