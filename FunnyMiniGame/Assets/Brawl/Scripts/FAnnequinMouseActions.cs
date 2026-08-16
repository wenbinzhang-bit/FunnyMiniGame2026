using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 左键攻击；右键长按拾取，松开右键放下。
    /// </summary>
    public class FAnnequinMouseActions : MonoBehaviour
    {
        [Tooltip("右键按住超过此时长后开始持续尝试拾取（秒）")]
        public float LongPressSeconds = 0.2f;

        [Tooltip("首次拾取失败后，继续按住右键的重试间隔（秒）")]
        [Min(0.05f)] public float PickupRetrySeconds = 0.1f;

        public System.Action OnShortPressAttack;
        public System.Action OnLongPressGrab;
        public System.Action OnRightClickRelease;

        public bool IsPickupButtonHeld => pickupButtonHeld;

        bool pickupButtonHeld;
        bool pickupFired;
        float holdTime;
        float pickupRetryTime;

        void Update()
        {
            if (!LocalCameraRig.IsCursorCaptured) return;

            if (Input.GetMouseButtonDown(0) && !pickupButtonHeld)
                OnShortPressAttack?.Invoke();

            if (Input.GetMouseButtonDown(1))
            {
                pickupButtonHeld = true;
                pickupFired = false;
                holdTime = 0f;
                pickupRetryTime = 0f;
            }

            if (!pickupButtonHeld) return;

            holdTime += Time.deltaTime;
            if (!pickupFired && holdTime >= LongPressSeconds)
            {
                pickupFired = true;
                pickupRetryTime = 0f;
                OnLongPressGrab?.Invoke();
            }
            else if (pickupFired)
            {
                pickupRetryTime += Time.deltaTime;
                if (pickupRetryTime >= PickupRetrySeconds)
                {
                    pickupRetryTime = 0f;
                    OnLongPressGrab?.Invoke();
                }
            }

            if (Input.GetMouseButtonUp(1))
            {
                bool shouldRelease = pickupFired;
                pickupButtonHeld = false;
                pickupFired = false;
                holdTime = 0f;
                pickupRetryTime = 0f;
                if (shouldRelease)
                    OnRightClickRelease?.Invoke();
            }
        }

        public void CancelHold()
        {
            pickupButtonHeld = false;
            pickupFired = false;
            holdTime = 0f;
            pickupRetryTime = 0f;
        }
    }
}
