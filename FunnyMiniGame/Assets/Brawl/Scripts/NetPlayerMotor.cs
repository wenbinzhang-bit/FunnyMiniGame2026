using System;
using System.Collections;
using System.Collections.Generic;
using FIMSpace.FProceduralAnimation;
using FIMSpace.RagdollAnimatorDemo;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 服务端权威的 Ragdoll 角色马达。
    /// 移植自 ClaymanController:输入不再来自本地键盘,而是客户端通过 Command 上报的意图。
    /// 全部物理只在服务端执行;客户端表现由 RagdollNetworkSync 的姿态流驱动。
    /// </summary>
    public class NetPlayerMotor : NetworkBehaviour
    {
        public RagdollAnimator2 RagdollAnimator;

        [Header("Requires IK Pass for hand catch animations")]
        public Animator Mecanim;

        [Space(5)]
        public float TargetMovementSpeed = 4f;
        public float JumpPower = 25f;

        [Range(0f, 2f)] public float AnchorRotationPower = 1f;

        [Tooltip("Anchor rotation power can become weaker when anchor is rotating off straight up rotation")]
        public AnimationCurve PowerOnDeflection = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f);

        [Range(0f, 1f)] public float AnchorRotationPowerOnFall = 0.1f;

        [Tooltip("根旋转平滑速度(度/秒)。Demo 原版是瞬时转向,联机后瞬时跳变会被姿态流放大成抖动")]
        public float RotationDegreesPerSecond = 420f;

        [Space(5)]
        public LayerMask GroundMask = 1;
        public float ExtraRaycastDistance = 0.12f;
        public float SpherecastRadius = 0.25f;

        [SyncVar] public int Score;

        /// <summary>服务端:是否接受该玩家的移动输入(对局管理器可冻结)。</summary>
        [NonSerialized] public bool InputActive = true;

        public bool ServerInitialized { get; private set; }
        public Vector3 SpawnPosition { get; set; }
        public float InitialMass { get; private set; }

        public Vector3 moveDirectionWorld { get; private set; }
        public Vector3 currentWorldAccel { get; private set; }
        public bool isGrounded { get; private set; } = true;

        NetPlayerGrab grab;
        readonly List<FootHelper> feet = new List<FootHelper>();
        Quaternion targetInstantRotation;
        bool hasTargetRotation;
        float jumpTime = -1f;
        float jumpRequest;

        void Awake()
        {
            grab = GetComponent<NetPlayerGrab>();
            if (RagdollAnimator == null) RagdollAnimator = GetComponent<RagdollAnimator2>();
        }

        public override void OnStartServer()
        {
            StartCoroutine(ServerInitRagdoll());
        }

        public override void OnStopServer()
        {
            // Mirror 销毁玩家对象时子物体销毁顺序不定,RA2 的 OnDisable 清理会踩到已销毁的骨骼刚体。
            // 在销毁前主动禁用 RA2,让它在假体完整时走完 SwitchDummyPhysics 清理。
            if (RagdollAnimator) RagdollAnimator.enabled = false;
        }

        IEnumerator ServerInitRagdoll()
        {
            SpawnPosition = transform.position;

            while (RagdollAnimator.Handler.WasInitialized == false) yield return null;

            // Ensure initialization for physical ragdoll dummy existence
            RagdollAnimator.Handler.Initialize(RagdollAnimator, RagdollAnimator.gameObject);

            InitialMass = RagdollAnimator.Handler.ReferenceMass;
            RagdollAnimator.Handler.AnchorUpdateMode = RagdollHandler.ERagdollAnchorUpdateMode.None;

            // Gather last rigidbodies of legs to detect grounded state if any leg is close to the ground
            for (int i = 0; i < RagdollAnimator.Handler.Chains.Count; i++)
            {
                if (RagdollAnimator.Handler.Chains[i].ChainType.IsLeg() == false) continue;
                int lastId = RagdollAnimator.Handler.Chains[i].BoneSetups.Count - 1;
                var footB = RagdollAnimator.Handler.Chains[i].BoneSetups[lastId];

                FootHelper footH = new FootHelper();
                footH.Init(RagdollAnimator.GetBaseTransform, footB.GameRigidbody.transform);
                feet.Add(footH);
            }

            // Add catchable references for players interactions
            RagdollAnimator.Handler.CallOnAllRagdollBones((RagdollChainBone bone) =>
            {
                bone.GameRigidbody.gameObject.AddComponent<ClayCatchable>().Moving = true;
            });

            if (grab) grab.ServerInitHands();

            ServerInitialized = true;
            Debug.Log($"BRAWL_SMOKE: SERVER_RAGDOLL_INIT netId={netId}");
        }

        #region Commands (来自 NetPlayerInput)

        [Command]
        public void CmdSetInput(Vector3 moveWorldDir, byte reachState)
        {
            if (!InputActive) { SetMoveDirection(Vector3.zero); return; }

            moveWorldDir.y = 0f;
            if (moveWorldDir.sqrMagnitude > 0.0001f)
            {
                moveWorldDir = Vector3.ClampMagnitude(moveWorldDir, 1f);
                SetMoveDirection(moveWorldDir);
                SetTargetRotation(moveWorldDir);
            }
            else
            {
                SetMoveDirection(Vector3.zero);
            }

            if (grab) grab.ServerSetReach((NetPlayerGrab.EReachingAction)reachState);
        }

        [Command]
        public void CmdJump()
        {
            if (!InputActive) return;
            jumpRequest = JumpPower;
        }

        [Command]
        public void CmdThrow()
        {
            if (!InputActive) return;
            if (grab) grab.ServerThrow(transform.forward * 10f + Vector3.up * 2f);
        }

        #endregion

        #region Server movement (移植 ClaymanController)

        void Update()
        {
            if (!isServer || !ServerInitialized) return;

            var anchor = RagdollAnimator.Handler.GetAnchorBoneController;

            ComputeMovementVelocity();

            transform.position = Vector3.Lerp(transform.position, anchor.GameRigidbody.transform.position, Time.deltaTime * 30f);

            // 平滑转向:瞬时转向在联机姿态流下会表现为变向抖动
            if (hasTargetRotation)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetInstantRotation, RotationDegreesPerSecond * Time.deltaTime);
        }

        void ComputeMovementVelocity()
        {
            currentWorldAccel = Vector3.MoveTowards(currentWorldAccel, moveDirectionWorld * TargetMovementSpeed, Time.deltaTime * 8f);
            if (Mecanim) Mecanim.SetFloat("Speed", currentWorldAccel.magnitude);
        }

        public void SetMoveDirection(Vector3 dir)
        {
            if (Mecanim) Mecanim.SetBool("Moving", dir != Vector3.zero);
            moveDirectionWorld = dir;
        }

        void SetTargetRotation(Vector3 dir)
        {
            targetInstantRotation = Quaternion.LookRotation(dir);
            hasTargetRotation = true;
            if (currentWorldAccel == Vector3.zero) currentWorldAccel = new Vector3(0.0000001f, 0f, 0f);
        }

        void FixedUpdate()
        {
            if (!isServer || !ServerInitialized) return;

            var anchor = RagdollAnimator.Handler.GetAnchorBoneController;

            Vector3 newVelo = anchor.GameRigidbody.velocity;
            float posePowerMul = Vector3.Dot(Vector3.up, anchor.GameRigidbody.transform.TransformDirection(anchor.LocalUp));
            posePowerMul = PowerOnDeflection.Evaluate(1f - posePowerMul);

            if (RagdollAnimator.IsInFallingOrSleepMode == false)
            {
                RagdollHandlerUtilities.AddRigidbodyTorqueToRotateTowards(anchor.GameRigidbody, anchor.BoneProcessor.AnimatorRotation, AnchorRotationPower * posePowerMul);

                newVelo.x = Mathf.MoveTowards(newVelo.x, currentWorldAccel.x, Time.fixedDeltaTime * 100f);
                newVelo.z = Mathf.MoveTowards(newVelo.z, currentWorldAccel.z, Time.fixedDeltaTime * 100f);
                anchor.GameRigidbody.velocity = newVelo;
            }
            else
            {
                RagdollHandlerUtilities.AddRigidbodyTorqueToRotateTowards(anchor.GameRigidbody, anchor.BoneProcessor.AnimatorRotation, AnchorRotationPowerOnFall);
            }

            if (jumpRequest != 0f)
            {
                if (isGrounded)
                {
                    isGrounded = false;
                    jumpTime = Time.time;
                    if (Mecanim) Mecanim.SetBool("Grounded", false);
                    newVelo = anchor.GameRigidbody.velocity;
                    newVelo.y += jumpRequest;
                    anchor.GameRigidbody.velocity = newVelo;
                    RagdollAnimator.User_SwitchFallState();
                }

                jumpRequest = 0f;
            }

            if (Time.time - jumpTime > 0.15f)
            {
                bool groundHit = false;

                for (int i = 0; i < feet.Count; i++)
                {
                    groundHit = feet[i].RaycastHitted(GroundMask, SpherecastRadius, (isGrounded ? 0.15f : 0f) + ExtraRaycastDistance);
                    if (groundHit) break;
                }

                if (groundHit == false)
                {
                    isGrounded = false;
                    RagdollAnimator.User_SwitchFallState();
                    if (Mecanim) Mecanim.SetBool("Grounded", false);
                }
                else
                {
                    isGrounded = true;
                    RagdollAnimator.User_SwitchFallState(true);
                    if (Mecanim) Mecanim.SetBool("Grounded", true);
                }
            }
        }

        #endregion

        /// <summary>服务端:把角色传送到指定位置(重生/回合重置)。</summary>
        [Server]
        public void ServerTeleport(Vector3 position)
        {
            if (grab) grab.ServerUndoCatchAll();

            transform.position = position;

            if (!ServerInitialized || RagdollAnimator.Handler.WasInitialized == false) return;

            var anchor = RagdollAnimator.Handler.GetAnchorBoneController;
            anchor.GameRigidbody.velocity = Vector3.zero;
            anchor.GameRigidbody.position = position;
            RagdollAnimator.Handler.User_Teleport(position);
            RagdollAnimator.Handler.User_WarpRefresh();
        }

        /// <summary>服务端:被抓住/放开时的质量调整(移植 OnCatchedBy/OnUncatchedBy)。</summary>
        [Server]
        public void ServerSetCaughtCount(int catchers)
        {
            if (!ServerInitialized) return;

            RagdollAnimator.Handler.ReferenceMass = catchers > 0 ? InitialMass / 2f : InitialMass;
            RagdollAnimator.Handler.User_UpdateRigidbodyParametersForAllBones();
        }

        class FootHelper
        {
            public Transform bone;
            public Vector3 localUp;

            public void Init(Transform parent, Transform bone)
            {
                this.bone = bone;
                localUp = bone.InverseTransformDirection(parent.up);
            }

            public bool RaycastHitted(LayerMask mask, float radius, float extraDistance)
            {
                Vector3 upRef = bone.TransformDirection(localUp);

                // Cast in average direction of feet down and world gravity down
                Vector3 castDown = Vector3.LerpUnclamped(-upRef, Physics.gravity.normalized, 0.5f).normalized;

                return Physics.SphereCast(bone.position + upRef, radius, castDown, out _, 1f + extraDistance - radius / 2f, mask, QueryTriggerInteraction.Ignore);
            }
        }
    }
}
