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
    [DefaultExecutionOrder(200)]
    public sealed class KpiComputerObjective : NetworkBehaviour
    {
        [Tooltip("达到这个工作量 KPI 的玩家获胜（旧规则，当前对局按倒计时排名）")]
        [Min(1f)] public float WinningKpi = 99f;

        [Tooltip("兼容字段：实际每 0.5 秒加分由 BrawlGameManager.HoldScorePoints 决定")]
        [Min(1)] public int PointsPerHoldTick = 1;

        [Tooltip("持有电脑每秒增加的 KPI（兼容旧配置）")]
        [Min(0.01f)] public float KpiPerHeldSecond = 1f;

        public Rigidbody Body;
        [Tooltip("使用拾取物闪烁材质的 Renderer；为空时会自动查找子物体")]
        public Renderer[] PickupRenderers;

        [Header("Held Typing Audio")]
        [Tooltip("持有电脑时循环播放；为空时从 Resources/Audio/ComputerTypingLoop 加载")]
        public AudioClip TypingLoopClip;
        [Range(0f, 1f)] public float TypingLoopVolume = 0.55f;
        [Min(0f)] public float TypingMinDistance = 1.5f;
        [Min(0.1f)] public float TypingMaxDistance = 14f;

        [SyncVar(hook = nameof(OnHolderNetIdChanged))] uint holderNetId;
        [SyncVar] bool thrown;

        NetFAnnequinController serverHolder;
        MaterialPropertyBlock pickupPropertyBlock;
        AudioSource typingLoopSource;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        bool hasSpawnPose;
        uint throwerNetId;
        float thrownUntil;
        float throwerImmunityUntil;
        bool throwerCollisionRestored;
        readonly Collider[] hitBuffer = new Collider[24];
        static readonly int PickupPulseEnabledId = Shader.PropertyToID("_PickupPulseEnabled");

        public uint HolderNetId => holderNetId;
        public bool IsHeld => holderNetId != 0u;
        public bool IsThrown => thrown;
        public uint ThrowerNetId => throwerNetId;
        public NetFAnnequinController ServerHolder => serverHolder;
        public int ScorePointsPerTick => Mathf.Max(1, PointsPerHoldTick);
        const float ThrowLifetimeSeconds = 2.6f;
        const float ThrowerImmunitySeconds = 0.4f;
        const float MinThrowHitSpeed = 2.2f;
        const float ClearThrownSpeed = 1.15f;
        const float ThrowHitRadius = 0.78f;

        public override void OnStartServer()
        {
            ResolveBody();
            ResolvePickupRenderers();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            hasSpawnPose = true;
            serverHolder = null;
            holderNetId = 0u;
            ClearThrownState();
            SetServerBodyHeld(false);
            ApplyPickupPulse(true);
        }

        public override void OnStartClient()
        {
            ResolveBody();
            ResolvePickupRenderers();
            ApplyPickupPulse(!IsHeld);
            SetTypingLoop(IsHeld);
            if (isServer || Body == null) return;

            // 电脑物理由服务端模拟，纯客户端只回放 NetworkTransform。
            Body.isKinematic = true;
            Body.useGravity = false;
            Body.detectCollisions = false;
        }

        public override void OnStopClient()
        {
            SetTypingLoop(false);
            base.OnStopClient();
        }

        void OnHolderNetIdChanged(uint previousHolderNetId, uint newHolderNetId)
        {
            ApplyPickupPulse(newHolderNetId == 0u);
            if (isClient)
                SetTypingLoop(newHolderNetId != 0u);
        }

        void SetTypingLoop(bool held)
        {
            if (!held)
            {
                if (typingLoopSource != null && typingLoopSource.isPlaying)
                    typingLoopSource.Stop();
                return;
            }

            EnsureTypingLoopSource();
            if (typingLoopSource == null || TypingLoopClip == null || typingLoopSource.isPlaying)
                return;

            typingLoopSource.clip = TypingLoopClip;
            typingLoopSource.Play();
        }

        void EnsureTypingLoopSource()
        {
            if (TypingLoopClip == null)
                TypingLoopClip = Resources.Load<AudioClip>("Audio/ComputerTypingLoop");

            if (typingLoopSource == null)
                typingLoopSource = gameObject.AddComponent<AudioSource>();

            typingLoopSource.playOnAwake = false;
            typingLoopSource.loop = true;
            typingLoopSource.volume = TypingLoopVolume;
            typingLoopSource.spatialBlend = 1f;
            typingLoopSource.dopplerLevel = 0f;
            typingLoopSource.minDistance = Mathf.Max(0f, TypingMinDistance);
            typingLoopSource.maxDistance = Mathf.Max(typingLoopSource.minDistance + 0.1f, TypingMaxDistance);
            typingLoopSource.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        void FixedUpdate()
        {
            if (!isServer || IsHeld) return;
            if (!thrown) return;

            if (!throwerCollisionRestored && Time.time >= throwerImmunityUntil)
            {
                NetFAnnequinController thrower = FindPlayerByNetId(throwerNetId);
                if (thrower != null)
                    ServerSetIgnorePlayer(thrower, false);
                throwerCollisionRestored = true;
            }

            if (Time.time >= thrownUntil || CurrentSpeed() < ClearThrownSpeed)
            {
                if (!ServerTryReturnThrownComputer())
                {
                    ClearThrownState();
                    ServerIgnoreAllPlayerCollisions();
                }
                return;
            }

            ServerTryHitNearbyPlayers();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!isServer || !thrown || IsHeld || collision == null) return;
            ServerTryForceCatch(collision.collider);
        }

        void OnCollisionStay(Collision collision)
        {
            if (!isServer || !thrown || IsHeld || collision == null) return;
            ServerTryForceCatch(collision.collider);
        }

        void LateUpdate()
        {
            if (!IsHeld) return;

            // NetworkTransformReliable 必须始终保持启用。运行中禁用/启用会重置其增量压缩状态，
            // 导致释放电脑时客户端用出生点作为错误基准。这里在 LateUpdate 覆盖显示位置即可。
            NetFAnnequinController holder = FindHolder();
            if (holder == null) return;

            holder.GetComputerHoldPose(out Vector3 holdPosition, out Quaternion holdRotation);
            transform.SetPositionAndRotation(holdPosition, holdRotation);
            if (Body != null)
            {
                Body.position = holdPosition;
                Body.rotation = holdRotation;
            }
        }

        [Server]
        public bool ServerTransferTo(NetFAnnequinController from, NetFAnnequinController to, bool applyCatchStun)
        {
            if (to == null || to.IsDead || to.IsGrabbed) return false;
            if (from != null)
                from.ServerDetachComputer();

            serverHolder = to;
            holderNetId = to.netId;
            ClearThrownState();
            SetServerBodyHeld(true);
            to.ServerForceReceiveComputer(this, applyCatchStun);
            return true;
        }

        [Server]
        public bool ServerTryClaim(NetFAnnequinController holder)
        {
            if (holder == null) return false;
            if (serverHolder != null && serverHolder != holder) return false;

            serverHolder = holder;
            holderNetId = holder.netId;
            ClearThrownState();
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
            ClearThrownState();
            ApplyLoosePose(position, velocity);
        }

        [Server]
        public void ServerThrow(NetFAnnequinController thrower, Vector3 position, Vector3 velocity)
        {
            serverHolder = null;
            holderNetId = 0u;
            throwerNetId = thrower != null ? thrower.netId : 0u;
            thrownUntil = Time.time + ThrowLifetimeSeconds;
            throwerImmunityUntil = Time.time + ThrowerImmunitySeconds;
            throwerCollisionRestored = thrower == null;
            thrown = true;
            ApplyLoosePose(position, velocity, false);
            if (Body != null)
                Body.angularVelocity = Vector3.Cross(velocity.normalized, Vector3.up) * 10f;
            ServerEnablePlayerCollisionsExcept(thrower);
        }

        [Server]
        public void ServerResetToSpawn()
        {
            serverHolder = null;
            holderNetId = 0u;
            ClearThrownState();
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

        void ApplyLoosePose(Vector3 position, Vector3 velocity, bool ignoreAllPlayers = true)
        {
            ResolveBody();
            transform.position = position;
            SetServerBodyHeld(false, ignoreAllPlayers);
            if (Body == null) return;

            Body.position = position;
            Body.velocity = velocity;
            Body.angularVelocity = Vector3.zero;
        }

        void SetServerBodyHeld(bool held, bool ignoreAllPlayers = true)
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
                if (ignoreAllPlayers)
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
            ServerSetIgnorePlayer(player, true);
        }

        [Server]
        public void ServerIgnoreAllPlayerCollisions()
        {
            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
                ServerSetIgnorePlayer(player, true);
        }

        [Server]
        void ServerEnablePlayerCollisionsExcept(NetFAnnequinController except)
        {
            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
                ServerSetIgnorePlayer(player, player == except);
        }

        [Server]
        void ServerSetIgnorePlayer(NetFAnnequinController player, bool ignore)
        {
            Collider self = GetComponent<Collider>();
            if (self == null || player == null) return;

            Collider[] cols = player.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i] == self) continue;
                Physics.IgnoreCollision(self, cols[i], ignore);
            }
        }

        [Server]
        void ServerTryHitNearbyPlayers()
        {
            if (CurrentSpeed() < MinThrowHitSpeed) return;

            int count = Physics.OverlapSphereNonAlloc(transform.position, ThrowHitRadius, hitBuffer,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (ServerTryForceCatch(hitBuffer[i]))
                    return;
            }
        }

        [Server]
        bool ServerTryForceCatch(Collider hitCollider)
        {
            if (!thrown || IsHeld || hitCollider == null) return false;
            if (CurrentSpeed() < MinThrowHitSpeed) return false;

            NetFAnnequinController target = hitCollider.GetComponentInParent<NetFAnnequinController>();
            if (target == null) return false;
            if (target.IsDead || target.IsKnockedDown || target.IsGrabbed || target.IsHoldingComputer)
                return false;
            if (target.netId == throwerNetId && Time.time < throwerImmunityUntil)
                return false;

            if (!ServerTryClaim(target)) return false;
            target.ServerForceReceiveComputer(this);
            return true;
        }

        [Server]
        bool ServerTryReturnThrownComputer()
        {
            if (!BrawlGameManager.PassTheBuckDumpActive) return false;

            uint previousThrower = throwerNetId;
            NetFAnnequinController thrower = FindPlayerByNetId(previousThrower);
            if (ServerGiveToCatcher(thrower, false))
                return true;

            NetFAnnequinController nearest = null;
            float bestDist = float.MaxValue;
            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
            {
                if (player == null || player.IsDead || player.IsKnockedDown || player.IsGrabbed)
                    continue;
                if (player.IsHoldingComputer) continue;
                float dist = (player.transform.position - transform.position).sqrMagnitude;
                if (dist >= bestDist) continue;
                bestDist = dist;
                nearest = player;
            }

            return ServerGiveToCatcher(nearest, true);
        }

        [Server]
        bool ServerGiveToCatcher(NetFAnnequinController target, bool applyCatchStun)
        {
            if (target == null || target.IsDead || target.IsKnockedDown || target.IsGrabbed) return false;
            if (!ServerTryClaim(target)) return false;
            target.ServerForceReceiveComputer(this, applyCatchStun);
            return true;
        }

        void ClearThrownState()
        {
            thrown = false;
            throwerNetId = 0u;
            thrownUntil = 0f;
            throwerImmunityUntil = 0f;
            throwerCollisionRestored = true;
        }

        float CurrentSpeed()
        {
            ResolveBody();
            return Body != null ? Body.velocity.magnitude : 0f;
        }

        NetFAnnequinController FindHolder()
        {
            if (serverHolder != null) return serverHolder;
            return FindPlayerByNetId(holderNetId);
        }

        static NetFAnnequinController FindPlayerByNetId(uint netId)
        {
            if (netId == 0u) return null;
            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
            {
                if (player != null && player.netId == netId)
                    return player;
            }

            return null;
        }

        void ResolveBody()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
        }

        void ResolvePickupRenderers()
        {
            if (PickupRenderers == null || PickupRenderers.Length == 0)
                PickupRenderers = GetComponentsInChildren<Renderer>(true);
        }

        void ApplyPickupPulse(bool enabled)
        {
            ResolvePickupRenderers();
            if (PickupRenderers == null || PickupRenderers.Length == 0) return;

            if (pickupPropertyBlock == null)
                pickupPropertyBlock = new MaterialPropertyBlock();

            float value = enabled ? 1f : 0f;
            foreach (Renderer targetRenderer in PickupRenderers)
            {
                if (targetRenderer == null) continue;
                targetRenderer.GetPropertyBlock(pickupPropertyBlock);
                pickupPropertyBlock.SetFloat(PickupPulseEnabledId, value);
                targetRenderer.SetPropertyBlock(pickupPropertyBlock);
                pickupPropertyBlock.Clear();
            }
        }

        void Reset()
        {
            ResolveBody();
            ResolvePickupRenderers();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            ResolveBody();
            ResolvePickupRenderers();
        }
    }
}
