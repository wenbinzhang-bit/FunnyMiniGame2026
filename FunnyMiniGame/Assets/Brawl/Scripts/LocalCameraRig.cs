using FIMSpace.Basics;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 仅本地玩家:把场景主相机接上 FBasic 第三人称轨道相机并跟随自己。
    /// 相机纯本地,不参与网络同步。右键锁定鼠标旋转视角,Tab/Esc 释放。
    /// </summary>
    public class LocalCameraRig : NetworkBehaviour
    {
        public Vector3 FollowOffset = new Vector3(0f, 1.2f, 0f);
        public Vector2 DistanceRanges = new Vector2(2.5f, 8f);

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
            tpp.LockCursor = true;
            tpp.enabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
