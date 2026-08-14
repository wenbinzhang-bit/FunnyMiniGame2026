using System;
using System.Collections.Generic;
using FIMSpace.FProceduralAnimation;
using FIMSpace.RagdollAnimatorDemo;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 服务端权威的双手抓取/投掷逻辑,移植自 ClaymanController 的嵌套 TriggerHelper。
    /// 触发器、Joint 都只存在于服务端的物理假体上;客户端只通过姿态流看到结果。
    /// </summary>
    public class NetPlayerGrab : NetworkBehaviour
    {
        public RagdollAnimator2 RagdollAnimator;
        public Animator Mecanim;

        [Space(5)]
        public float TouchRadius = 0.15f;
        public Vector3 TouchOffset = new Vector3(0f, 0f, -0.12f);

        public enum EReachingAction : byte { None = 0, Forward = 1, Up = 2 }

        EReachingAction reachingAction = EReachingAction.None;
        float tryCatchingBlend;

        TriggerHelper leftHandTouch;
        TriggerHelper rightHandTouch;
        readonly List<TriggerHelper> catchedBy = new List<TriggerHelper>();

        NetPlayerMotor motor;

        void Awake()
        {
            motor = GetComponent<NetPlayerMotor>();
            if (RagdollAnimator == null) RagdollAnimator = GetComponent<RagdollAnimator2>();
        }

        /// <summary>由 NetPlayerMotor 在服务端 Ragdoll 初始化完成后调用。</summary>
        [Server]
        public void ServerInitHands()
        {
            PrepareHandComponents(RagdollAnimator.Handler.GetBoneSetupByBoneID(ERagdollBoneID.RightHand));
            PrepareHandComponents(RagdollAnimator.Handler.GetBoneSetupByBoneID(ERagdollBoneID.LeftHand));
        }

        [Server]
        public void ServerSetReach(EReachingAction action)
        {
            if (leftHandTouch == null || rightHandTouch == null) return;

            var preReaching = reachingAction;
            reachingAction = action;

            if (reachingAction == EReachingAction.Forward)
            {
                leftHandTouch.reachDirection = Vector3.forward;
                rightHandTouch.reachDirection = leftHandTouch.reachDirection;
            }
            else if (reachingAction == EReachingAction.Up)
            {
                leftHandTouch.reachDirection = (Vector3.up + Vector3.forward * 0.33f).normalized;
                rightHandTouch.reachDirection = leftHandTouch.reachDirection;
            }
            else if (preReaching != EReachingAction.None)
            {
                leftHandTouch.UndoCatch();
                rightHandTouch.UndoCatch();
            }
        }

        [Server]
        public void ServerThrow(Vector3 pushVector)
        {
            if (leftHandTouch == null || rightHandTouch == null) return;

            leftHandTouch.Throw(pushVector);
            rightHandTouch.Throw(pushVector);
            tryCatchingBlend = 0f;
            reachingAction = EReachingAction.None;
        }

        [Server]
        public void ServerUndoCatchAll()
        {
            if (leftHandTouch == null || rightHandTouch == null) return;

            leftHandTouch.UndoCatch();
            rightHandTouch.UndoCatch();
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (!isServer) return;
            if (leftHandTouch == null || rightHandTouch == null) return;

            if (reachingAction != EReachingAction.None)
                tryCatchingBlend = Mathf.MoveTowards(tryCatchingBlend, 1f, Time.deltaTime * 3f);
            else
                tryCatchingBlend = Mathf.MoveTowards(tryCatchingBlend, 0f, Time.deltaTime * 7f);

            if (leftHandTouch.IsCatchingSomething)
            {
                leftHandTouch.SetHandIKPosition(1f, leftHandTouch.holderObject.transform.position);
            }
            else if (tryCatchingBlend > 0f)
            {
                leftHandTouch.SetHandIKPosition(tryCatchingBlend, leftHandTouch.CalculateFrontHandPosition());
            }

            if (rightHandTouch.IsCatchingSomething)
            {
                rightHandTouch.SetHandIKPosition(1f, rightHandTouch.holderObject.transform.position);
            }
            else if (tryCatchingBlend > 0f)
            {
                rightHandTouch.SetHandIKPosition(tryCatchingBlend, rightHandTouch.CalculateFrontHandPosition());
            }
        }

        void PrepareHandComponents(RagdollChainBone hand)
        {
            // Generating helper trigger collider for detecting pickables around
            GameObject triggerHelper = new GameObject("Touch Trigger");

            triggerHelper.transform.SetParent(hand.PhysicalDummyBone, true);
            triggerHelper.transform.localPosition = TouchOffset;
            triggerHelper.transform.localScale = Vector3.one;
            triggerHelper.transform.localRotation = Quaternion.identity;

            SphereCollider sph = triggerHelper.AddComponent<SphereCollider>();
            sph.isTrigger = true;
            sph.radius = TouchRadius;

            TriggerHelper tHelper = triggerHelper.AddComponent<TriggerHelper>();
            tHelper.Initialize(this, hand);
            if (tHelper.IsRight) rightHandTouch = tHelper; else leftHandTouch = tHelper;
        }

        void OnCatchedBy(TriggerHelper triggerHelper)
        {
            catchedBy.Add(triggerHelper);
            if (motor) motor.ServerSetCaughtCount(catchedBy.Count);
        }

        void OnUncatchedBy(TriggerHelper triggerHelper)
        {
            catchedBy.Remove(triggerHelper);
            if (motor) motor.ServerSetCaughtCount(catchedBy.Count);
        }

        class TriggerHelper : MonoBehaviour
        {
            public NetPlayerGrab Parent;
            public bool IsRight;
            public AvatarIKGoal IKGoal;

            public Transform BaseTransform => Parent.RagdollAnimator.GetBaseTransform;
            public Animator Mecanim => Parent.Mecanim;

            [NonSerialized] public RagdollChainBone handBone;
            [NonSerialized] public RagdollChainBone shoulderBone;
            [NonSerialized] public float limbLength;

            public bool IsCatchingSomething => holdingRightNow != null;
            public ClayCatchable holdingRightNow;
            internal GameObject holderObject;
            internal Rigidbody holderRigidbody;
            ConfigurableJoint holderJoint;
            ConfigurableJoint generatedCatchJoint;

            public Vector3 reachDirection = Vector3.forward;

            public void SetHandIKPosition(float blend, Vector3 position)
            {
                Mecanim.SetIKPositionWeight(IKGoal, blend);
                Mecanim.SetIKPosition(IKGoal, position);
            }

            public Vector3 CalculateFrontHandPosition()
            {
                Vector3 targetPos = shoulderBone.BoneProcessor.AnimatorPosition;
                float yDiff = Mathf.Abs(shoulderBone.BoneProcessor.AnimatorPosition.y - handBone.BoneProcessor.AnimatorPosition.y);
                yDiff /= limbLength;

                targetPos += BaseTransform.TransformDirection(reachDirection) * limbLength * 0.875f;
                targetPos.y += yDiff * 0.15f;

                Vector3 posInLocal = BaseTransform.InverseTransformPoint(targetPos);
                posInLocal.x *= 0.7f;

                targetPos = BaseTransform.TransformPoint(posInLocal);

                return targetPos;
            }

            public void Initialize(NetPlayerGrab parent, RagdollChainBone bone)
            {
                Parent = parent;
                handBone = bone;
                shoulderBone = bone.ParentChain.BoneSetups[0];
                limbLength = bone.ParentChain.CalculateLength();
                IsRight = bone.ParentChain.ChainType == ERagdollChainType.RightArm;
                IKGoal = IsRight ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
            }

            void OnTriggerEnter(Collider other)
            {
                if (holdingRightNow != null) return;

                if (Parent.reachingAction != EReachingAction.None && Parent.tryCatchingBlend > 0.5f)
                {
                    if (Parent.RagdollAnimator.Handler.ContainsPhysicalBoneTransform(other.transform)) return; // Dont catch self!

                    ClayCatchable catched = other.gameObject.GetComponent<ClayCatchable>();

                    if (catched)
                    {
                        StartHolding(catched, other);

                        var rBone = catched.GetComponent<RagdollAnimator2BoneIndicator>();
                        if (rBone)
                        {
                            var otherGrab = rBone.ParentRagdollAnimator.GetBaseTransform.GetComponent<NetPlayerGrab>();
                            if (otherGrab) otherGrab.OnCatchedBy(this);
                        }
                    }
                }
            }

            void StartHolding(ClayCatchable catchable, Collider other)
            {
                holdingRightNow = catchable;

                GameObject catchedObject = catchable.gameObject;
                Transform catchedTransform = holdingRightNow.transform;

                if (holderObject != null) Destroy(holderObject.gameObject);

                holderObject = new GameObject(name + "-Holder");
                holderObject.transform.SetParent(catchedTransform, true);
                holderObject.transform.position = other.ClosestPoint(handBone.GameRigidbody.position);
                holderObject.transform.rotation = handBone.BoneProcessor.AnimatorRotation;

                holderRigidbody = holderObject.AddComponent<Rigidbody>();
                if (holderObject.isStatic) holderRigidbody.isKinematic = true;
                holderRigidbody.constraints = RigidbodyConstraints.FreezePosition;
                holderRigidbody.useGravity = false;

                if (catchable.Moving)
                {
                    holderRigidbody.mass = 1f; // Avoiding overpulling objects which are moving
                    holderRigidbody.constraints = RigidbodyConstraints.None;
                }
                else
                {
                    holderRigidbody.mass = 3f; // Higher mass = stronger pull of hands towards attachment when using FreezePosition
                }

                holderJoint = holderObject.AddComponent<ConfigurableJoint>();
                holderJoint.connectedBody = handBone.GameRigidbody;

                RagdollHandler.SetConfigurableJointMotionLock(holderJoint, ConfigurableJointMotion.Locked);
                holderJoint.autoConfigureConnectedAnchor = false;
                holderJoint.connectedAnchor = handBone.GameRigidbody.transform.InverseTransformPoint(holderObject.transform.position);

                generatedCatchJoint = catchedObject.AddComponent<ConfigurableJoint>();
                if (generatedCatchJoint.gameObject.isStatic) generatedCatchJoint.gameObject.GetComponent<Rigidbody>().isKinematic = true;
                generatedCatchJoint.connectedBody = holderRigidbody;
                RagdollHandler.SetConfigurableJointMotionLock(generatedCatchJoint, ConfigurableJointMotion.Locked);
            }

            public void UndoCatch()
            {
                if (holdingRightNow == null) return;

                var rBone = holdingRightNow.GetComponent<RagdollAnimator2BoneIndicator>();
                if (rBone)
                {
                    var otherGrab = rBone.ParentRagdollAnimator.GetBaseTransform.GetComponent<NetPlayerGrab>();
                    if (otherGrab) otherGrab.OnUncatchedBy(this);
                }

                Destroy(holderObject);
                Destroy(generatedCatchJoint);

                holdingRightNow = null;
            }

            public void Throw(Vector3 pushVector)
            {
                if (holdingRightNow == null) return;

                var holdRig = holdingRightNow.GetComponent<Rigidbody>();
                if (holdRig) holdRig.AddForce(pushVector, ForceMode.VelocityChange);

                var indic = holdingRightNow.GetComponent<RagdollAnimator2BoneIndicator>();
                if (indic) indic.ParentHandler.User_AddAllBonesImpact(pushVector * 0.07f, 0.05f);

                UndoCatch();
            }
        }
    }
}
