using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 左键攻击；右键长按拾取，松开右键放下。
    /// </summary>
    public class FAnnequinMouseActions : MonoBehaviour
    {
        [Tooltip("右键按住超过此时长后触发拾取（秒）")]
        public float LongPressSeconds = 0.45f;

        public System.Action OnShortPressAttack;
        public System.Action OnLongPressGrab;
        public System.Action OnRightClickRelease;

        public bool IsPickupButtonHeld => pickupButtonHeld;

        bool pickupButtonHeld;
        bool pickupFired;
        float holdTime;

        void Update()
        {
            if (Input.GetMouseButtonDown(0) && !pickupButtonHeld)
                OnShortPressAttack?.Invoke();

            if (Input.GetMouseButtonDown(1))
            {
                pickupButtonHeld = true;
                pickupFired = false;
                holdTime = 0f;
            }

            if (!pickupButtonHeld) return;

            holdTime += Time.deltaTime;
            if (!pickupFired && holdTime >= LongPressSeconds)
            {
                pickupFired = true;
                OnLongPressGrab?.Invoke();
            }

            if (Input.GetMouseButtonUp(1))
            {
                bool shouldRelease = pickupFired;
                pickupButtonHeld = false;
                pickupFired = false;
                holdTime = 0f;
                if (shouldRelease)
                    OnRightClickRelease?.Invoke();
            }
        }

        public void CancelHold()
        {
            pickupButtonHeld = false;
            pickupFired = false;
            holdTime = 0f;
        }
    }
}
