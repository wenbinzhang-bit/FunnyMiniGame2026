using FIMSpace.Basics;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 仅本地玩家:把场景主相机接上 FBasic 第三人称轨道相机并跟随自己。
    /// 相机纯本地,不参与网络同步。开局默认捕获鼠标,Esc 释放,再点窗口重新捕获。
    /// </summary>
    public class LocalCameraRig : NetworkBehaviour
    {
        public Vector3 FollowOffset = new Vector3(0f, 1.2f, 0f);
        public Vector2 DistanceRanges = new Vector2(2.5f, 8f);

        [Header("Camera Rotation")]
        [Min(0f)] public float HorizontalRotationSensitivity = 3f;
        [Range(0f, 1f)] public float VerticalRotationSensitivityMultiplier = 0.4f;
        public Vector2 VerticalRotationRanges = new Vector2(2f, 45f);
        [Range(0.1f, 1f)] public float RotationSpeed = 0.65f;

        [Header("Camera Height Protection")]
        [Min(0f)] public float MinimumHeightAboveFollowPoint = 0.1f;

        [Header("Vertical Follow")]
        [Min(0f)] public float VerticalFollowSmoothTime = 0.18f;
        [Min(0f)] public float VerticalFollowDeadZone = 0.15f;

        public override void OnStartLocalPlayer()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            var tpp = cam.GetComponent<FBasic_TPPCameraBehaviour>();
            if (tpp == null) tpp = cam.gameObject.AddComponent<FBasic_TPPCameraBehaviour>();

            tpp.ToFollow = transform;
            tpp.FollowingOffset = FollowOffset;
            tpp.DistanceRanges = DistanceRanges;
            tpp.RotationSensitivity = HorizontalRotationSensitivity;
            tpp.VerticalRotationSensitivityMultiplier = VerticalRotationSensitivityMultiplier;
            tpp.RotationRanges = VerticalRotationRanges;
            tpp.RotationSpeed = RotationSpeed;
            tpp.MinimumHeightAboveFollowPoint = MinimumHeightAboveFollowPoint;
            tpp.VerticalFollowSmoothTime = VerticalFollowSmoothTime;
            tpp.VerticalFollowDeadZone = VerticalFollowDeadZone;
            tpp.LockCursor = true;
            tpp.RightClickToLockCursor = false;
            tpp.enabled = true;

            releasedByUser = false;
            LockCursor();
        }

        bool releasedByUser;

        void Update()
        {
            if (!isLocalPlayer) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                releasedByUser = true;
                UnlockCursor();
                return;
            }

            if (releasedByUser && Application.isFocused && Input.GetMouseButtonDown(0))
            {
                releasedByUser = false;
                LockCursor();
            }
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public override void OnStopLocalPlayer()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            var tpp = cam.GetComponent<FBasic_TPPCameraBehaviour>();
            if (tpp != null && tpp.ToFollow == transform) tpp.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
