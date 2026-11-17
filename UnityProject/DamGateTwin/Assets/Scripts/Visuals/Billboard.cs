using UnityEngine;

namespace DamTwin
{
    /// <summary>Keeps a world-space label facing the main camera.</summary>
    public class Billboard : MonoBehaviour
    {
        private Camera cachedCamera;

        private void LateUpdate()
        {
            if (cachedCamera == null) cachedCamera = Camera.main;
            if (cachedCamera == null) return;
            Vector3 direction = transform.position - cachedCamera.transform.position;
            direction.y = 0f; // stay upright
            if (direction.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(direction);
        }
    }
}
