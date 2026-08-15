using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// Marks the laptop as the round objective and stores the shared tuning values.
    /// The authoritative network game manager should own holder detection and scores.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class KpiComputerObjective : NetworkBehaviour
    {
        [Tooltip("达到这个工作量 KPI 的玩家获胜")]
        [Min(1f)] public float WinningKpi = 99f;

        [Tooltip("持有电脑每秒增加的 KPI")]
        [Min(0.01f)] public float KpiPerHeldSecond = 1f;

        public Rigidbody Body;
        [SyncVar] uint holderNetId;

        NetFAnnequinController serverHolder;

        public uint HolderNetId => holderNetId;
        public bool IsHeld => holderNetId != 0u;

        public override void OnStartServer()
        {
            ResolveBody();
            serverHolder = null;
            holderNetId = 0u;
            SetServerBodyHeld(false);
        }

        public override void OnStartClient()
        {
            ResolveBody();
            if (isServer || Body == null) return;

            // 电脑物理由服务端模拟，纯客户端只回放 NetworkTransform。
            Body.isKinematic = true;
            Body.useGravity = false;
            Body.detectCollisions = false;
        }

        [Server]
        public bool ServerTryClaim(NetFAnnequinController holder)
        {
            if (holder == null) return false;
            if (serverHolder != null && serverHolder != holder) return false;

            serverHolder = holder;
            holderNetId = holder.netId;
            SetServerBodyHeld(true);
            return true;
        }

        [Server]
        public void ServerMoveHeld(Vector3 position, Quaternion rotation)
        {
            if (serverHolder == null) return;
            ResolveBody();

            transform.SetPositionAndRotation(position, rotation);
            if (Body == null) return;
            Body.position = position;
            Body.rotation = rotation;
        }

        [Server]
        public void ServerRelease(NetFAnnequinController holder, Vector3 position, Vector3 velocity)
        {
            if (serverHolder != null && holder != null && serverHolder != holder) return;

            serverHolder = null;
            holderNetId = 0u;
            ResolveBody();
            transform.position = position;
            SetServerBodyHeld(false);
            if (Body == null) return;

            Body.position = position;
            Body.velocity = velocity;
            Body.angularVelocity = Vector3.zero;
        }

        void SetServerBodyHeld(bool held)
        {
            ResolveBody();
            if (Body == null) return;

            if (held)
            {
                if (!Body.isKinematic)
                {
                    Body.velocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                }
                Body.useGravity = false;
                Body.detectCollisions = false;
                Body.isKinematic = true;
            }
            else
            {
                Body.isKinematic = false;
                Body.useGravity = true;
                Body.detectCollisions = true;
                Body.velocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
        }

        void ResolveBody()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
        }

        void Reset()
        {
            ResolveBody();
        }

        void OnValidate()
        {
            ResolveBody();
        }
    }
}
