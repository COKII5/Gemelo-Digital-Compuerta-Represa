using UnityEngine;
using UnityEngine.EventSystems;

namespace DamTwin
{
    /// <summary>Tracks whether the user is currently holding a slider (pointer down).</summary>
    public class PointerHoldTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool IsHeld { get; private set; }
        public void OnPointerDown(PointerEventData eventData) => IsHeld = true;
        public void OnPointerUp(PointerEventData eventData) => IsHeld = false;
        private void OnDisable() => IsHeld = false;
    }
}
