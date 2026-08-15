using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// Marks the laptop as the round objective. Holder and scores are owned by BrawlGameManager.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class KpiComputerObjective : NetworkBehaviour
    {
        [Tooltip("达到这个工作量 KPI 的玩家获胜（旧规则，当前对局按倒计时排名）")]
        [Min(1f)] public float WinningKpi = 99f;

        [Tooltip("兼容字段：实际每 0.5 秒加分由 BrawlGameManager.HoldScorePoints 决定")]
        [Min(1)] public int PointsPerHoldTick = 1;

        [Tooltip("持有电脑每秒增加的 KPI（兼容旧配置）")]
        [Min(0.01f)] public float KpiPerHeldSecond = 1f;

        public Rigidbody Body;
        [SyncVar] uint holderNetId;

        NetFAnnequinController serverHolder;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        bool hasSpawnPose;

        public uint HolderNetId => holderNetId;
        public bool IsHeld => holderNetId != 0u;
        public NetFAnnequinController ServerHolder => serverHolder;
        public int ScorePointsPerTick => Mathf.Max(1, PointsPerHoldTick);

        public override void OnStartServer()
        {
            ResolveBody();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            hasSpawnPose = true;
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
            ApplyLoosePose(position, velocity);
        }

        [Server]
        public void ServerResetToSpawn()
        {
            serverHolder = null;
            holderNetId = 0u;
            Vector3 pos = hasSpawnPose ? spawnPosition : transform.position;
            Quaternion rot = hasSpawnPose ? spawnRotation : transform.rotation;
            transform.SetPositionAndRotation(pos, rot);
            ApplyLoosePose(pos, Vector3.zero);
            if (Body != null) Body.rotation = rot;
        }

        [Server]
        public bool ServerIsBelow(float killY)
        {
            return !IsHeld && transform.position.y < killY;
        }

        void ApplyLoosePose(Vector3 position, Vector3 velocity)
        {
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
                ServerIgnoreAllPlayerCollisions();
            }
        }

        public Vector3 GetPickupPoint(Vector3 from)
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return transform.position;
            return col.ClosestPoint(from);
        }

        [Server]
        public void ServerIgnoreCollisionsWith(NetFAnnequinController player)
        {
            Collider self = GetComponent<Collider>();
            if (self == null || player == null) return;

            Collider[] cols = player.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i] == self) continue;
                Physics.IgnoreCollision(self, cols[i], true);
            }
        }

        [Server]
        public void ServerIgnoreAllPlayerCollisions()
        {
            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
                ServerIgnoreCollisionsWith(player);
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
