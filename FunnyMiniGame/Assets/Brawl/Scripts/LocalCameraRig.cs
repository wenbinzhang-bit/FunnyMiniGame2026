using FIMSpace.Basics;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Brawl
{
    /// <summary>
    /// 仅本地玩家:把场景主相机接上 FBasic 第三人称轨道相机并跟随自己。
    /// 相机纯本地,不参与网络同步。开局默认捕获鼠标,Esc 释放；Alt 或点空白处重新捕获，点 UI 不抢鼠标。
    /// </summary>
    public class LocalCameraRig : NetworkBehaviour
    {
        public static bool IsCursorCaptured { get; private set; } = true;
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
            SceneManager.sceneLoaded += OnSceneLoaded;
            BindFollowCamera();
            releasedByUser = false;
            LockCursor();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!isLocalPlayer) return;
            BindFollowCamera();
        }

        void BindFollowCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            BrawlSession.AdoptActor(cam.gameObject);
            DisableForeignCameras(cam);

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
            tpp.LockCursor = false;
            tpp.RightClickToLockCursor = false;
            tpp.HandleCursorHotkeys = false;
            tpp.enabled = true;
            tppCamera = tpp;
        }

        static void DisableForeignCameras(Camera keep)
        {
            Camera[] cameras = FindObjectsOfType<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null || cam == keep) continue;
                if (cam.targetTexture != null) continue;
                cam.enabled = false;
                AudioListener listener = cam.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = false;
            }
        }

        FBasic_TPPCameraBehaviour tppCamera;

        bool releasedByUser;
        bool wasRoundEnd;

        void Update()
        {
            if (!isLocalPlayer) return;

            BrawlGameManager gm = BrawlGameManager.Instance;
            bool unlockForUi = gm != null && (gm.HudIsRoundEnd || gm.HudIsFinalKpi);
            if (unlockForUi)
            {
                if (IsCursorCaptured)
                {
                    releasedByUser = true;
                    UnlockCursor();
                }
                wasRoundEnd = true;
                return;
            }

            if (wasRoundEnd)
            {
                wasRoundEnd = false;
                releasedByUser = false;
                LockCursor();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                releasedByUser = true;
                UnlockCursor();
                return;
            }

            if (!releasedByUser || !Application.isFocused) return;

            bool pressAlt = Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt);
            bool clickEmpty = Input.GetMouseButtonDown(0) && !IsPointerOverUi();
            if (pressAlt || clickEmpty)
            {
                releasedByUser = false;
                LockCursor();
            }
        }

        void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            IsCursorCaptured = true;
            if (tppCamera != null) tppCamera.SetRotateCamera(true);
        }

        void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            IsCursorCaptured = false;
            if (tppCamera != null) tppCamera.SetRotateCamera(false);
        }

        static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        public override void OnStopLocalPlayer()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Camera cam = Camera.main;
            if (cam == null) return;

            var tpp = cam.GetComponent<FBasic_TPPCameraBehaviour>();
            if (tpp != null && tpp.ToFollow == transform) tpp.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            IsCursorCaptured = false;
        }
    }
}
