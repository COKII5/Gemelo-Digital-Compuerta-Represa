using UnityEngine;
using UnityEngine.EventSystems;

namespace DamTwin
{
    /// <summary>
    /// Simple orbit camera: right mouse drag = orbit, wheel = zoom,
    /// keys 1/2/3 = preset views (overview / gate / reservoir cut), R = reset.
    /// </summary>
    public class CameraOrbit : MonoBehaviour
    {
        public Vector3 target = new Vector3(0f, 4f, -4f);
        public float distance = 42f;
        public float yaw = -132f;
        public float pitch = 24f;
        public float minDistance = 8f;
        public float maxDistance = 90f;
        public float orbitSpeed = 4f;
        public float zoomSpeed = 6f;
        public float smoothing = 10f;

        private Vector3 homeTarget;
        private float homeDistance, homeYaw, homePitch;
        private Vector3 goalTarget;
        private float goalDistance, goalYaw, goalPitch;

        private void Awake()
        {
            homeTarget = goalTarget = target;
            homeDistance = goalDistance = distance;
            homeYaw = goalYaw = yaw;
            homePitch = goalPitch = pitch;
            Apply();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (Input.GetMouseButton(1))
            {
                goalYaw += Input.GetAxis("Mouse X") * orbitSpeed;
                goalPitch = Mathf.Clamp(goalPitch - Input.GetAxis("Mouse Y") * orbitSpeed, 3f, 85f);
            }

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (!overUi && Mathf.Abs(wheel) > 0.001f)
                goalDistance = Mathf.Clamp(goalDistance * (1f - wheel * zoomSpeed * 0.1f), minDistance, maxDistance);

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.R))
                SetGoal(homeTarget, homeDistance, homeYaw, homePitch);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                SetGoal(new Vector3(0f, 2.5f, 2f), 16f, -150f, 14f);   // gate and outflow
            if (Input.GetKeyDown(KeyCode.Alpha3))
                SetGoal(new Vector3(5f, 5f, -10f), 30f, -90f, 8f);     // reservoir cut (glass wall)
#endif
            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            target = Vector3.Lerp(target, goalTarget, k);
            distance = Mathf.Lerp(distance, goalDistance, k);
            yaw = Mathf.LerpAngle(yaw, goalYaw, k);
            pitch = Mathf.Lerp(pitch, goalPitch, k);
            Apply();
        }

        private void SetGoal(Vector3 t, float d, float y, float p)
        {
            goalTarget = t;
            goalDistance = d;
            goalYaw = y;
            goalPitch = p;
        }

        private void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }
    }
}
