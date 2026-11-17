using UnityEngine;

namespace DamTwin
{
    /// <summary>
    /// Lumped (0-D) reservoir mass balance:
    ///
    ///     A * dh/dt = Qin - Qgate(h, a) - Qspill(h)
    ///
    ///   Qgate  = Cd * b * a_eff * sqrt(2 g (h - a_eff/2)),  a_eff = min(a, h)   (sluice gate / orifice)
    ///   Qspill = Cw * L * (h - crest)^1.5                   if h > crest        (free spillway)
    ///
    /// The gate opening a = opening01 * aMax comes from the servo angle reported by the device.
    /// </summary>
    [DisallowMultipleComponent]
    public class ReservoirModel : MonoBehaviour
    {
        public const float Gravity = 9.81f;

        [Header("Reservoir")]
        [Tooltip("Constant inflow Qin (m3/s).")]
        public float inflow = 10f;
        [Tooltip("Free-surface area A (m2). The visual basin is 20 m x 20 m = 400 m2.")]
        public float surfaceArea = 400f;
        [Tooltip("Initial water level (m).")]
        public float initialLevel = 6f;
        [Tooltip("Dam crest elevation (m). Above it the dam would be overtopped.")]
        public float damCrest = 11f;

        [Header("Sluice gate")]
        public float gateWidth = 3f;          // b (m)
        public float gateMaxOpening = 2f;     // aMax (m)
        public float dischargeCoefficient = 0.61f;

        [Header("Spillway")]
        public float spillwayCrest = 9f;      // m
        public float spillwayLength = 4f;     // L (m)
        public float weirCoefficient = 1.7f;  // Cw (SI, broad crested)

        [Header("Time")]
        [Tooltip("Time acceleration so level changes are visible in seconds (1 = real time).")]
        public float timeScale = 10f;
        [Tooltip("Maximum integration step in simulated seconds.")]
        public float maxSubstep = 0.05f;

        // ---- State --------------------------------------------------------------------------
        public float Level { get; private set; }
        public float GateOpening01 { get; set; }
        public float GateOpeningMeters => Mathf.Clamp01(GateOpening01) * gateMaxOpening;
        public float GateFlow { get; private set; }
        public float SpillwayFlow { get; private set; }
        public float TotalOutflow => GateFlow + SpillwayFlow;
        public float LevelRate { get; private set; }         // m per simulated second
        public float LevelRateVisible { get; private set; }  // m per real second
        public float JetVelocity { get; private set; }       // m/s at the gate
        public float SimulatedTime { get; private set; }
        public bool IsOvertopping => Level >= damCrest - 1e-3f;

        /// <summary>The simulation waits until the device has reported a state (no data, no twin).</summary>
        public bool Paused { get; set; }

        private void Awake() => ResetLevel();

        public void ResetLevel()
        {
            Level = Mathf.Clamp(initialLevel, 0f, damCrest);
            SimulatedTime = 0f;
            UpdateFlows();
        }

        private void Update()
        {
            if (Paused)
            {
                LevelRate = 0f;
                LevelRateVisible = 0f;
                UpdateFlows();
                return;
            }

            float simDt = Time.deltaTime * timeScale;
            float startLevel = Level;
            SimulatedTime += simDt;

            int steps = Mathf.Max(1, Mathf.CeilToInt(simDt / Mathf.Max(1e-3f, maxSubstep)));
            float h = simDt / steps;
            for (int i = 0; i < steps; i++) Step(h);

            LevelRate = simDt > 0f ? (Level - startLevel) / simDt : 0f;
            LevelRateVisible = Time.deltaTime > 0f ? (Level - startLevel) / Time.deltaTime : 0f;
            UpdateFlows();
        }

        private void Step(float dt)
        {
            float a = GateOpeningMeters;
            float qOut = ComputeGateFlow(Level, a) + ComputeSpillwayFlow(Level);

            // Cannot release more water than is stored plus what comes in during the step.
            float available = inflow + Level * surfaceArea / dt;
            qOut = Mathf.Min(qOut, available);

            Level += (inflow - qOut) / surfaceArea * dt;
            Level = Mathf.Clamp(Level, 0f, damCrest);
        }

        private void UpdateFlows()
        {
            float a = GateOpeningMeters;
            GateFlow = ComputeGateFlow(Level, a);
            SpillwayFlow = ComputeSpillwayFlow(Level);
            float aEff = Mathf.Min(a, Level);
            JetVelocity = aEff > 0f ? Mathf.Sqrt(2f * Gravity * Mathf.Max(0f, Level - 0.5f * aEff)) : 0f;
        }

        public float ComputeGateFlow(float level, float opening)
        {
            float aEff = Mathf.Min(opening, level);
            if (aEff <= 0f) return 0f;
            float head = Mathf.Max(0f, level - 0.5f * aEff);
            return dischargeCoefficient * gateWidth * aEff * Mathf.Sqrt(2f * Gravity * head);
        }

        public float ComputeSpillwayFlow(float level)
        {
            float over = level - spillwayCrest;
            return over > 0f ? weirCoefficient * spillwayLength * Mathf.Pow(over, 1.5f) : 0f;
        }

        /// <summary>
        /// Level at which Qin = Qgate + Qspill for a given opening (bisection; the outflow grows
        /// monotonically with h). Returns damCrest if the dam would be overtopped.
        /// </summary>
        public float ComputeEquilibriumLevel(float opening01)
        {
            float a = Mathf.Clamp01(opening01) * gateMaxOpening;
            float low = 0f, high = damCrest;
            if (inflow - ComputeGateFlow(high, a) - ComputeSpillwayFlow(high) > 0f) return damCrest;

            for (int i = 0; i < 40; i++)
            {
                float mid = 0.5f * (low + high);
                float net = inflow - ComputeGateFlow(mid, a) - ComputeSpillwayFlow(mid);
                if (net > 0f) low = mid; else high = mid;
            }
            return 0.5f * (low + high);
        }
    }
}
