using System.Collections;
using System.Collections.Generic;
using FIMSpace.FProceduralAnimation;
using FIMSpace.RagdollAnimatorDemo;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// MiniGame_01 联机玩家:控制 Punch Demo 的 FAnnequinV2。
    /// 移动走 FBasic_RigidbodyMover(服务端权威),出拳走 Demo_Ragd_Hero1。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class NetFAnnequinController : NetworkBehaviour, IBrawlPlayer
    {
        public FBasic_RigidbodyMover Mover;
        public Demo_Ragd_Hero1 Hero;
        public Animator Mecanim;
        public RagdollAnimator2 Ragdoll;

        [Header("Attack Timing")]
        [Min(0.1f)] public float PunchAnimationLockSeconds = 1.25f;
        [Min(0.1f)] public float PunchMovementLockSeconds = 1.23f;
        [Min(0.1f)] public float UppercutAnimationLockSeconds = 0.8f;

        [Header("Player Punch Hit Detection")]
        [Min(0.5f)] public float PunchHitRange = 2f;
        [Range(10f, 120f)] public float PunchHitAngle = 55f;
        [Min(0f)] public float PunchPointBlankRange = 0.55f;

        [Header("Hit Voice")]
        public AudioClip HitVoiceClip;
        [Range(0f, 1f)] public float HitVoiceVolume = 0.9f;
        public Vector2 HitVoicePitchRange = new Vector2(0.96f, 1.04f);

        [Header("KPI Computer Pickup")]
        [Min(0.5f)] public float ComputerPickupRange = 2.3f;
        [Range(10f, 180f)] public float ComputerPickupAngle = 120f;
        [Min(0.5f)] public float ComputerPickupPointBlank = 1.75f;
        [Min(0.1f)] public float ComputerPickupAnimationSeconds = 0.45f;
        public Vector3 ComputerHoldOffset = new Vector3(0f, 0.06f, 0.08f);
        public Vector3 ComputerHoldEuler = new Vector3(8f, 0f, 0f);
        [Min(0f)] public float ComputerDropForward = 0.8f;

        [Header("Turbo")]
        [Tooltip("按住 Shift 加速跑可连续使用的秒数")]
        [Min(0.1f)] public float TurboDurationSeconds = 5f;
        [Tooltip("松开 Shift 后从空恢复到满所需的秒数")]
        [Min(0.1f)] public float TurboRechargeSeconds = 5f;

        [Header("Turbo Exhausted Audio")]
        [Tooltip("Shift Turbo 耗尽时播放；为空时从 Resources/Audio/TurboTired 加载")]
        public AudioClip TurboExhaustedClip;
        [Range(0f, 1f)] public float TurboExhaustedVolume = 0.85f;
        public Vector2 TurboExhaustedPitchRange = new Vector2(0.98f, 1.02f);

        [Header("Knockdown / Get Up")]
        [Min(0.1f)] public float KnockdownGroundSeconds = 1.55f;
        [Min(0.1f)] public float GetUpFaceSeconds = 1.35f;
        [Min(0.1f)] public float GetUpBackSeconds = 1.65f;
        [Min(0f)] public float KnockdownSlideSpeed = 5.3f;
        [Min(0f)] public float KnockdownLiftSpeed = 3f;
        [Min(0.05f)] public float KnockbackControlSeconds = 1f;
        [Min(0f)] public float KnockbackDeceleration = 5.3f;
        [Min(0f)] public float KnockbackSpinSpeed = 1.25f;
        [Min(0f)] public float KnockbackSpinDeceleration = 1.5f;
        [Range(0.1f, 1f)] public float RagdollHorizontalForceScale = 0.31f;
        [Range(45f, 90f)] public float VisualFallAngle = 82f;
        [Range(0f, 360f)] public float VisualTumbleDegrees = 180f;
        [Min(0f)] public float VisualFallDelay = 0.1f;
        [Min(0.05f)] public float VisualFallSeconds = 0.5f;
        [Min(0f)] public float VisualFallLift = 0.02f;
        [Min(0.05f)] public float GetUpAlignmentBlendSeconds = 0.5f;

        [SyncVar] int score;
        public int Score { get => score; set => score = value; }

        [SyncVar(hook = nameof(OnSyncMoving))] bool syncMoving;
        [SyncVar(hook = nameof(OnSyncGrounded))] bool syncGrounded = true;
        [SyncVar(hook = nameof(OnSyncSpeed))] float syncSpeed;
        [SyncVar(hook = nameof(OnSyncHoldingComputer))] bool syncHoldingComputer;
        [SyncVar] float syncTurboRemaining = 5f;
        [SyncVar] public bool LobbyReady;

        public bool InputActive { get; set; } = true;
        public Vector3 SpawnPosition { get; set; }

        public uint NetId => netId;
        public Transform Transform => this != null ? transform : null;
        public PlayerAttributes Attributes => attributes;
        public bool IsDead => false;
        public bool WantsToMove => pendingMove.sqrMagnitude > 0.0001f;
        public bool IsMoveAuthority => Mover != null && Mover.Rigb != null && !Mover.Rigb.isKinematic && (isServer || isLocalPlayer);
        bool UsesServerPoseSync => poseSync != null && poseSync.enabled;
        public bool IsInKnockback => isServer && Time.time < knockbackUntil;
        public bool IsHoldingPlayer => heldPlayer != null;
        public bool IsHoldingComputer => syncHoldingComputer || heldComputer != null;
        public bool IsGrabbed => grabber != null;
        public bool IsKnockedDown { get; private set; }
        public Vector3 KnockbackVelocity { get; private set; }
        public float KnockbackSpinVelocity { get; private set; }
        public float TurboRemainingSeconds => Mathf.Clamp(syncTurboRemaining, 0f, Mathf.Max(0.1f, TurboDurationSeconds));
        public float TurboNormalized => Mathf.Clamp01(TurboRemainingSeconds / Mathf.Max(0.1f, TurboDurationSeconds));

        Vector3 pendingMove;
        float lastSendTime;
        Vector3 lastSentDir;
        byte lastSentButtons;
        byte serverButtons;
        float baseMoveSpeed = 4.5f;
        FAnnequinMouseActions mouseActions;
        FAnnequinGrabHelper grabHelper;
        PlayerAttributes attributes;
        AudioSource hitVoiceSource;
        AudioSource turboExhaustedSource;
        PunchAnimationEventRelay punchVoiceRelay;
        FAnnequinMeleeVictim meleeVictim;
        CapsuleCollider gameplayCapsule;
        NetworkTransformReliable netTransform;
        BrawlMoveSync moveSync;
        RagdollNetworkSync poseSync;
        double lastMovePoseSend;
        Coroutine throwFallback;
        Coroutine punchFallback;
        Coroutine computerPickupRoutine;
        Coroutine knockdownRoutine;
        Coroutine visualFallRoutine;
        float knockbackUntil;
        RigidbodyConstraints standingConstraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        float lastPlayerPunchTime = -1f;
        float attackLockedUntil = -1f;
        float attackMovementLockedUntil = -1f;
        float localAttackInputLockedUntil = -1f;
        float localAttackMovementLockedUntil = -1f;
        float lastReceivedHitTime = -1f;
        bool serverJumpAvailable = true;
        bool serverLeftGroundSinceJump;
        bool serverCatching;
        KpiComputerObjective heldComputer;
        NetFAnnequinController heldPlayer;
        NetFAnnequinController grabber;
        Collider grabIgnoreA;
        Collider grabIgnoreB;
        float holdBodyOffset = 1.35f;
        bool preferFaceGetUp;
        bool visualFallForward;
        bool physicalRagdollActive;
        Transform visualRoot;
        Vector3 visualStandingLocalPosition;
        Quaternion visualStandingLocalRotation;

        const float ThrowEventDelay = 0.28f;
        const float PunchHitDelay = 0.33f;
        const float UppercutHitDelay = 0.3f;
        const float HeartbeatInterval = 0.1f;
        const float JumpLandingNormalMinY = 0.55f;
        const float JumpLandingMaxUpwardSpeed = 0.5f;
        const byte BtnSprint = 1;
        const byte BtnCtrl = 2;

        void Awake()
        {
            if (Mover == null) Mover = GetComponent<FBasic_RigidbodyMover>();
            if (Hero == null) Hero = GetComponent<Demo_Ragd_Hero1>();
            if (Mecanim == null) Mecanim = GetComponent<Animator>();
            if (Ragdoll == null) Ragdoll = Hero != null && Hero.Ragdoll != null
                ? Hero.Ragdoll
                : GetComponent<RagdollAnimator2>();
            if (HitVoiceClip == null) HitVoiceClip = Resources.Load<AudioClip>("Audio/HitVoice_Ah");
            gameplayCapsule = GetComponent<CapsuleCollider>();
            hitVoiceSource = Hero != null && Hero.HitAudio != null ? Hero.HitAudio : GetComponent<AudioSource>();
            if (hitVoiceSource == null)
            {
                hitVoiceSource = gameObject.AddComponent<AudioSource>();
                hitVoiceSource.playOnAwake = false;
                hitVoiceSource.loop = false;
                hitVoiceSource.spatialBlend = 1f;
                hitVoiceSource.dopplerLevel = 0f;
                hitVoiceSource.minDistance = 1f;
                hitVoiceSource.maxDistance = 14f;
                hitVoiceSource.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            if (Mecanim != null && Mecanim.transform != transform)
            {
                visualRoot = Mecanim.transform;
                visualStandingLocalPosition = visualRoot.localPosition;
                visualStandingLocalRotation = visualRoot.localRotation;
            }
            punchVoiceRelay = GetComponentInChildren<PunchAnimationEventRelay>(true);
            if (Mover) baseMoveSpeed = Mover.MovementSpeed;

            attributes = GetComponent<PlayerAttributes>();
            if (attributes == null) attributes = gameObject.AddComponent<PlayerAttributes>();

            mouseActions = GetComponent<FAnnequinMouseActions>();
            if (mouseActions == null) mouseActions = gameObject.AddComponent<FAnnequinMouseActions>();
            mouseActions.OnShortPressAttack = RequestPunch;
            mouseActions.OnLongPressGrab = RequestComputerPickup;
            mouseActions.OnRightClickRelease = CmdReleaseHeldObject;
            mouseActions.enabled = false;

            if (GetComponent<FAnnequinLocomotionFix>() == null)
                gameObject.AddComponent<FAnnequinLocomotionFix>();
            grabHelper = GetComponent<FAnnequinGrabHelper>();
            if (grabHelper == null) grabHelper = gameObject.AddComponent<FAnnequinGrabHelper>();

            meleeVictim = GetComponent<FAnnequinMeleeVictim>();
            if (meleeVictim == null) meleeVictim = gameObject.AddComponent<FAnnequinMeleeVictim>();
            // Demo 自带的玩家拳击盒最远会搜索到 2.8 米，不能让它直接触发联机玩家倒地。
            // 联机玩家命中统一走 ServerPunchPlayers 的短距离服务器判定。
            meleeVictim.OnHitByMelee = null;
            netTransform = GetComponent<NetworkTransformReliable>();
            poseSync = GetComponent<RagdollNetworkSync>();
            moveSync = GetComponent<BrawlMoveSync>();
            if (moveSync == null)
                moveSync = gameObject.AddComponent<BrawlMoveSync>();
        }

        public override void OnStartServer()
        {
            BrawlSession.AdoptActor(gameObject);
            SpawnPosition = transform.position;
            ResetServerJumpState();
            ServerResetTurbo();
            if (Mover != null && Mover.Rigb != null)
                standingConstraints = Mover.Rigb.constraints;

            var capsule = gameplayCapsule != null ? gameplayCapsule : GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.enabled = true;
                capsule.isTrigger = false;
                if (capsule.radius < 0.38f) capsule.radius = 0.38f;
            }

            if (Hero != null)
                Hero.OnMeleeHit = ServerOnHeroMeleeHit;
            if (meleeVictim != null)
                meleeVictim.OnHitByMelee = null;
            ConfigureNetworkSync();
            DisableLocalDemoInput();
            ServerIgnoreComputerCollisions();
        }

        public override void OnStartClient()
        {
            BrawlSession.AdoptActor(gameObject);
            DisableLocalDemoInput();
            ConfigureNetworkSync();

            if (!isServer)
            {
                if (Mover)
                {
                    Mover.enabled = false;
                    if (Mover.Rigb)
                    {
                        Mover.Rigb.isKinematic = true;
                        // 刚体插值会和 NetworkTransform 写 transform 抢位置,远程角色就会抖
                        Mover.Rigb.interpolation = RigidbodyInterpolation.None;
                    }
                }

                if (Mecanim)
                {
                    Mecanim.SetBool("Moving", syncMoving);
                    Mecanim.SetBool("Grounded", syncGrounded);
                    Mecanim.SetFloat("Speed", syncSpeed);
                }
            }
        }

        void ConfigureNetworkSync()
        {
            var netAnim = GetComponent<NetworkAnimator>();
            if (netAnim != null)
                netAnim.enabled = false;

            if (poseSync == null)
                poseSync = GetComponent<RagdollNetworkSync>();
            if (poseSync != null)
                poseSync.enabled = true;

            if (netTransform == null)
                netTransform = GetComponent<NetworkTransformReliable>();
            if (netTransform != null)
                netTransform.enabled = false;
        }

        void OnSyncMoving(bool _, bool value)
        {
            if (!isServer && Mecanim) Mecanim.SetBool("Moving", value);
        }

        void OnSyncGrounded(bool _, bool value)
        {
            if (!isServer && Mecanim) Mecanim.SetBool("Grounded", value);
        }

        void OnSyncSpeed(float _, float value)
        {
            if (!isServer && Mecanim) Mecanim.SetFloat("Speed", value);
        }

        void OnSyncHoldingComputer(bool _, bool value)
        {
            ApplyComputerHoldingPose(value);
        }

        void ApplyComputerHoldingPose(bool holding)
        {
            if (Hero != null)
            {
                Hero.SetHoldingPose(holding);
                return;
            }

            if (Mecanim == null || Mecanim.layerCount < 2) return;
            if (holding) Mecanim.CrossFadeInFixedTime("Holding", 0.1f, 1);
            else Mecanim.SetLayerWeight(1, 0f);
        }

        void ServerSyncLocomotionParams()
        {
            if (!isServer || Mecanim == null) return;
            bool moving = Mecanim.GetBool("Moving");
            bool grounded = Mecanim.GetBool("Grounded");
            float speed = Mecanim.GetFloat("Speed");
            if (syncMoving != moving) syncMoving = moving;
            if (syncGrounded != grounded) syncGrounded = grounded;
            if (Mathf.Abs(syncSpeed - speed) > 0.05f) syncSpeed = speed;
        }

        public override void OnStartLocalPlayer()
        {
            if (mouseActions) mouseActions.enabled = true;
            if (!isServer && !UsesServerPoseSync && Mover != null)
            {
                Mover.enabled = true;
                if (Mover.Rigb != null)
                {
                    Mover.Rigb.isKinematic = false;
                    Mover.Rigb.interpolation = RigidbodyInterpolation.Interpolate;
                }
            }
        }

        public override void OnStopLocalPlayer()
        {
            if (mouseActions)
            {
                mouseActions.CancelHold();
                mouseActions.enabled = false;
            }
        }

        void DisableLocalDemoInput()
        {
            if (Mover)
            {
                Mover.UpdateInput = false;
                Mover.DisableRootMotion = true;
            }
            if (Hero) Hero.ProcessInput = false;
            if (Mecanim) Mecanim.applyRootMotion = false;
        }

        void Update()
        {
            bool attackMovementLocked = IsAttackMovementLocked();

            if (isServer)
                ServerTickTurbo(Time.deltaTime, attackMovementLocked);

            if (isServer && !serverJumpAvailable && Mover != null && !Mover.isGrounded)
                serverLeftGroundSinceJump = true;

            if ((isServer || (isLocalPlayer && !UsesServerPoseSync)) && Mover && Mover.enabled)
            {
                Vector3 dir = InputActive && !attackMovementLocked ? pendingMove : Vector3.zero;
                Mover.moveDirectionWorld = dir;
                if (dir.sqrMagnitude > 0.0001f)
                    Mover.SetTargetRotation(dir);
            }

            if (isLocalPlayer && !isServer && !UsesServerPoseSync)
                ClientReconcile();

            if (isServer)
                ServerSyncLocomotionParams();

            if (!isLocalPlayer) return;

            Vector2 inputValue = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) inputValue.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) inputValue.y -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) inputValue.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) inputValue.x += 1f;
            if (inputValue.sqrMagnitude > 1f) inputValue.Normalize();

            Vector3 worldDir = Vector3.zero;
            if (!attackMovementLocked && inputValue != Vector2.zero)
            {
                float camYaw = Camera.main ? Camera.main.transform.eulerAngles.y : 0f;
                worldDir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(inputValue.x, 0f, inputValue.y);
            }

            byte buttons = 0;
            if (Input.GetKey(KeyCode.LeftShift)) buttons |= BtnSprint;
            if (Input.GetKey(KeyCode.LeftControl)) buttons |= BtnCtrl;

            if (!InputActive)
            {
                worldDir = Vector3.zero;
                buttons = 0;
            }

            if (!isServer)
                pendingMove = worldDir;

            bool changed = (worldDir - lastSentDir).sqrMagnitude > 0.0001f || buttons != lastSentButtons;
            if (changed || Time.time - lastSendTime > HeartbeatInterval)
            {
                CmdSetInput(worldDir, buttons);
                lastSentDir = worldDir;
                lastSentButtons = buttons;
                lastSendTime = Time.time;
            }

            if (InputActive && !attackMovementLocked && Input.GetKeyDown(KeyCode.Space)) CmdJump();
            if (InputActive && (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.F)))
                RequestPunch();
        }

        [Command]
        void CmdSetInput(Vector3 moveWorldDir, byte buttons)
        {
            try
            {
            serverButtons = buttons;
            if (!InputActive || Time.time < attackMovementLockedUntil)
            {
                pendingMove = Vector3.zero;
                ApplyServerMovementSpeed();
                return;
            }

            moveWorldDir.y = 0f;
            pendingMove = moveWorldDir.sqrMagnitude > 0.0001f ? Vector3.ClampMagnitude(moveWorldDir, 1f) : Vector3.zero;

            ApplyServerMovementSpeed();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdSetInput: {e.Message}");
            }
        }

        [Command]
        void CmdJump()
        {
            try
            {
                if (!InputActive || Hero == null || Mover == null || Time.time < attackMovementLockedUntil) return;
                if (!serverJumpAvailable || !Mover.isGrounded) return;

                // 服务端在真正施加起跳前立即消耗本次跳跃，避免同一物理帧内的重复 Command
                // 或短暂的 grounded 抖动再次触发向上速度。
                serverJumpAvailable = false;
                serverLeftGroundSinceJump = false;
                Hero.DoJump();

                // 引用异常或插件拒绝起跳时不应永久锁住玩家。
                if (Mover.jumpRequest == 0f)
                    ResetServerJumpState();
            }
            catch (System.Exception e)
            {
                ResetServerJumpState();
                Debug.LogWarning($"CmdJump: {e.Message}");
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            ServerTryRestoreJump(collision);
        }

        void OnCollisionStay(Collision collision)
        {
            ServerTryRestoreJump(collision);
        }

        void ServerTryRestoreJump(Collision collision)
        {
            if (!isServer || serverJumpAvailable || !serverLeftGroundSinceJump) return;
            if (collision == null || collision.collider == null || collision.collider.isTrigger) return;
            if (collision.transform == transform || collision.transform.IsChildOf(transform)) return;
            if (Mover == null || Mover.Rigb == null || Mover.Rigb.velocity.y > JumpLandingMaxUpwardSpeed) return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y < JumpLandingNormalMinY) continue;
                ResetServerJumpState();
                return;
            }
        }

        [Server]
        void ResetServerJumpState()
        {
            serverJumpAvailable = true;
            serverLeftGroundSinceJump = false;
        }

        Vector3 GetLookDir()
        {
            return Camera.main ? Camera.main.transform.forward : transform.forward;
        }

        void RequestPunch()
        {
            if (!InputActive || Time.time < localAttackInputLockedUntil) return;
            if (IsHoldingComputer || (mouseActions != null && mouseActions.IsPickupButtonHeld)) return;

            bool willThrow = Hero != null && (Hero.IsHoldingUp || Hero.IsThrowing);
            if (!willThrow)
            {
                // 本地先播，避免等待 Command -> ClientRpc 往返后才听到挥拳声。
                PlayPunchVoiceAtAttackStart();
                BeginLocalAttackLocks(PunchAnimationLockSeconds, PunchMovementLockSeconds);
            }

            CmdPunchOrThrow(0, GetLookDir());
        }

        void RequestComputerPickup()
        {
            if (!InputActive || IsHoldingComputer) return;
            CmdCatch(GetLookDir());
        }

        bool IsAttackMovementLocked()
        {
            return (isServer && Time.time < attackMovementLockedUntil)
                || (isLocalPlayer && Time.time < localAttackMovementLockedUntil);
        }

        void BeginLocalAttackLocks(float attackDuration, float movementDuration)
        {
            localAttackInputLockedUntil = Mathf.Max(localAttackInputLockedUntil, Time.time + attackDuration);
            localAttackMovementLockedUntil = Mathf.Max(localAttackMovementLockedUntil, Time.time + movementDuration);
            StopMovementForAttack();
        }

        void StopMovementForAttack()
        {
            pendingMove = Vector3.zero;

            if (Mover != null)
            {
                Mover.moveDirectionWorld = Vector3.zero;
                Mover.ResetTargetRotation();

                if (Mover.Rigb != null && !Mover.Rigb.isKinematic)
                {
                    Vector3 velocity = Mover.Rigb.velocity;
                    Mover.Rigb.velocity = new Vector3(0f, velocity.y, 0f);
                }
            }

            if (Mecanim != null)
            {
                Mecanim.SetBool("Moving", false);
                Mecanim.SetFloat("Speed", 0f);
            }
        }

        public void ServerBotSetMove(Vector3 worldDir, bool sprint = false)
        {
            if (!isServer) return;
            if (sprint) serverButtons |= BtnSprint;
            else serverButtons &= unchecked((byte)~BtnSprint);
            if (!InputActive || Time.time < attackMovementLockedUntil)
            {
                pendingMove = Vector3.zero;
                ApplyServerMovementSpeed();
                return;
            }

            worldDir.y = 0f;
            pendingMove = worldDir.sqrMagnitude > 0.0001f ? Vector3.ClampMagnitude(worldDir, 1f) : Vector3.zero;
            ApplyServerMovementSpeed();
        }

        [Server]
        public void ServerResetTurbo()
        {
            syncTurboRemaining = Mathf.Max(0.1f, TurboDurationSeconds);
            serverButtons = 0;
            ApplyServerMovementSpeed();
        }

        [Server]
        void ServerTickTurbo(float deltaTime, bool attackMovementLocked)
        {
            float capacity = Mathf.Max(0.1f, TurboDurationSeconds);
            float rechargeSeconds = Mathf.Max(0.1f, TurboRechargeSeconds);
            bool sprintHeld = (serverButtons & BtnSprint) != 0;
            bool canRun = InputActive && !IsDead && !IsKnockedDown && !IsGrabbed
                && !attackMovementLocked && pendingMove.sqrMagnitude > 0.0001f;

            float next = syncTurboRemaining;
            if (sprintHeld && canRun && next > 0f)
                next = Mathf.Max(0f, next - Mathf.Max(0f, deltaTime));
            else if (!sprintHeld || !InputActive || IsDead || IsKnockedDown || IsGrabbed)
                next = Mathf.Min(capacity, next + Mathf.Max(0f, deltaTime) * capacity / rechargeSeconds);

            bool exhaustedNow = sprintHeld && canRun
                && syncTurboRemaining > 0.0001f && next <= 0.0001f;
            if (!Mathf.Approximately(syncTurboRemaining, next))
                syncTurboRemaining = next;
            if (exhaustedNow && connectionToClient != null)
                TargetPlayTurboExhausted();

            ApplyServerMovementSpeed();
        }

        [TargetRpc]
        void TargetPlayTurboExhausted()
        {
            if (!isLocalPlayer) return;
            PlayTurboExhaustedAudio();
        }

        void PlayTurboExhaustedAudio()
        {
            if (TurboExhaustedClip == null)
                TurboExhaustedClip = Resources.Load<AudioClip>("Audio/TurboTired");
            if (TurboExhaustedClip == null) return;

            if (turboExhaustedSource == null)
            {
                turboExhaustedSource = gameObject.AddComponent<AudioSource>();
                turboExhaustedSource.playOnAwake = false;
                turboExhaustedSource.loop = false;
                // 体力耗尽属于本地 UI 反馈，只让操作者自己以 2D 声音听到。
                turboExhaustedSource.spatialBlend = 0f;
                turboExhaustedSource.dopplerLevel = 0f;
            }

            turboExhaustedSource.Stop();
            turboExhaustedSource.clip = TurboExhaustedClip;
            turboExhaustedSource.volume = TurboExhaustedVolume;
            float minPitch = Mathf.Min(TurboExhaustedPitchRange.x, TurboExhaustedPitchRange.y);
            float maxPitch = Mathf.Max(TurboExhaustedPitchRange.x, TurboExhaustedPitchRange.y);
            turboExhaustedSource.pitch = Random.Range(minPitch, maxPitch);
            turboExhaustedSource.Play();
        }

        [Server]
        void ApplyServerMovementSpeed()
        {
            if (Mover == null) return;

            bool turboActive = InputActive
                && Time.time >= attackMovementLockedUntil
                && pendingMove.sqrMagnitude > 0.0001f
                && (serverButtons & BtnSprint) != 0
                && syncTurboRemaining > 0.0001f
                && Mover.HoldShiftForSpeed > 0f;

            if (turboActive)
                Mover.MovementSpeed = Mover.HoldShiftForSpeed;
            else if ((serverButtons & BtnCtrl) != 0 && Mover.HoldCtrlForSpeed > 0f)
                Mover.MovementSpeed = Mover.HoldCtrlForSpeed;
            else
                Mover.MovementSpeed = baseMoveSpeed;
        }

        public void ServerBotFace(Vector3 lookDir)
        {
            if (!isServer) return;
            ServerFaceYaw(lookDir);
        }

        public void ServerBotPunch()
        {
            if (!isServer) return;
            ServerExecutePunchOrThrow(0, transform.forward);
        }

        public bool ServerBotTryPickup()
        {
            if (!isServer || !InputActive) return false;
            if (!ServerTryPickupComputer()) return false;
            ServerFinishComputerPickupImmediate();
            return true;
        }

        [Server]
        void ServerFinishComputerPickupImmediate()
        {
            if (heldComputer == null) return;
            if (computerPickupRoutine != null)
            {
                StopCoroutine(computerPickupRoutine);
                computerPickupRoutine = null;
            }

            syncHoldingComputer = true;
            FinishComputerPickupAnimation();
            ApplyComputerHoldingPose(true);
            ServerHoldComputerAtHands();
            RpcFinishComputerPickup();
        }

        void ServerExecutePunchOrThrow(byte kind, Vector3 lookDir)
        {
            if (!InputActive || Hero == null) return;
            if (IsHoldingComputer) return;
            if (Hero.IsHoldingUp || Hero.IsThrowing || IsHoldingPlayer)
            {
                ServerStartThrow(lookDir);
                return;
            }

            if (Time.time < attackLockedUntil) return;
            float attackLockSeconds = kind == 0 ? PunchAnimationLockSeconds : UppercutAnimationLockSeconds;
            float movementLockSeconds = kind == 0 ? PunchMovementLockSeconds : UppercutAnimationLockSeconds;
            attackLockedUntil = Time.time + attackLockSeconds;
            attackMovementLockedUntil = Time.time + movementLockSeconds;
            StopMovementForAttack();

            ServerFaceYaw(lookDir);
            if (kind == 0)
            {
                Hero.DoPunchF();
                PlayPunchVoiceAtAttackStart();
            }
            else Hero.DoPunchU();
            RpcPlayClip(kind == 0 ? "Punch F" : "Punch U", 0);

            // 玩家击飞必须等到动画命中帧（Punch F 为 0.333 秒）。
            // 真正判定由动画事件触发；下面的协程只在动画事件漏发时兜底。

            if (punchFallback != null) StopCoroutine(punchFallback);
            punchFallback = StartCoroutine(PunchHitFallback(kind));
        }

        [Command]
        void CmdPunchOrThrow(byte kind, Vector3 lookDir)
        {
            try
            {
                ServerExecutePunchOrThrow(kind, lookDir);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdPunchOrThrow: {e.Message}");
            }
        }

        [Command]
        void CmdCatch(Vector3 lookDir)
        {
            try
            {
                if (!InputActive || Hero == null) return;
                if (IsHoldingComputer || Hero.IsHoldingUp || Hero.IsThrowing || IsHoldingPlayer) return;

                // 长按右键会持续重试。没有可拾取物时不能改角色朝向，
                // 否则会和跑步转向每 0.1 秒互相抢控制，表现为人物抖动。
                ServerTryPickupComputer(lookDir);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdCatch: {e.Message}");
            }
        }

        [Command]
        public void CmdRequestNextRound()
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.ServerOnNextRoundRequested();
        }

        [Command]
        public void CmdSetLobbyReady(bool ready)
        {
            BrawlGameManager gm = BrawlGameManager.Instance;
            if (gm == null || !gm.ServerCanAcceptLobbyReady())
                return;
            LobbyReady = ready;
            if (ready)
                gm.ServerTryStartFromLobby();
        }

        [Command]
        public void CmdRequestLobbyStart()
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.ServerTryStartFromLobby();
        }

        [Command]
        public void CmdDebugSetRemainingSeconds(float seconds)
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.ServerDebugSetRemainingSeconds(seconds);
        }

        [Command]
        void CmdReleaseHeldObject()
        {
            try
            {
                if (!InputActive || Hero == null) return;
                if (heldComputer != null)
                    ServerReleaseComputer();
                else if (IsHoldingPlayer)
                    ServerReleaseHeldPlayer(Vector3.zero, false);
                // 什么都没拿时保持当前移动/动画，不调用 Demo 的释放动作。
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdReleaseHeldObject: {e.Message}");
            }
        }

        [Command]
        void CmdSetCatching(bool catching, Vector3 lookDir)
        {
            try
            {
                serverCatching = catching && InputActive && !IsDead && !IsGrabbed && !IsKnockedDown;
                if (!serverCatching)
                {
                    if (heldComputer != null)
                        ServerReleaseComputer();
                    else if (IsHoldingPlayer)
                        ServerReleaseHeldPlayer(Vector3.zero, false);
                    else if (Hero != null)
                        Hero.DoRelease();
                    return;
                }

                if (Hero == null || IsHoldingComputer || Hero.IsHoldingUp || Hero.IsThrowing || IsHoldingPlayer)
                    return;

                ServerTryPickupComputer(lookDir);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdSetCatching: {e.Message}");
            }
        }

        [Server]
        void ServerFaceYaw(Vector3 lookDir)
        {
            Vector3 yaw = lookDir;
            yaw.y = 0f;
            if (yaw.sqrMagnitude < 0.0001f) return;
            yaw.Normalize();
            transform.rotation = Quaternion.LookRotation(yaw);
            if (Mover) Mover.SetTargetRotation(yaw);
        }

        [Server]
        void ServerStartThrow(Vector3 lookDir)
        {
            if (Hero == null || Hero.IsThrowing) return;
            if (!Hero.IsHoldingUp && !IsHoldingPlayer) return;

            ServerFaceYaw(lookDir);

            Vector3 throwDir = lookDir.sqrMagnitude > 0.0001f ? lookDir.normalized : transform.forward;
            if (throwDir.y < 0.08f) throwDir.y = 0.08f;
            throwDir.Normalize();
            Hero.PendingThrowDirection = throwDir;

            if (Hero.IsHoldingUp)
                Hero.DoThrow();
            else
                Hero.PlayThrowAnimation();

            RpcPlayClip("Holding Throw", 0);

            if (throwFallback != null) StopCoroutine(throwFallback);
            throwFallback = StartCoroutine(ThrowFallback(throwDir));
        }

        IEnumerator ThrowFallback(Vector3 throwDir)
        {
            yield return new WaitForSeconds(ThrowEventDelay);
            throwFallback = null;
            if (Hero != null && Hero.IsHoldingUp)
                Hero.EThrow(throwDir);
            if (IsHoldingPlayer)
                ServerReleaseHeldPlayer(throwDir * 10f, true);
            if (Hero != null) Hero.ClearThrowing();
        }

        IEnumerator PunchHitFallback(byte kind)
        {
            yield return new WaitForSeconds(kind == 0 ? PunchHitDelay : UppercutHitDelay);
            punchFallback = null;
            if (Hero == null) yield break;

            try
            {
                if (kind == 0) Hero.EPunchForward();
                else Hero.EPunchUp();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"PunchHitFallback: {e.Message}");
            }

            ServerPunchPlayers(kind);
        }

        [Server]
        void ServerOnHeroMeleeHit(Vector3 impactDirection)
        {
            ServerPunchPlayers(impactDirection.sqrMagnitude > 0.01f ? impactDirection.normalized : transform.forward, 9f);
        }

        [Server]
        void ServerPunchPlayers(byte kind)
        {
            Vector3 dir = kind == 0
                ? (transform.forward + Vector3.up * 0.45f).normalized
                : (Vector3.up + transform.forward * 0.2f).normalized;
            ServerPunchPlayers(dir, kind == 0 ? 8.5f : 10f);
        }

        [Server]
        void ServerPunchPlayers(Vector3 dir, float speed)
        {
            if (Time.time - lastPlayerPunchTime < 0.35f) return;
            Physics.SyncTransforms();

            var targets = FindNearbyPlayers(PunchHitRange, PunchHitAngle, PunchPointBlankRange);
            if (targets.Count == 0)
            {
                // 极近距离保底，处理角色胶囊相贴或短暂网络位置误差；不再跨身位吸附目标。
                NetFAnnequinController nearest = FindNearestPlayer(PunchPointBlankRange);
                if (nearest != null) targets.Add(nearest);
            }

            if (targets.Count == 0) return;
            lastPlayerPunchTime = Time.time;

            foreach (NetFAnnequinController other in targets)
            {
                other.ServerKnockDown(dir * speed, attributes != null ? attributes.AttackDamage : 10);
                if (Hero != null && Hero.HitAudio) Hero.HitAudio.Play();
            }
        }

        [Server]
        List<NetFAnnequinController> FindNearbyPlayers(float range, float maxAngle, float alwaysHitDistance)
        {
            var result = new List<NetFAnnequinController>();
            var seen = new HashSet<NetFAnnequinController>();
            Vector3 origin = transform.position;
            Vector3 face = transform.forward;
            face.y = 0f;
            if (face.sqrMagnitude < 0.0001f) face = Vector3.forward;
            face.Normalize();

            void TryAdd(NetFAnnequinController other)
            {
                if (other == null || other == this || other.IsGrabbed || other.IsDead) return;
                if (!seen.Add(other)) return;

                Vector3 to = other.transform.position - origin;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > range) return;
                if (dist > alwaysHitDistance && Vector3.Dot(face, to / Mathf.Max(dist, 0.001f)) < Mathf.Cos(maxAngle * Mathf.Deg2Rad))
                    return;

                result.Add(other);
            }

            foreach (NetFAnnequinController other in AllSpawnedPlayers())
                TryAdd(other);

            Collider[] cols = Physics.OverlapSphere(origin + Vector3.up, range, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                TryAdd(cols[i].GetComponentInParent<NetFAnnequinController>());
            }

            return result;
        }

        [Server]
        NetFAnnequinController FindNearestPlayer(float range)
        {
            NetFAnnequinController best = null;
            float bestDist = range;
            foreach (NetFAnnequinController other in AllSpawnedPlayers())
            {
                if (other == null || other == this || other.IsGrabbed || other.IsDead) continue;
                float dist = Vector3.Distance(transform.position, other.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = other;
                }
            }

            return best;
        }

        [Server]
        static List<NetFAnnequinController> AllSpawnedPlayers()
        {
            var list = new List<NetFAnnequinController>();
            var seen = new HashSet<NetFAnnequinController>();

            if (NetworkServer.active)
            {
                foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
                {
                    if (identity == null) continue;
                    var player = identity.GetComponent<NetFAnnequinController>();
                    if (player != null && seen.Add(player))
                        list.Add(player);
                }
            }

            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
            {
                if (player != null && seen.Add(player))
                    list.Add(player);
            }

            return list;
        }

        [Server]
        public void ServerApplyHit(Vector3 velocity, float duration)
        {
            KnockbackVelocity = velocity;
            knockbackUntil = Time.time + Mathf.Max(0.05f, duration);

            if (Mover != null && Mover.Rigb != null && !Mover.Rigb.isKinematic)
            {
                Vector3 current = Mover.Rigb.velocity;
                current.x = velocity.x;
                current.z = velocity.z;
                current.y = Mathf.Max(current.y, velocity.y);
                Mover.Rigb.velocity = current;
            }
        }

        [Server]
        public void TickKnockback(float deltaTime)
        {
            KnockbackVelocity = Vector3.MoveTowards(KnockbackVelocity, Vector3.zero, deltaTime * KnockbackDeceleration);
            KnockbackSpinVelocity = Mathf.MoveTowards(KnockbackSpinVelocity, 0f, deltaTime * KnockbackSpinDeceleration);
        }

        [Server]
        public void ServerKnockDown(Vector3 impulse, int damage = 10)
        {
            if (IsDead) return;
            if (IsGrabbed) return;
            if (Time.time - lastReceivedHitTime < 0.45f) return;
            lastReceivedHitTime = Time.time;
            if (isClient) PlayHitVoice();
            RpcPlayHitVoice();
            serverCatching = false;
            if (heldComputer != null)
            {
                Vector3 toss = Vector3.ProjectOnPlane(impulse, Vector3.up);
                if (toss.sqrMagnitude < 0.01f) toss = -transform.forward;
                ServerReleaseComputer(true, toss.normalized * 2.5f + Vector3.up * 2f);
            }
            if (heldPlayer != null)
                ServerReleaseHeldPlayer(Vector3.zero, false);

            IsKnockedDown = true;
            InputActive = false;
            pendingMove = Vector3.zero;

            Vector3 impact = impulse.sqrMagnitude > 0.01f ? impulse : -transform.forward;
            Vector3 horizontal = Vector3.ProjectOnPlane(impact, Vector3.up);
            if (horizontal.sqrMagnitude < 0.01f) horizontal = -transform.forward;
            // 只收短水平飞行距离，保留向上的冲量和 Bot 同款的四肢翻滚感。
            Vector3 impactDirection = horizontal.normalized * RagdollHorizontalForceScale
                + Vector3.up * 0.33f;
            physicalRagdollActive = TryStartPhysicalRagdoll(impactDirection);
            Vector3 vel = horizontal.normalized * KnockdownSlideSpeed + Vector3.up * KnockdownLiftSpeed;
            float lateralImpact = Vector3.Dot(horizontal.normalized, transform.right);
            float spinDirection = Mathf.Abs(lateralImpact) > 0.15f
                ? Mathf.Sign(lateralImpact)
                : ((netId & 1u) == 0u ? 1f : -1f);
            KnockbackSpinVelocity = spinDirection * KnockbackSpinSpeed;

            Vector3 standingForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Vector3 knockdownDirection = Vector3.ProjectOnPlane(vel, Vector3.up);
            visualFallForward = standingForward.sqrMagnitude > 0.01f && knockdownDirection.sqrMagnitude > 0.01f
                && Vector3.Dot(standingForward.normalized, knockdownDirection.normalized) > 0f;
            float tumbleRemainder = Mathf.Repeat(Mathf.Abs(VisualTumbleDegrees), 360f);
            bool tumbleFlipsLandingSide = tumbleRemainder >= 90f && tumbleRemainder < 270f;
            preferFaceGetUp = tumbleFlipsLandingSide ? !visualFallForward : visualFallForward;

            if (!physicalRagdollActive && Mover != null && Mover.Rigb != null)
            {
                Mover.enabled = false;
                if (Mover.Rigb.isKinematic) Mover.Rigb.isKinematic = false;
                // 只放开 Y 轴并施加短促转身；X/Z 仍锁定，避免整个人像轮胎一样连续翻滚。
                Mover.Rigb.constraints = standingConstraints;
                Mover.Rigb.angularVelocity = Vector3.up * KnockbackSpinVelocity;
                Mover.Rigb.velocity = vel;
            }

            KnockbackVelocity = physicalRagdollActive ? Vector3.zero : new Vector3(vel.x, 0f, vel.z);
            knockbackUntil = physicalRagdollActive ? 0f : Time.time + KnockbackControlSeconds;
            ServerIgnoreNearbyPlayerCollision(true);

            if (!physicalRagdollActive && Mecanim)
            {
                Mecanim.SetBool("Ragdolled", false);
                Mecanim.SetBool("Grounded", false);
                Mecanim.SetBool("Moving", false);
                Mecanim.CrossFadeInFixedTime("Fall", 0.08f, 0);
            }
            if (!physicalRagdollActive)
                BeginVisualFall(visualFallForward);
            RpcKnockDown(true, "Fall", visualFallForward, physicalRagdollActive);

            if (knockdownRoutine != null) StopCoroutine(knockdownRoutine);
            knockdownRoutine = StartCoroutine(KnockdownRoutine());
        }

        /// <summary>
        /// 使用 Punch Demo Bot 的 RA2 击飞方式：全身冲量负责惯性，核心骨冲量负责命中张力。
        /// 落点跟随和起身由 Bot 同款 RA2 Features 接管。
        /// </summary>
        [Server]
        bool TryStartPhysicalRagdoll(Vector3 impactDirection)
        {
            if (Ragdoll == null || !Ragdoll.enabled || !Ragdoll.Handler.WasInitialized)
                return false;

            Rigidbody core = Ragdoll.User_GetNearestRagdollRigidbodyToPosition(
                transform.TransformPoint(new Vector3(0f, 1.45f, 0.2f)),
                true,
                ERagdollChainType.Core);
            if (core == null) return false;

            if (visualFallRoutine != null)
            {
                StopCoroutine(visualFallRoutine);
                visualFallRoutine = null;
            }
            ResetVisualToStanding();

            if (Mover != null)
            {
                Mover.enabled = false;
                if (Mover.Rigb != null)
                {
                    Mover.Rigb.velocity = Vector3.zero;
                    Mover.Rigb.angularVelocity = Vector3.zero;
                    Mover.Rigb.constraints = standingConstraints;
                }
            }
            // Punch Demo Bot 在 Falling 期间会关闭角色控制胶囊，避免它与自己的
            // 物理假体互相顶开并造成起身前后的长距离滑步。
            if (gameplayCapsule != null)
                gameplayCapsule.enabled = false;

            float punchPower = Hero != null ? Hero.PunchPower : 20f;
            Ragdoll.User_SwitchFallState(RagdollHandler.EAnimatingMode.Falling);
            Ragdoll.User_AddAllBonesImpact(
                impactDirection * (punchPower * 0.5f), 0.05f, ForceMode.Impulse);
            Ragdoll.User_AddRigidbodyImpact(
                core, impactDirection * (punchPower * 1.5f), 0f, ForceMode.Impulse);
            return true;
        }

        [Server]
        void ServerIgnoreNearbyPlayerCollision(bool ignore)
        {
            Collider self = GetComponent<Collider>();
            if (self == null) return;
            foreach (NetFAnnequinController other in AllSpawnedPlayers())
            {
                if (other == null || other == this) continue;
                Collider otherCol = other.GetComponent<Collider>();
                if (otherCol == null) continue;
                Physics.IgnoreCollision(self, otherCol, ignore);
            }
        }

        IEnumerator KnockdownRoutine()
        {
            if (physicalRagdollActive)
            {
                // Auto Get Up 会等身体速度足够低且触地稳定后切换 Standing；
                // 这里只锁住玩法输入，避免固定计时强行对齐造成瞬移。
                float timeout = 6f;
                while (timeout > 0f && IsKnockedDown && !IsGrabbed && !IsDead
                    && Ragdoll != null
                    && Ragdoll.Handler.AnimatingMode != RagdollHandler.EAnimatingMode.Standing)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }

                if (!IsKnockedDown || IsGrabbed || IsDead) yield break;
                if (Ragdoll != null
                    && Ragdoll.Handler.AnimatingMode != RagdollHandler.EAnimatingMode.Standing)
                {
                    // 极端卡角落的兜底仍走 RA2 混合，不直接改骨骼或根节点。
                    Ragdoll.User_TransitionToStandingMode(1f, 0f);
                }

                StabilizePhysicalGetUp();

                yield return new WaitForSeconds(Mathf.Max(GetUpFaceSeconds, GetUpBackSeconds));
                if (!IsKnockedDown || IsGrabbed || IsDead) yield break;
                ServerForceStand();
                yield break;
            }

            yield return new WaitForSeconds(KnockdownGroundSeconds);
            if (!IsKnockedDown || IsGrabbed || IsDead) yield break;

            float lyingDirection = Vector3.Dot(transform.forward, Vector3.up);
            bool getUpFromFace = Mathf.Abs(lyingDirection) > 0.15f
                ? lyingDirection > 0f
                : preferFaceGetUp;
            string getUp = getUpFromFace ? "Get Up Face" : "Get Up Back";
            Vector3 yaw = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (yaw.sqrMagnitude < 0.01f) yaw = Vector3.ProjectOnPlane(-transform.up, Vector3.up);
            if (yaw.sqrMagnitude < 0.01f) yaw = Vector3.forward;
            Quaternion upright = Quaternion.LookRotation(yaw.normalized);

            if (Mover != null && Mover.Rigb != null)
            {
                Mover.Rigb.angularVelocity = Vector3.zero;
                Mover.Rigb.velocity = Vector3.zero;
                transform.rotation = upright;
                Mover.Rigb.MoveRotation(upright);
                Mover.Rigb.constraints = standingConstraints;
            }

            if (Mecanim)
            {
                Mecanim.SetBool("Ragdolled", false);
                Mecanim.SetBool("Grounded", true);
                PlayMatchedGetUp(getUp);
            }
            RpcKnockDown(false, getUp, getUpFromFace, false);

            yield return new WaitForSeconds(getUpFromFace ? GetUpFaceSeconds : GetUpBackSeconds);
            if (!IsKnockedDown || IsGrabbed || IsDead) yield break;
            ServerForceStand();
        }

        [Server]
        void ServerForceStand()
        {
            if (IsDead)
            {
                IsKnockedDown = true;
                InputActive = false;
                return;
            }

            bool wasDown = IsKnockedDown;
            bool wasPhysicalRagdoll = physicalRagdollActive;
            ResetServerJumpState();
            if (wasPhysicalRagdoll && Ragdoll != null && Ragdoll.Handler.WasInitialized
                && Ragdoll.Handler.AnimatingMode != RagdollHandler.EAnimatingMode.Standing)
            {
                Ragdoll.User_TransitionToStandingMode(0.35f, 0f);
            }
            physicalRagdollActive = false;
            if (gameplayCapsule != null)
                gameplayCapsule.enabled = true;
            IsKnockedDown = false;
            if (knockdownRoutine != null)
            {
                StopCoroutine(knockdownRoutine);
                knockdownRoutine = null;
            }

            if (Mover != null && Mover.Rigb != null)
            {
                Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                transform.rotation = upright;
                Mover.Rigb.rotation = upright;
                Mover.Rigb.angularVelocity = Vector3.zero;
                Mover.Rigb.velocity = new Vector3(0f, Mover.Rigb.velocity.y, 0f);
                Mover.Rigb.constraints = standingConstraints;
            }

            ServerIgnoreNearbyPlayerCollision(false);
            KnockbackVelocity = Vector3.zero;
            KnockbackSpinVelocity = 0f;
            knockbackUntil = 0f;

            if (IsGrabbed) return;

            InputActive = true;
            if (Mover) Mover.enabled = true;
            if (Mecanim)
            {
                Mecanim.SetBool("Ragdolled", false);
                Mecanim.SetBool("Grounded", true);
                Mecanim.SetBool("Moving", false);
            }
            RestoreComputerPoseAfterGetUp();
            if (wasDown)
            {
                if (!wasPhysicalRagdoll)
                    ResetVisualToStanding();
                RpcKnockDown(false, string.Empty, false, wasPhysicalRagdoll);
            }
        }

        [Server]
        void StabilizePhysicalGetUp()
        {
            if (Mover != null && Mover.Rigb != null)
            {
                Mover.Rigb.velocity = Vector3.zero;
                Mover.Rigb.angularVelocity = Vector3.zero;
                Mover.Rigb.constraints = standingConstraints;
            }
            if (gameplayCapsule != null)
                gameplayCapsule.enabled = true;
        }

        [ClientRpc]
        void RpcPlayHitVoice()
        {
            // Host 已经在服务端路径播放；这里只补远端客户端，避免听到两遍。
            if (isServer) return;
            PlayHitVoice();
        }

        void PlayHitVoice()
        {
            if (HitVoiceClip == null)
                HitVoiceClip = Resources.Load<AudioClip>("Audio/HitVoice_Ah");
            if (HitVoiceClip == null) return;

            if (hitVoiceSource == null)
                hitVoiceSource = Hero != null && Hero.HitAudio != null ? Hero.HitAudio : GetComponent<AudioSource>();
            if (hitVoiceSource == null) return;

            float minPitch = Mathf.Min(HitVoicePitchRange.x, HitVoicePitchRange.y);
            float maxPitch = Mathf.Max(HitVoicePitchRange.x, HitVoicePitchRange.y);
            hitVoiceSource.pitch = Random.Range(minPitch, maxPitch);
            hitVoiceSource.PlayOneShot(HitVoiceClip, HitVoiceVolume);
        }

        [ClientRpc]
        void RpcKnockDown(bool down, string clip, bool faceDown, bool physicalRagdoll)
        {
            if (isServer) return;

            // down=false + 有起身动画时仍处于受控起身阶段；直到服务端随后发来空 clip 才恢复操作。
            bool controlsLocked = down || !string.IsNullOrEmpty(clip);
            ApplyClientKnockdownLock(controlsLocked);

            if (physicalRagdoll && gameplayCapsule != null)
                gameplayCapsule.enabled = !down;

            if (!physicalRagdoll)
            {
                if (down) BeginVisualFall(faceDown);
                else if (string.IsNullOrEmpty(clip)) ResetVisualToStanding();
            }

            if (!physicalRagdoll && !UsesServerPoseSync && Mecanim != null)
            {
                Mecanim.SetBool("Ragdolled", false);
                Mecanim.SetBool("Grounded", !down);
                if (controlsLocked) Mecanim.SetBool("Moving", false);
                if (!string.IsNullOrEmpty(clip))
                {
                    if (down) Mecanim.CrossFadeInFixedTime(clip, 0.1f, 0);
                    else PlayMatchedGetUp(clip);
                }
            }

            if (!down && string.IsNullOrEmpty(clip))
                RestoreComputerPoseAfterGetUp();
        }

        void RestoreComputerPoseAfterGetUp()
        {
            if (syncHoldingComputer)
            {
                ApplyComputerHoldingPose(true);
                return;
            }

            // RA2 从 Falling 回到 Standing 时可能恢复倒地前缓存的 Animator Layer 状态。
            // 电脑已经掉落时再次归零 Base/Upper Body，避免起身后残留 Holding（打字）姿势。
            FinishComputerPickupAnimation();
            ApplyComputerHoldingPose(false);
        }

        void BeginVisualFall(bool faceDown)
        {
            if (visualRoot == null) return;
            if (visualFallRoutine != null) StopCoroutine(visualFallRoutine);
            visualFallRoutine = StartCoroutine(AnimateVisualFall(faceDown));
        }

        IEnumerator AnimateVisualFall(bool faceDown)
        {
            Vector3 startPosition = visualRoot.localPosition;
            Quaternion startRotation = visualRoot.localRotation;
            Vector3 targetPosition = visualStandingLocalPosition + Vector3.up * VisualFallLift;
            float fallDirection = faceDown ? 1f : -1f;
            float sideDirection = (netId & 1u) == 0u ? 1f : -1f;
            float recoilAngle = -fallDirection * 10f;
            float impactAngle = fallDirection * (VisualFallAngle + VisualTumbleDegrees + 6f);
            float targetAngle = fallDirection * (VisualFallAngle + VisualTumbleDegrees);
            Quaternion recoilRotation = visualStandingLocalRotation
                * Quaternion.Euler(recoilAngle, 0f, sideDirection * 5f);
            Quaternion impactRotation = visualStandingLocalRotation
                * Quaternion.Euler(impactAngle, 0f, sideDirection * 7f);
            Quaternion targetRotation = visualStandingLocalRotation
                * Quaternion.Euler(targetAngle, 0f, sideDirection * 2f);
            Vector3 recoilPosition = Vector3.Lerp(startPosition, targetPosition, 0.35f) + Vector3.up * 0.055f;
            Vector3 impactPosition = targetPosition - Vector3.up * 0.012f;
            float elapsed = 0f;

            // 受击先后仰并抬起一点，再加速倒下；这样不会像整块模型被开关直接放平。
            while (elapsed < VisualFallDelay)
            {
                if (visualRoot == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / VisualFallDelay));
                visualRoot.localPosition = Vector3.Lerp(startPosition, recoilPosition, t);
                visualRoot.localRotation = Quaternion.Slerp(startRotation, recoilRotation, t);
                yield return null;
            }

            elapsed = 0f;

            while (elapsed < VisualFallSeconds)
            {
                if (visualRoot == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / VisualFallSeconds);
                float acceleratedFall = t * t;
                visualRoot.localPosition = Vector3.Lerp(recoilPosition, impactPosition, acceleratedFall);
                // 直接按连续角度采样，避免 Quaternion.Slerp 把半圈翻滚自动改走最短路径。
                float tumbleAngle = Mathf.Lerp(recoilAngle, impactAngle, acceleratedFall);
                float sideAngle = Mathf.Lerp(sideDirection * 5f, sideDirection * 7f, acceleratedFall);
                visualRoot.localRotation = visualStandingLocalRotation * Quaternion.Euler(tumbleAngle, 0f, sideAngle);
                yield return null;
            }

            // 触地后回弹几度并稳定，保留重量感，但不允许根刚体继续翻滚。
            const float settleSeconds = 0.1f;
            elapsed = 0f;
            while (elapsed < settleSeconds)
            {
                if (visualRoot == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / settleSeconds));
                visualRoot.localPosition = Vector3.Lerp(impactPosition, targetPosition, t);
                visualRoot.localRotation = Quaternion.Slerp(impactRotation, targetRotation, t);
                yield return null;
            }

            if (visualRoot != null)
            {
                visualRoot.localPosition = targetPosition;
                visualRoot.localRotation = targetRotation;
            }
            visualFallRoutine = null;
        }

        void ResetVisualToStanding()
        {
            if (visualFallRoutine != null)
            {
                StopCoroutine(visualFallRoutine);
                visualFallRoutine = null;
            }
            if (visualRoot == null) return;
            visualRoot.localPosition = visualStandingLocalPosition;
            visualRoot.localRotation = visualStandingLocalRotation;
        }

        void PlayMatchedGetUp(string state)
        {
            if (Mecanim == null) return;
            if (visualRoot == null || !Mecanim.isHuman)
            {
                Mecanim.CrossFadeInFixedTime(state, 0.08f, 0);
                return;
            }

            Transform hips = Mecanim.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                ResetVisualToStanding();
                Mecanim.CrossFadeInFixedTime(state, 0.08f, 0);
                return;
            }

            // 记录倒地时的髋骨位置，再采样起身动画第一帧，用位置补偿消除切换支点造成的跳动。
            Vector3 fallenHipsPosition = hips.position;
            if (visualFallRoutine != null)
            {
                StopCoroutine(visualFallRoutine);
                visualFallRoutine = null;
            }

            visualRoot.localPosition = visualStandingLocalPosition;
            visualRoot.localRotation = visualStandingLocalRotation;
            Mecanim.Play(state, 0, 0f);
            Mecanim.Update(0f);

            Vector3 worldOffset = Vector3.ClampMagnitude(fallenHipsPosition - hips.position, 1.25f);
            Transform parent = visualRoot.parent;
            Vector3 localOffset = parent != null ? parent.InverseTransformVector(worldOffset) : worldOffset;
            visualRoot.localPosition = visualStandingLocalPosition + localOffset;
            visualFallRoutine = StartCoroutine(BlendVisualPositionToStanding());
        }

        IEnumerator BlendVisualPositionToStanding()
        {
            Vector3 startPosition = visualRoot.localPosition;
            float elapsed = 0f;

            while (elapsed < GetUpAlignmentBlendSeconds)
            {
                if (visualRoot == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / GetUpAlignmentBlendSeconds));
                visualRoot.localPosition = Vector3.Lerp(startPosition, visualStandingLocalPosition, t);
                visualRoot.localRotation = visualStandingLocalRotation;
                yield return null;
            }

            if (visualRoot != null)
            {
                visualRoot.localPosition = visualStandingLocalPosition;
                visualRoot.localRotation = visualStandingLocalRotation;
            }
            visualFallRoutine = null;
        }

        void ApplyClientKnockdownLock(bool locked)
        {
            IsKnockedDown = locked;
            if (!isLocalPlayer) return;

            InputActive = !locked && !IsDead && !IsGrabbed;
            pendingMove = Vector3.zero;

            if (Mover == null) return;
            if (!isServer && UsesServerPoseSync)
            {
                // 纯客户端始终回放服务器骨骼姿态，不能在解锁时重新开启本地物理抢写。
                Mover.enabled = false;
                if (Mover.Rigb != null)
                {
                    if (!Mover.Rigb.isKinematic)
                    {
                        Mover.Rigb.velocity = Vector3.zero;
                        Mover.Rigb.angularVelocity = Vector3.zero;
                    }
                    Mover.Rigb.isKinematic = true;
                    Mover.Rigb.interpolation = RigidbodyInterpolation.None;
                }
                return;
            }
            Mover.enabled = !locked && !IsDead;
            if (Mover.Rigb == null) return;

            Mover.Rigb.velocity = Vector3.zero;
            Mover.Rigb.angularVelocity = Vector3.zero;
            Mover.Rigb.isKinematic = locked;
            Mover.Rigb.interpolation = locked ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
        }

        [ClientRpc]
        void RpcPlayClip(string state, int layer)
        {
            if (isServer) return;
            if (Mecanim == null) return;

            if (isLocalPlayer)
            {
                if (state == "Punch F") BeginLocalAttackLocks(PunchAnimationLockSeconds, PunchMovementLockSeconds);
                else if (state == "Punch U") BeginLocalAttackLocks(UppercutAnimationLockSeconds, UppercutAnimationLockSeconds);
            }

            if (Mecanim.layerCount > 1)
            {
                if (layer == 1) Mecanim.SetLayerWeight(1, 1f);
                else if (state == "Holding Throw") Mecanim.SetLayerWeight(1, 0f);
            }
            if (state == "Punch F")
                PlayPunchVoiceAtAttackStart();
            Mecanim.CrossFadeInFixedTime(state, 0.145f, layer, 0f);
        }

        void PlayPunchVoiceAtAttackStart()
        {
            if (punchVoiceRelay == null)
                punchVoiceRelay = GetComponentInChildren<PunchAnimationEventRelay>(true);
            if (punchVoiceRelay != null)
                punchVoiceRelay.PlayPunchVoiceAtAttackStart();
        }

        [Server]
        public void ServerTeleport(Vector3 position)
        {
            ServerForceStand();
            if (heldComputer != null)
                ServerReleaseComputer(true);
            if (heldPlayer != null)
                ServerReleaseHeldPlayer(Vector3.zero, false);
            if (grabber != null)
                grabber.ServerReleaseHeldPlayer(Vector3.zero, false);

            transform.position = position;
            if (Mover && Mover.Rigb)
            {
                Mover.Rigb.velocity = Vector3.zero;
                Mover.Rigb.position = position;
            }
        }

        public override void OnStopServer()
        {
            if (heldComputer != null)
                ServerReleaseComputer(true);
            if (heldPlayer != null)
                ServerReleaseHeldPlayer(Vector3.zero, false);
            if (grabber != null)
                grabber.ServerReleaseHeldPlayer(Vector3.zero, false);
        }

        void ClientReconcile()
        {
            if (UsesServerPoseSync) return;
            if (IsKnockedDown || IsGrabbed) return;
            if (moveSync == null || !moveSync.HasServerPose || Mover == null || Mover.Rigb == null) return;

            Vector3 err = moveSync.ServerPosition - Mover.Rigb.position;
            err.y = 0f;
            if (err.magnitude < 1.25f) return;
            Vector3 target = Mover.Rigb.position + err * 0.25f;
            target.y = Mover.Rigb.position.y;
            Mover.Rigb.MovePosition(target);
        }

        void LateUpdate()
        {
            if (isServer)
            {
                if (serverCatching && heldComputer == null && InputActive && !IsKnockedDown && !IsGrabbed)
                    ServerTryPickupComputer();
                if (heldComputer != null)
                    ServerHoldComputerAtHands();
                if (heldPlayer != null)
                    ServerHoldPlayerAtHand();
                if (!UsesServerPoseSync && NetworkTime.localTime - lastMovePoseSend >= 0.05)
                {
                    lastMovePoseSend = NetworkTime.localTime;
                    RpcMovePose(NetworkTime.localTime, transform.position, transform.rotation);
                }
                return;
            }

            if (!UsesServerPoseSync && (!isLocalPlayer || IsKnockedDown || IsGrabbed) && moveSync != null)
                moveSync.ApplyRemoteInterpolation();
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcMovePose(double serverTime, Vector3 pos, Quaternion rot)
        {
            if (isServer) return;
            if (moveSync == null) return;
            moveSync.Receive(serverTime, pos, rot);
        }

        [Server]
        void ServerIgnoreComputerCollisions()
        {
            foreach (KpiComputerObjective computer in FindObjectsOfType<KpiComputerObjective>())
            {
                if (computer != null)
                    computer.ServerIgnoreCollisionsWith(this);
            }
        }

        [Server]
        bool ServerTryPickupComputer(Vector3 pickupLookDir = default)
        {
            if (heldComputer != null || IsHoldingPlayer || IsGrabbed || IsKnockedDown) return false;

            Physics.SyncTransforms();
            Vector3 origin = transform.position + Vector3.up * 0.35f;
            Vector3 forward = Vector3.ProjectOnPlane(pickupLookDir, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            KpiComputerObjective best = null;
            float bestDistance = float.MaxValue;
            float minDot = Mathf.Cos(ComputerPickupAngle * 0.5f * Mathf.Deg2Rad);
            foreach (KpiComputerObjective objective in FindObjectsOfType<KpiComputerObjective>())
            {
                if (objective == null || !objective.isActiveAndEnabled || objective.IsHeld) continue;

                Collider pickupCollider = objective.GetComponentInChildren<Collider>();
                Vector3 pickupPoint = pickupCollider != null
                    ? pickupCollider.ClosestPoint(origin)
                    : objective.GetPickupPoint(origin);
                Vector3 toComputer = pickupPoint - origin;
                if (toComputer.magnitude > ComputerPickupRange) continue;

                Vector3 horizontal = Vector3.ProjectOnPlane(toComputer, Vector3.up);
                float horizontalDistance = horizontal.magnitude;
                if (horizontalDistance > ComputerPickupRange) continue;

                if (horizontalDistance > ComputerPickupPointBlank
                    && horizontal.sqrMagnitude > 0.01f
                    && Vector3.Dot(forward, horizontal.normalized) < minDot)
                    continue;

                if (horizontalDistance < bestDistance)
                {
                    bestDistance = horizontalDistance;
                    best = objective;
                }
            }

            if (best == null || !best.ServerTryClaim(this)) return false;

            // 只有真正取得电脑所有权后才面向拾取方向并锁定动作。
            ServerFaceYaw(forward);
            heldComputer = best;
            float pickupSeconds = Mathf.Max(0.1f, ComputerPickupAnimationSeconds);
            attackLockedUntil = Mathf.Max(attackLockedUntil, Time.time + pickupSeconds);
            attackMovementLockedUntil = Mathf.Max(attackMovementLockedUntil, Time.time + pickupSeconds);
            StopMovementForAttack();
            PlayComputerPickupAnimation();
            RpcPlayComputerPickup(pickupSeconds);

            if (computerPickupRoutine != null) StopCoroutine(computerPickupRoutine);
            computerPickupRoutine = StartCoroutine(FinishComputerPickup(best, pickupSeconds));
            return true;
        }

        IEnumerator FinishComputerPickup(KpiComputerObjective computer, float pickupSeconds)
        {
            Vector3 startPosition = computer != null ? computer.transform.position : Vector3.zero;
            Quaternion startRotation = computer != null ? computer.transform.rotation : Quaternion.identity;
            float elapsed = 0f;
            float liftStart = pickupSeconds * 0.55f;
            while (elapsed < pickupSeconds)
            {
                if (heldComputer != computer || computer == null || IsKnockedDown || IsGrabbed || IsDead)
                {
                    computerPickupRoutine = null;
                    yield break;
                }

                elapsed += Time.deltaTime;
                if (elapsed >= liftStart)
                {
                    GetComputerHoldPose(out Vector3 targetPosition, out Quaternion targetRotation);
                    float liftT = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(liftStart, pickupSeconds, elapsed));
                    computer.ServerMoveHeld(
                        Vector3.Lerp(startPosition, targetPosition, liftT),
                        Quaternion.Slerp(startRotation, targetRotation, liftT));
                }
                yield return null;
            }

            computerPickupRoutine = null;
            if (heldComputer != computer || computer == null || IsKnockedDown || IsGrabbed || IsDead)
                yield break;

            syncHoldingComputer = true;
            FinishComputerPickupAnimation();
            ApplyComputerHoldingPose(true);
            ServerHoldComputerAtHands();
            RpcFinishComputerPickup();
        }

        void PlayComputerPickupAnimation()
        {
            if (Mecanim == null) return;
            Mecanim.CrossFadeInFixedTime("Force Grip", 0.08f, 0, 0f);
        }

        void FinishComputerPickupAnimation()
        {
            if (Mecanim != null)
                Mecanim.CrossFadeInFixedTime("Idle", 0.08f, 0, 0f);
        }

        [ClientRpc]
        void RpcPlayComputerPickup(float pickupSeconds)
        {
            if (isServer) return;
            PlayComputerPickupAnimation();
            if (isLocalPlayer)
                BeginLocalAttackLocks(pickupSeconds, pickupSeconds);
        }

        [ClientRpc]
        void RpcFinishComputerPickup()
        {
            if (isServer) return;
            FinishComputerPickupAnimation();
            ApplyComputerHoldingPose(true);
        }

        [ClientRpc]
        void RpcCancelComputerPickup()
        {
            if (isServer) return;
            FinishComputerPickupAnimation();
            ApplyComputerHoldingPose(false);
        }

        [Server]
        void ServerHoldComputerAtHands()
        {
            if (heldComputer == null)
            {
                if (syncHoldingComputer)
                {
                    syncHoldingComputer = false;
                    ApplyComputerHoldingPose(false);
                }
                return;
            }

            if (!syncHoldingComputer)
                syncHoldingComputer = true;

            GetComputerHoldPose(out Vector3 holdPosition, out Quaternion holdRotation);
            heldComputer.ServerMoveHeld(holdPosition, holdRotation);
        }

        public void GetComputerHoldPose(out Vector3 holdPosition, out Quaternion holdRotation)
        {
            Transform leftHand = null;
            Transform rightHand = Hero != null ? Hero.Hand : null;
            if (Mecanim != null && Mecanim.isHuman)
            {
                leftHand = Mecanim.GetBoneTransform(HumanBodyBones.LeftHand);
                if (rightHand == null)
                    rightHand = Mecanim.GetBoneTransform(HumanBodyBones.RightHand);
            }

            if (leftHand != null && rightHand != null)
            {
                holdPosition = (leftHand.position + rightHand.position) * 0.5f;
                holdPosition += transform.right * ComputerHoldOffset.x;
                holdPosition += Vector3.up * ComputerHoldOffset.y;
                holdPosition += transform.forward * ComputerHoldOffset.z;
            }
            else if (rightHand != null)
            {
                holdPosition = rightHand.TransformPoint(ComputerHoldOffset);
            }
            else
            {
                holdPosition = transform.TransformPoint(new Vector3(
                    ComputerHoldOffset.x,
                    1.35f + ComputerHoldOffset.y,
                    0.55f + ComputerHoldOffset.z));
            }

            holdRotation = Quaternion.LookRotation(transform.forward, Vector3.up)
                * Quaternion.Euler(ComputerHoldEuler);
        }

        [Server]
        public void ServerForceDropComputer(Vector3 extraVelocity = default)
        {
            if (heldComputer != null)
                ServerReleaseComputer(false, extraVelocity);
        }

        [Server]
        void ServerReleaseComputer(bool dropFromHands = false, Vector3 extraVelocity = default)
        {
            if (computerPickupRoutine != null)
            {
                StopCoroutine(computerPickupRoutine);
                computerPickupRoutine = null;
            }

            KpiComputerObjective computer = heldComputer;
            heldComputer = null;
            syncHoldingComputer = false;
            FinishComputerPickupAnimation();
            ApplyComputerHoldingPose(false);
            RpcCancelComputerPickup();
            if (computer == null) return;

            Vector3 horizontalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (horizontalForward.sqrMagnitude < 0.001f) horizontalForward = Vector3.forward;
            horizontalForward.Normalize();
            GetComputerHoldPose(out Vector3 holdPosition, out _);
            Vector3 releasePosition = dropFromHands
                ? holdPosition + horizontalForward * 0.25f
                : transform.position + horizontalForward * ComputerDropForward + Vector3.up * 0.35f;
            Vector3 releaseVelocity = extraVelocity;
            if (Mover != null && Mover.Rigb != null && !Mover.Rigb.isKinematic)
            {
                Vector3 carry = Vector3.ProjectOnPlane(Mover.Rigb.velocity, Vector3.up);
                releaseVelocity += Vector3.ClampMagnitude(carry, 3f);
            }

            computer.ServerRelease(this, releasePosition, releaseVelocity);
        }

        [Server]
        bool ServerTryGrabPlayer()
        {
            Physics.SyncTransforms();

            NetFAnnequinController best = null;
            float bestDist = float.MaxValue;
            foreach (NetFAnnequinController other in FindNearbyPlayers(2.0f, 90f, 1.3f))
            {
                if (other.IsHoldingPlayer || other.IsDead) continue;
                float dist = (other.transform.position - transform.position).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = other;
                }
            }

            if (best == null) return false;
            ServerBeginGrab(best);
            return true;
        }

        [Server]
        void ServerBeginGrab(NetFAnnequinController other)
        {
            heldPlayer = other;
            var capsule = other.GetComponent<CapsuleCollider>();
            holdBodyOffset = capsule != null ? capsule.height * 0.75f : 1.35f;

            grabIgnoreA = GetComponent<Collider>();
            grabIgnoreB = other.GetComponent<Collider>();
            if (grabIgnoreA != null && grabIgnoreB != null)
                Physics.IgnoreCollision(grabIgnoreA, grabIgnoreB, true);

            other.ServerBeginGrabbed(this);
            if (Hero) Hero.SetHoldingPose(true);
        }

        [Server]
        public void ServerReleaseHeldPlayer(Vector3 throwVelocity, bool applyThrow)
        {
            var victim = heldPlayer;
            heldPlayer = null;

            if (grabIgnoreA != null && grabIgnoreB != null)
                Physics.IgnoreCollision(grabIgnoreA, grabIgnoreB, false);
            grabIgnoreA = null;
            grabIgnoreB = null;

            if (Hero) Hero.SetHoldingPose(false);
            if (victim == null) return;

            victim.ServerEndGrabbed();
            if (applyThrow && throwVelocity.sqrMagnitude > 0.0001f)
                victim.ServerKnockDown(throwVelocity, attributes != null ? attributes.AttackDamage : 10);
        }

        [Server]
        public void ServerBeginGrabbed(NetFAnnequinController who)
        {
            serverCatching = false;
            if (heldComputer != null)
                ServerReleaseComputer(true);

            if (IsKnockedDown)
            {
                IsKnockedDown = false;
                if (knockdownRoutine != null)
                {
                    StopCoroutine(knockdownRoutine);
                    knockdownRoutine = null;
                }
                if (Mover != null && Mover.Rigb != null)
                    Mover.Rigb.constraints = standingConstraints;
            }

            grabber = who;
            InputActive = false;
            pendingMove = Vector3.zero;
            KnockbackVelocity = Vector3.zero;
            KnockbackSpinVelocity = 0f;
            knockbackUntil = 0f;
            if (Mover)
            {
                Mover.enabled = false;
                if (Mover.Rigb)
                {
                    Mover.Rigb.velocity = Vector3.zero;
                    Mover.Rigb.angularVelocity = Vector3.zero;
                }
            }
        }

        [Server]
        public void ServerEndGrabbed()
        {
            grabber = null;
            InputActive = !IsDead;
            if (Mover) Mover.enabled = !IsDead;
        }

        [Server]
        void ServerHoldPlayerAtHand()
        {
            if (heldPlayer == null) return;

            Transform hand = Hero != null && Hero.Hand != null
                ? Hero.Hand
                : (Mecanim != null && Mecanim.isHuman ? Mecanim.GetBoneTransform(HumanBodyBones.RightHand) : transform);
            Vector3 holdPoint = hand.TransformPoint(new Vector3(-0.088f, 0.114f, -0.061f));
            Vector3 rootPos = holdPoint + Vector3.down * holdBodyOffset;
            heldPlayer.ServerHoldAt(rootPos, transform.rotation);
        }

        [Server]
        public void ServerHoldAt(Vector3 position, Quaternion facing)
        {
            transform.position = position;
            transform.rotation = facing;
            if (Mover != null && Mover.Rigb != null)
            {
                Mover.Rigb.position = position;
                Mover.Rigb.rotation = facing;
                Mover.Rigb.velocity = Vector3.zero;
                Mover.Rigb.angularVelocity = Vector3.zero;
            }
        }
    }
}
