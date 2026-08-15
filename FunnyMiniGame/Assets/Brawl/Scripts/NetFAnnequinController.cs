using System.Collections;
using System.Collections.Generic;
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

        [SyncVar] int score;
        public int Score { get => score; set => score = value; }

        [SyncVar(hook = nameof(OnSyncMoving))] bool syncMoving;
        [SyncVar(hook = nameof(OnSyncGrounded))] bool syncGrounded = true;
        [SyncVar(hook = nameof(OnSyncSpeed))] float syncSpeed;

        public bool InputActive { get; set; } = true;
        public Vector3 SpawnPosition { get; set; }

        public uint NetId => netId;
        public Transform Transform => transform;
        public PlayerAttributes Attributes => attributes;
        public bool IsDead => attributes != null && attributes.IsDead;
        public bool WantsToMove => pendingMove.sqrMagnitude > 0.0001f;
        public bool IsMoveAuthority => Mover != null && Mover.Rigb != null && !Mover.Rigb.isKinematic && (isServer || isLocalPlayer);
        public bool IsInKnockback => isServer && Time.time < knockbackUntil;
        public bool IsHoldingPlayer => heldPlayer != null;
        public bool IsGrabbed => grabber != null;
        public bool IsKnockedDown { get; private set; }
        public Vector3 KnockbackVelocity { get; private set; }

        Vector3 pendingMove;
        float lastSendTime;
        Vector3 lastSentDir;
        byte lastSentButtons;
        float baseMoveSpeed = 4.5f;
        FAnnequinMouseActions mouseActions;
        FAnnequinGrabHelper grabHelper;
        PlayerAttributes attributes;
        FAnnequinMeleeVictim meleeVictim;
        NetworkTransformReliable netTransform;
        BrawlMoveSync moveSync;
        double lastMovePoseSend;
        Coroutine throwFallback;
        Coroutine punchFallback;
        Coroutine knockdownRoutine;
        float knockbackUntil;
        RigidbodyConstraints standingConstraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        float lastPlayerPunchTime = -1f;
        float lastReceivedHitTime = -1f;
        NetFAnnequinController heldPlayer;
        NetFAnnequinController grabber;
        Collider grabIgnoreA;
        Collider grabIgnoreB;
        float holdBodyOffset = 1.35f;

        const float ThrowEventDelay = 0.28f;
        const float PunchHitDelay = 0.33f;
        const float UppercutHitDelay = 0.3f;
        const float KnockdownGroundSeconds = 1.55f;
        const float GetUpSeconds = 1.15f;

        const float HeartbeatInterval = 0.1f;
        const byte BtnSprint = 1;
        const byte BtnCtrl = 2;

        void Awake()
        {
            if (Mover == null) Mover = GetComponent<FBasic_RigidbodyMover>();
            if (Hero == null) Hero = GetComponent<Demo_Ragd_Hero1>();
            if (Mecanim == null) Mecanim = GetComponent<Animator>();
            if (Mover) baseMoveSpeed = Mover.MovementSpeed;

            attributes = GetComponent<PlayerAttributes>();
            if (attributes == null) attributes = gameObject.AddComponent<PlayerAttributes>();

            mouseActions = GetComponent<FAnnequinMouseActions>();
            if (mouseActions == null) mouseActions = gameObject.AddComponent<FAnnequinMouseActions>();
            mouseActions.OnShortPressAttack = () => CmdPunchOrThrow(0, GetLookDir());
            mouseActions.OnLongPressGrab = () => CmdCatch(GetLookDir());
            mouseActions.OnRightClickRelease = () => CmdThrow(GetLookDir());
            mouseActions.enabled = false;

            if (GetComponent<FAnnequinLocomotionFix>() == null)
                gameObject.AddComponent<FAnnequinLocomotionFix>();
            grabHelper = GetComponent<FAnnequinGrabHelper>();
            if (grabHelper == null) grabHelper = gameObject.AddComponent<FAnnequinGrabHelper>();

            meleeVictim = GetComponent<FAnnequinMeleeVictim>();
            if (meleeVictim == null) meleeVictim = gameObject.AddComponent<FAnnequinMeleeVictim>();
            meleeVictim.OnHitByMelee = ServerReceiveMeleeHit;
            netTransform = GetComponent<NetworkTransformReliable>();
            moveSync = GetComponent<BrawlMoveSync>();
            if (moveSync == null)
                moveSync = gameObject.AddComponent<BrawlMoveSync>();
        }

        public override void OnStartServer()
        {
            SpawnPosition = transform.position;
            if (Mover != null && Mover.Rigb != null)
                standingConstraints = Mover.Rigb.constraints;

            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.enabled = true;
                capsule.isTrigger = false;
                if (capsule.radius < 0.38f) capsule.radius = 0.38f;
            }

            if (Hero != null)
                Hero.OnMeleeHit = ServerOnHeroMeleeHit;
            if (meleeVictim != null)
                meleeVictim.OnHitByMelee = ServerReceiveMeleeHit;
            if (attributes != null)
                attributes.Died += ServerOnDied;

            ConfigureNetworkSync();
            DisableLocalDemoInput();
        }

        public override void OnStartClient()
        {
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

            var poseSync = GetComponent<RagdollNetworkSync>();
            if (poseSync != null)
                poseSync.enabled = false;

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
            if (mouseActions) mouseActions.enabled = !IsDead;
            if (attributes != null)
                attributes.HealthChanged += OnLocalHealthChanged;
            if (!isServer && Mover != null)
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
            if (attributes != null)
                attributes.HealthChanged -= OnLocalHealthChanged;
            if (mouseActions)
            {
                mouseActions.CancelHold();
                mouseActions.enabled = false;
            }
        }

        void OnLocalHealthChanged(int current, int max)
        {
            if (mouseActions == null) return;
            if (current <= 0)
            {
                mouseActions.CancelHold();
                mouseActions.enabled = false;
            }
            else if (isLocalPlayer)
            {
                mouseActions.enabled = true;
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
            if ((isServer || isLocalPlayer) && Mover && Mover.enabled)
            {
                Vector3 dir = InputActive ? pendingMove : Vector3.zero;
                Mover.moveDirectionWorld = dir;
                if (dir.sqrMagnitude > 0.0001f)
                    Mover.SetTargetRotation(dir);
            }

            if (isLocalPlayer && !isServer)
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
            if (inputValue != Vector2.zero)
            {
                float camYaw = Camera.main ? Camera.main.transform.eulerAngles.y : 0f;
                worldDir = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(inputValue.x, 0f, inputValue.y);
            }

            byte buttons = 0;
            if (Input.GetKey(KeyCode.LeftShift)) buttons |= BtnSprint;
            if (Input.GetKey(KeyCode.LeftControl)) buttons |= BtnCtrl;

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

            if (Input.GetKeyDown(KeyCode.Space)) CmdJump();
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.F))
                CmdPunchOrThrow(0, GetLookDir());
        }

        [Command]
        void CmdSetInput(Vector3 moveWorldDir, byte buttons)
        {
            try
            {
            if (!InputActive)
            {
                pendingMove = Vector3.zero;
                return;
            }

            moveWorldDir.y = 0f;
            pendingMove = moveWorldDir.sqrMagnitude > 0.0001f ? Vector3.ClampMagnitude(moveWorldDir, 1f) : Vector3.zero;

            if (Mover)
            {
                if ((buttons & BtnSprint) != 0 && Mover.HoldShiftForSpeed > 0f)
                    Mover.MovementSpeed = Mover.HoldShiftForSpeed;
                else if ((buttons & BtnCtrl) != 0 && Mover.HoldCtrlForSpeed > 0f)
                    Mover.MovementSpeed = Mover.HoldCtrlForSpeed;
                else
                    Mover.MovementSpeed = baseMoveSpeed;
            }
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
                if (!InputActive || Hero == null) return;
                Hero.DoJump();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdJump: {e.Message}");
            }
        }

        Vector3 GetLookDir()
        {
            return Camera.main ? Camera.main.transform.forward : transform.forward;
        }

        [Command]
        void CmdPunchOrThrow(byte kind, Vector3 lookDir)
        {
            try
            {
                if (!InputActive || Hero == null) return;
                if (Hero.IsHoldingUp || Hero.IsThrowing || IsHoldingPlayer)
                {
                    ServerStartThrow(lookDir);
                    return;
                }

                ServerFaceYaw(lookDir);
                if (kind == 0) Hero.DoPunchF();
                else Hero.DoPunchU();
                RpcPlayClip(kind == 0 ? "Punch F" : "Punch U", 0);
                ServerPunchPlayers(kind);

                if (punchFallback != null) StopCoroutine(punchFallback);
                punchFallback = StartCoroutine(PunchHitFallback(kind));
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
                if (grabHelper) grabHelper.EnsureCatchMagnet();

                if (Hero.IsHoldingUp || Hero.IsThrowing || IsHoldingPlayer)
                {
                    ServerStartThrow(lookDir);
                    return;
                }

                ServerFaceYaw(lookDir);
                Hero.DoCatch(0.45f, 0.55f, 1.2f);
                if (!Hero.IsHoldingUp)
                    ServerTryGrabPlayer();
                if (Hero.IsHoldingUp || IsHoldingPlayer)
                    Hero.SetHoldingPose(true);
                RpcPlayClip("Holding", 1);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdCatch: {e.Message}");
            }
        }

        [Command]
        void CmdThrow(Vector3 lookDir)
        {
            try
            {
                if (!InputActive || Hero == null) return;
                if (!Hero.IsHoldingUp && !IsHoldingPlayer)
                {
                    Hero.DoRelease();
                    return;
                }

                ServerStartThrow(lookDir);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CmdThrow: {e.Message}");
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
        void ServerReceiveMeleeHit(Vector3 impactDirection)
        {
            Vector3 dir = impactDirection.sqrMagnitude > 0.01f ? impactDirection.normalized : -transform.forward;
            if (dir.y < 0.35f) dir.y = 0.35f;
            ServerKnockDown(dir.normalized * 11f);
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

            var targets = FindNearbyPlayers(3.5f, 160f, 2.4f);
            if (targets.Count == 0)
            {
                NetFAnnequinController nearest = FindNearestPlayer(3.5f);
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
            KnockbackVelocity = Vector3.MoveTowards(KnockbackVelocity, Vector3.zero, deltaTime * 18f);
        }

        [Server]
        public void ServerKnockDown(Vector3 impulse, int damage = 10)
        {
            if (IsDead) return;
            if (IsGrabbed) return;
            if (Time.time - lastReceivedHitTime < 0.45f) return;
            lastReceivedHitTime = Time.time;
            if (attributes != null && damage > 0)
                attributes.ServerTakeDamage(damage);
            if (heldPlayer != null)
                ServerReleaseHeldPlayer(Vector3.zero, false);

            IsKnockedDown = true;
            InputActive = false;
            pendingMove = Vector3.zero;

            Vector3 vel = impulse.sqrMagnitude > 0.01f ? impulse : -transform.forward * 8f + Vector3.up * 4f;
            vel.y = Mathf.Max(vel.y, 4f);
            if (new Vector3(vel.x, 0f, vel.z).magnitude < 7f)
            {
                Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
                if (horizontal.sqrMagnitude < 0.01f) horizontal = -transform.forward;
                horizontal = horizontal.normalized * 8f;
                vel.x = horizontal.x;
                vel.z = horizontal.z;
            }

            if (Mover != null && Mover.Rigb != null)
            {
                Mover.enabled = false;
                if (Mover.Rigb.isKinematic) Mover.Rigb.isKinematic = false;
                Mover.Rigb.constraints = RigidbodyConstraints.None;
                Vector3 push = new Vector3(vel.x, 0f, vel.z).normalized * 0.45f;
                Mover.Rigb.position += push;
                transform.position = Mover.Rigb.position;
                Mover.Rigb.velocity = vel;

                Vector3 torqueAxis = Vector3.Cross(Vector3.up, new Vector3(vel.x, 0f, vel.z).normalized);
                if (torqueAxis.sqrMagnitude < 0.01f) torqueAxis = transform.right;
                Mover.Rigb.AddTorque(torqueAxis.normalized * 16f, ForceMode.VelocityChange);
            }

            KnockbackVelocity = new Vector3(vel.x, 0f, vel.z);
            knockbackUntil = Time.time + 0.2f;
            ServerIgnoreNearbyPlayerCollision(true);

            if (Mecanim)
            {
                Mecanim.SetBool("Ragdolled", false);
                Mecanim.SetBool("Grounded", false);
                Mecanim.SetBool("Moving", false);
                Mecanim.CrossFadeInFixedTime("Fall", 0.08f, 0);
            }
            RpcKnockDown(true, "Fall");

            if (IsDead)
            {
                ServerStayDown();
                return;
            }

            if (knockdownRoutine != null) StopCoroutine(knockdownRoutine);
            knockdownRoutine = StartCoroutine(KnockdownRoutine());
        }

        [Server]
        void ServerOnDied()
        {
            ServerStayDown();
        }

        [Server]
        void ServerStayDown()
        {
            if (grabber != null)
                grabber.ServerReleaseHeldPlayer(Vector3.zero, false);
            if (heldPlayer != null)
                ServerReleaseHeldPlayer(Vector3.zero, false);

            IsKnockedDown = true;
            InputActive = false;
            pendingMove = Vector3.zero;
            if (knockdownRoutine != null)
            {
                StopCoroutine(knockdownRoutine);
                knockdownRoutine = null;
            }

            if (mouseActions)
            {
                mouseActions.CancelHold();
                mouseActions.enabled = false;
            }

            if (Mecanim)
            {
                Mecanim.SetBool("Ragdolled", false);
                Mecanim.SetBool("Grounded", false);
                Mecanim.SetBool("Moving", false);
                Mecanim.CrossFadeInFixedTime("Fall", 0.08f, 0);
            }
            RpcKnockDown(true, "Fall");
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
            yield return new WaitForSeconds(KnockdownGroundSeconds);
            if (!IsKnockedDown || IsGrabbed || IsDead) yield break;

            string getUp = Vector3.Dot(transform.forward, Vector3.up) > 0.1f ? "Get Up Face" : "Get Up Back";
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
                Mecanim.CrossFadeInFixedTime(getUp, 0.12f, 0);
            }
            RpcKnockDown(false, getUp);

            yield return new WaitForSeconds(GetUpSeconds);
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
            if (wasDown) RpcKnockDown(false, string.Empty);
        }

        [ClientRpc]
        void RpcKnockDown(bool down, string clip)
        {
            if (isServer) return;
            if (Mecanim == null) return;
            Mecanim.SetBool("Ragdolled", false);
            Mecanim.SetBool("Grounded", !down);
            if (down) Mecanim.SetBool("Moving", false);
            if (!string.IsNullOrEmpty(clip))
                Mecanim.CrossFadeInFixedTime(clip, 0.1f, 0);
        }

        [ClientRpc]
        void RpcPlayClip(string state, int layer)
        {
            if (isServer) return;
            if (Mecanim == null) return;
            if (Mecanim.layerCount > 1)
            {
                if (layer == 1) Mecanim.SetLayerWeight(1, 1f);
                else if (state == "Holding Throw") Mecanim.SetLayerWeight(1, 0f);
            }
            Mecanim.CrossFadeInFixedTime(state, 0.145f, layer, 0f);
        }

        [Server]
        public void ServerTeleport(Vector3 position)
        {
            ServerForceStand();
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
            if (attributes != null)
                attributes.Died -= ServerOnDied;
            if (heldPlayer != null)
                ServerReleaseHeldPlayer(Vector3.zero, false);
            if (grabber != null)
                grabber.ServerReleaseHeldPlayer(Vector3.zero, false);
        }

        void ClientReconcile()
        {
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
                if (heldPlayer != null)
                    ServerHoldPlayerAtHand();
                if (NetworkTime.localTime - lastMovePoseSend >= 0.05)
                {
                    lastMovePoseSend = NetworkTime.localTime;
                    RpcMovePose(NetworkTime.localTime, transform.position, transform.rotation);
                }
                return;
            }

            if (!isLocalPlayer && moveSync != null)
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
