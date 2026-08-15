using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 左键按下立刻攻击,按住超过阈值再抓取。右键投掷。
    /// </summary>
    public class FAnnequinMouseActions : MonoBehaviour
    {
        [Tooltip("按住超过此时长再抓取(秒)")]
        public float LongPressSeconds = 0.45f;

        public int MouseButton = 0;

        public System.Action OnShortPressAttack;
        public System.Action OnLongPressGrab;
        public System.Action OnRightClickRelease;

        bool holding;
        bool grabFired;
        float holdTime;

        void Update()
        {
            if (Input.GetMouseButtonDown(1))
                OnRightClickRelease?.Invoke();

            if (Input.GetMouseButtonDown(MouseButton))
            {
                holding = true;
                grabFired = false;
                holdTime = 0f;
                OnShortPressAttack?.Invoke();
            }

            if (!holding) return;

            holdTime += Time.deltaTime;
            if (!grabFired && holdTime >= LongPressSeconds)
            {
                grabFired = true;
                OnLongPressGrab?.Invoke();
            }

            if (Input.GetMouseButtonUp(MouseButton))
            {
                holding = false;
                grabFired = false;
                holdTime = 0f;
            }
        }

        public void CancelHold()
        {
            holding = false;
            grabFired = false;
            holdTime = 0f;
        }
    }
}
