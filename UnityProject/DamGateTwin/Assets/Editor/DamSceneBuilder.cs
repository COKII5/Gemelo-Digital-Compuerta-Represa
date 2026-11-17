using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DamTwin.EditorTools
{
    /// <summary>
    /// Builds the whole dam scene procedurally (geometry, materials, particles, labels and
    /// the DigitalTwin systems) and saves it to Assets/Scenes/DamGateTwin.unity.
    /// It runs automatically the first time the project is opened, and can be re-run
    /// from the menu: Digital Twin > Rebuild Dam Scene.
    ///
    /// World layout (1 unit = 1 m):
    ///   Reservoir   x [-10, 10], z [-20, 0], floor y = 0 (20 m x 20 m = 400 m2)
    ///   Dam         x [-12, 12], z [0, 3], crest y = 11, spillway notch x [6, 10] at y = 9
    ///   Sluice gate x [-1.5, 1.5], sill y = 0, max opening 2 m
    ///   Channel     x [-3, 3], z [3, 43]
    /// The near side of the reservoir (x = +10) is a glass wall: cut-away view with a staff gauge.
    /// </summary>
    [InitializeOnLoad]
    public static class DamSceneBuilder
    {
        public const string SceneFolder = "Assets/Scenes";
        public const string ScenePath = SceneFolder + "/DamGateTwin.unity";
        private const string MaterialFolder = "Assets/Materials";

        private static readonly Quaternion FaceDefaultCamera = Quaternion.Euler(0f, -132f, 0f);
        private static readonly Quaternion FacePlusX = Quaternion.Euler(0f, -90f, 0f);

        private static Font font;

        static DamSceneBuilder()
        {
            EditorApplication.delayCall += AutoBuildOrOpen;
        }

        private static void AutoBuildOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isBatchMode) return;

            if (!File.Exists(ScenePath))
            {
                Build();
                return;
            }

            Scene active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Digital Twin/Rebuild Dam Scene")]
        public static void RebuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Materials");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- Materials --------------------------------------------------------------------
            Material grass = Opaque("Grass", new Color(0.33f, 0.47f, 0.25f), 0f, 0.1f);
            Material rock = Opaque("Rock", new Color(0.45f, 0.40f, 0.34f), 0f, 0.15f);
            Material sand = Opaque("ReservoirBed", new Color(0.50f, 0.45f, 0.36f), 0f, 0.1f);
            Material concrete = Opaque("Concrete", new Color(0.74f, 0.73f, 0.70f), 0f, 0.2f);
            Material darkConcrete = Opaque("ConcreteDark", new Color(0.46f, 0.46f, 0.46f), 0f, 0.2f);
            Material steel = Opaque("Steel", new Color(0.30f, 0.34f, 0.40f), 0.75f, 0.55f);
            Material servoBlue = Opaque("ServoBlue", new Color(0.15f, 0.35f, 0.85f), 0.1f, 0.5f);
            Material red = Opaque("MarkerRed", new Color(0.85f, 0.15f, 0.10f), 0f, 0.4f);
            Material yellow = Opaque("MarkerYellow", new Color(1.00f, 0.82f, 0.10f), 0f, 0.4f);
            Material white = Opaque("White", new Color(0.95f, 0.95f, 0.95f), 0f, 0.3f);
            Material lamp = Emissive("AlarmLamp", new Color(0.15f, 0.85f, 0.3f));
            Material water = Transparent("Water", new Color(0.12f, 0.42f, 0.72f, 0.62f), 0.92f);
            Material channelWater = Transparent("ChannelWater", new Color(0.20f, 0.52f, 0.78f, 0.70f), 0.92f);
            Material glass = Transparent("Glass", new Color(0.80f, 0.92f, 1.00f, 0.12f), 0.95f);

            // ---- Environment ------------------------------------------------------------------
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.color = new Color(1f, 0.96f, 0.88f);
            lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = light;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.75f, 0.82f, 0.9f);
            RenderSettings.fogStartDistance = 90f;
            RenderSettings.fogEndDistance = 260f;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();
            var orbit = camGo.AddComponent<CameraOrbit>();
            Quaternion camRot = Quaternion.Euler(orbit.pitch, orbit.yaw, 0f);
            camGo.transform.SetPositionAndRotation(orbit.target - camRot * Vector3.forward * orbit.distance, camRot);

            var world = new GameObject("World").transform;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(world, false);
            ground.transform.localPosition = new Vector3(0f, 0f, 10f);
            ground.transform.localScale = new Vector3(16f, 1f, 16f);
            ground.GetComponent<Renderer>().sharedMaterial = grass;

            // Valley banks
            Box("LeftBank", world, new Vector3(-27f, 6f, -8f), new Vector3(32f, 12f, 36f), grass);
            Box("BackHill", world, new Vector3(0f, 6.5f, -34f), new Vector3(70f, 13f, 24f), grass);
            Box("RightBankDownstream", world, new Vector3(24f, 1.5f, 12f), new Vector3(20f, 3f, 14f), grass);

            // ---- Reservoir ---------------------------------------------------------------------
            var reservoirRoot = new GameObject("Reservoir").transform;
            reservoirRoot.SetParent(world, false);
            Box("Bed", reservoirRoot, new Vector3(0f, -0.24f, -10f), new Vector3(21.5f, 0.5f, 21.5f), sand);
            Box("BackWall", reservoirRoot, new Vector3(0f, 5.5f, -20.75f), new Vector3(23f, 11f, 1.5f), rock);
            Box("LeftWall", reservoirRoot, new Vector3(-10.75f, 5.5f, -10f), new Vector3(1.5f, 11f, 21.5f), rock);
            Box("GlassWall", reservoirRoot, new Vector3(10.1f, 5.5f, -10f), new Vector3(0.2f, 11f, 20.2f), glass, false);
            Box("GlassFrameTop", reservoirRoot, new Vector3(10.1f, 11.05f, -10.2f), new Vector3(0.35f, 0.15f, 20.6f), steel);
            Box("GlassFrameBottom", reservoirRoot, new Vector3(10.1f, 0.05f, -10.2f), new Vector3(0.35f, 0.15f, 20.6f), steel);
            Box("GlassFrameBack", reservoirRoot, new Vector3(10.1f, 5.5f, -20.4f), new Vector3(0.35f, 11f, 0.3f), steel);
            Box("GlassFrameMid", reservoirRoot, new Vector3(10.1f, 5.5f, -10.5f), new Vector3(0.3f, 11f, 0.15f), steel);
            Label("CutLabel", reservoirRoot, "Corte transversal del embalse (pared de vidrio)", new Vector3(10.4f, 11.8f, -14f), 0.55f,
                  new Color(0.15f, 0.2f, 0.28f), TextAnchor.MiddleCenter, FacePlusX, false);

            var waterGo = Box("ReservoirWater", reservoirRoot, new Vector3(0f, 3.02f, -10f), new Vector3(19.9f, 6f, 19.9f), water, false);
            waterGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Staff gauge on the glass
            var staff = new GameObject("StaffGauge").transform;
            staff.SetParent(reservoirRoot, false);
            Box("Pole", staff, new Vector3(10.24f, 5.5f, -1.0f), new Vector3(0.03f, 11f, 0.06f), white, false);
            for (int m = 0; m <= 11; m++)
            {
                bool major = m % 5 == 0;
                Box("Tick_" + m, staff, new Vector3(10.25f, m + 0.02f, -1.0f), new Vector3(0.04f, 0.05f, major ? 0.9f : 0.5f), white, false);
                Label("TickLabel_" + m, staff, m + " m", new Vector3(10.3f, m + 0.02f, -1.55f), 0.38f,
                      Color.white, TextAnchor.MiddleRight, FacePlusX, false);
            }
            Box("CrestLine", staff, new Vector3(10.27f, 9.02f, -10f), new Vector3(0.03f, 0.07f, 20.2f), red, false);
            Label("CrestLabel", staff, "Cresta del vertedero 9 m", new Vector3(10.32f, 9.4f, -17.5f), 0.42f,
                  new Color(0.9f, 0.2f, 0.15f), TextAnchor.MiddleCenter, FacePlusX, false);

            var levelIndicator = new GameObject("LevelIndicator").transform;
            levelIndicator.SetParent(staff, false);
            levelIndicator.localPosition = new Vector3(10.29f, 6.02f, 0f);
            Box("Line", levelIndicator, new Vector3(0f, 0f, -10f), new Vector3(0.03f, 0.05f, 20.2f), yellow, false);
            var levelLabel = Label("LevelLabel", levelIndicator, "h = 6.00 m", new Vector3(0.05f, 0.4f, -6f), 0.55f,
                                   new Color(1f, 0.85f, 0.1f), TextAnchor.MiddleCenter, FacePlusX, false);

            // Inflow (constant Qin)
            var inflowRoot = new GameObject("Inflow").transform;
            inflowRoot.SetParent(world, false);
            var pipe = Cylinder("InflowPipe", inflowRoot, new Vector3(-6f, 10.5f, -21.2f), new Vector3(1.2f, 1.5f, 1.2f), Quaternion.Euler(90f, 0f, 0f), darkConcrete);
            pipe.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            var inflowPs = Particles("InflowStream", inflowRoot, new Vector3(-6f, 10.5f, -19.75f), Quaternion.identity,
                                     new Vector3(1.0f, 0.6f, 0.05f), new Color(0.75f, 0.88f, 1f, 0.85f), 0.35f, 0.6f, 1f, 2000, false);
            var inflowMain = inflowPs.main;
            inflowMain.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.2f);
            Label("InflowLabel", inflowRoot, "Entrada Qin constante", new Vector3(-6f, 12.6f, -20f), 0.6f,
                  new Color(0.12f, 0.2f, 0.35f), TextAnchor.MiddleCenter, FaceDefaultCamera, true);

            // ---- Dam ---------------------------------------------------------------------------
            var dam = new GameObject("Dam").transform;
            dam.SetParent(world, false);
            Box("BlockLeft", dam, new Vector3(-6.75f, 5.5f, 1.5f), new Vector3(10.5f, 11f, 3f), concrete);
            Box("BlockAboveGate", dam, new Vector3(0f, 6.5f, 1.5f), new Vector3(3f, 9f, 3f), concrete);
            Box("BlockCenterRight", dam, new Vector3(3.75f, 5.5f, 1.5f), new Vector3(4.5f, 11f, 3f), concrete);
            Box("SpillwayBlock", dam, new Vector3(8f, 4.5f, 1.5f), new Vector3(4f, 9f, 3f), darkConcrete);
            Box("BlockRight", dam, new Vector3(11f, 5.5f, 1.5f), new Vector3(2f, 11f, 3f), concrete);
            Box("GateTunnelFloor", dam, new Vector3(0f, -0.04f, 1.5f), new Vector3(3f, 0.1f, 3f), darkConcrete);
            Box("GuideLeft", dam, new Vector3(-1.7f, 2.8f, -0.25f), new Vector3(0.25f, 5.6f, 0.45f), steel);
            Box("GuideRight", dam, new Vector3(1.7f, 2.8f, -0.25f), new Vector3(0.25f, 5.6f, 0.45f), steel);
            Box("SpillwayApron", dam, new Vector3(8f, 0.03f, 5f), new Vector3(5f, 0.1f, 4f), darkConcrete);
            Label("SpillwayLabel", dam, "Vertedero (cresta 9 m)", new Vector3(8f, 10.4f, 3.2f), 0.6f,
                  new Color(0.12f, 0.2f, 0.35f), TextAnchor.MiddleCenter, FaceDefaultCamera, true);

            var gatePlate = Box("GatePlate", dam, new Vector3(0f, 1.2f, -0.25f), new Vector3(3.2f, 2.4f, 0.2f), steel);
            var gateStem = Box("GateStem", dam, new Vector3(0f, 7.3f, -0.25f), new Vector3(0.18f, 9.8f, 0.18f), steel, false);

            // Hoist with the servo-driven wheel (shows the reported shaft angle)
            var hoist = new GameObject("Hoist").transform;
            hoist.SetParent(dam, false);
            Box("Platform", hoist, new Vector3(0f, 11.1f, -0.3f), new Vector3(4.4f, 0.2f, 1.4f), darkConcrete);
            Box("PostLeft", hoist, new Vector3(-1.9f, 12.2f, -0.25f), new Vector3(0.25f, 2.1f, 0.25f), steel);
            Box("PostRight", hoist, new Vector3(1.9f, 12.2f, -0.25f), new Vector3(0.25f, 2.1f, 0.25f), steel);
            Box("CrossBeam", hoist, new Vector3(0f, 13.3f, -0.25f), new Vector3(4.05f, 0.2f, 0.3f), steel);
            Box("ServoHousing", hoist, new Vector3(0.15f, 12.2f, -0.25f), new Vector3(0.6f, 0.7f, 0.7f), servoBlue);

            var wheel = new GameObject("HoistWheel").transform;
            wheel.SetParent(hoist, false);
            wheel.localPosition = new Vector3(0.55f, 12.2f, -0.25f);
            Cylinder("Disc", wheel, Vector3.zero, new Vector3(1.6f, 0.06f, 1.6f), Quaternion.Euler(0f, 0f, 90f), steel);
            Box("Pointer", wheel, new Vector3(0.09f, 0.4f, 0f), new Vector3(0.06f, 0.8f, 0.14f), red, false);
            Label("Dial0", hoist, "0°", new Vector3(0.7f, 13.2f, -0.25f), 0.28f, Color.white, TextAnchor.MiddleCenter, FacePlusX, false);
            Label("Dial45", hoist, "45°", new Vector3(0.7f, 12.92f, 0.47f), 0.28f, Color.white, TextAnchor.MiddleCenter, FacePlusX, false);
            Label("Dial90", hoist, "90°", new Vector3(0.7f, 12.2f, 0.82f), 0.28f, Color.white, TextAnchor.MiddleCenter, FacePlusX, false);
            var gateLabel = Label("GateLabel", hoist, "Compuerta", new Vector3(0f, 14.3f, -0.25f), 0.7f,
                                  new Color(0.12f, 0.2f, 0.35f), TextAnchor.MiddleCenter, FaceDefaultCamera, true);

            // Alarm lamp
            Box("LampPost", dam, new Vector3(-11f, 11.6f, 1.5f), new Vector3(0.15f, 1.2f, 0.15f), steel, false);
            var lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lampGo.name = "AlarmLamp";
            lampGo.transform.SetParent(dam, false);
            lampGo.transform.localPosition = new Vector3(-11f, 12.4f, 1.5f);
            lampGo.transform.localScale = Vector3.one * 0.7f;
            lampGo.GetComponent<Renderer>().sharedMaterial = lamp;
            Object.DestroyImmediate(lampGo.GetComponent<Collider>());

            // ---- Downstream channel --------------------------------------------------------------
            var channel = new GameObject("Channel").transform;
            channel.SetParent(world, false);
            Box("ChannelBed", channel, new Vector3(0f, -0.04f, 23f), new Vector3(6f, 0.1f, 40f), darkConcrete);
            Box("ChannelWallLeft", channel, new Vector3(-3.15f, 1.2f, 23f), new Vector3(0.3f, 2.4f, 40f), concrete);
            Box("ChannelWallRight", channel, new Vector3(3.15f, 1.2f, 23f), new Vector3(0.3f, 2.4f, 40f), concrete);
            var channelWaterGo = Box("ChannelWater", channel, new Vector3(0f, 0.5f, 23f), new Vector3(5.95f, 1f, 40f), channelWater, false);
            channelWaterGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Label("OutflowLabel", channel, "Caudal de salida", new Vector3(0f, 3.4f, 9f), 0.6f,
                  new Color(0.12f, 0.2f, 0.35f), TextAnchor.MiddleCenter, FaceDefaultCamera, true);

            var jet = Particles("GateJet", channel, new Vector3(0f, 0.5f, 3.05f), Quaternion.identity,
                                new Vector3(2.9f, 1f, 0.05f), new Color(0.78f, 0.9f, 1f, 0.85f), 0.25f, 0.55f, 1f, 5000, true);
            var jetMain = jet.main;
            jetMain.startLifetime = 1.4f;

            var foam = Particles("ChannelFoam", channel, new Vector3(0f, 0.6f, 4f), Quaternion.identity,
                                 new Vector3(5.2f, 0.02f, 0.3f), new Color(1f, 1f, 1f, 0.75f), 0.12f, 0.28f, 0f, 3000, false);

            var spill = Particles("SpillwayFlow", dam, new Vector3(8f, 9.1f, 3.05f), Quaternion.identity,
                                  new Vector3(3.9f, 0.12f, 0.05f), new Color(0.8f, 0.92f, 1f, 0.85f), 0.25f, 0.5f, 1f, 4000, true);
            var spillMain = spill.main;
            spillMain.startLifetime = 1.8f;

            // ---- Digital twin systems ---------------------------------------------------------------
            var systems = new GameObject("DigitalTwin");
            var twin = systems.AddComponent<TwinController>();
            var reservoir = systems.AddComponent<ReservoirModel>();
            var visuals = systems.AddComponent<DamVisuals>();
            var stability = systems.AddComponent<StabilityMonitor>();
            var logger = systems.AddComponent<ExperimentLogger>();
            var hud = systems.AddComponent<HudController>();

            visuals.twin = twin;
            visuals.reservoir = reservoir;
            visuals.reservoirWater = waterGo.transform;
            visuals.waterBottom = 0.02f;
            visuals.levelLabel = levelLabel;
            visuals.levelMarker = levelIndicator;
            visuals.gatePlate = gatePlate.transform;
            visuals.gatePlateHeight = 2.4f;
            visuals.gateSillY = 0f;
            visuals.gateStem = gateStem.transform;
            visuals.hoistAxleY = 12.2f;
            visuals.hoistWheel = wheel;
            visuals.gateLabel = gateLabel;
            visuals.gateJet = jet;
            visuals.channelWater = channelWaterGo.transform;
            visuals.channelFloorY = 0.02f;
            visuals.channelWidth = 5.95f;
            visuals.channelFoam = foam;
            visuals.channelLength = 38f;
            visuals.inflowStream = inflowPs;
            visuals.inflowOutletY = 10.5f;
            visuals.spillwayFlow = spill;
            visuals.alarmLamp = lampGo.GetComponent<Renderer>();

            stability.twin = twin;
            logger.twin = twin;
            logger.reservoir = reservoir;
            logger.stability = stability;
            hud.twin = twin;
            hud.reservoir = reservoir;
            hud.stability = stability;
            hud.logger = logger;

            // ---- Save --------------------------------------------------------------------------
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = systems;
            Debug.Log("[DamTwin] Scene built and saved to " + ScenePath);
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================
        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static TextMesh Label(string name, Transform parent, string text, Vector3 position, float worldHeight,
                                      Color color, TextAnchor anchor, Quaternion rotation, bool billboard)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;

            const int fontSize = 64;
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.fontSize = fontSize;
            mesh.characterSize = worldHeight * 10f / fontSize;
            mesh.anchor = anchor;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            mesh.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            if (billboard) go.AddComponent<Billboard>();
            return mesh;
        }

        private static ParticleSystem Particles(string name, Transform parent, Vector3 position, Quaternion rotation,
                                                Vector3 boxSize, Color color, float minSize, float maxSize,
                                                float gravity, int maxParticles, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 1.5f;
            main.startSpeed = 3f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;
            shape.randomDirectionAmount = 0.03f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.6f));

            if (collide)
            {
                var collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.quality = ParticleSystemCollisionQuality.Medium;
                collision.dampen = 0.6f;
                collision.bounce = 0.15f;
                collision.lifetimeLoss = 0.35f;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }

        // ---- Materials (Built-in Render Pipeline, Standard shader) ----------------------------------
        private static Material LoadOrCreate(string name)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader == null || material.shader.name != "Standard")
            {
                material.shader = Shader.Find("Standard");
            }
            return material;
        }

        private static Material Opaque(string name, Color color, float metallic, float smoothness)
        {
            var m = LoadOrCreate(name);
            m.SetFloat("_Mode", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = -1;
            m.color = color;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Emissive(string name, Color color)
        {
            var m = Opaque(name, color, 0f, 0.6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", color * 2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Transparent(string name, Color color, float smoothness)
        {
            var m = LoadOrCreate(name);
            m.SetFloat("_Mode", 3f); // Transparent
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.color = color;
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
