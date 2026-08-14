using FIMSpace.FProceduralAnimation;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace FIMSpace.RagdollAnimatorDemo
{

    public class ClaymanController : MonoBehaviour
    {
        public RagdollAnimator2 RagdollAnimator;

        [Header( "Requires IK Pass for hand catch animations" )]
        public Animator Mecanim;

        [Space( 5 )]
        public float TargetMovementSpeed = 4f;
        public float JumpPower = 10f;

        [Space( 3 )]
        [Range( 0f, 2f )] public float AnchorRotationPower = 1f;

        [FPD_FixedCurveWindow]
        [Tooltip( "Anchor rotation power can become weaker when anchor is rotating off straight up rotation" )]
        public AnimationCurve PowerOnDeflection = AnimationCurve.EaseInOut( 0f, 1f, 1f, 0.25f );

        [Space( 3 )]
        [Range( 0f, 1f )] public float AnchorRotationPowerOnFall = 0.2f;

        [Space( 5 )]
        public float TouchRadius = 1f;
        public Vector3 TouchOffset = Vector3.zero;

        [Space( 5 )]
        public LayerMask GroundMask = 0 >> 1;
        public float ExtraRaycastDistance = 0.11f;
        public float SpherecastRadius = 0.2f;

        [Space( 5 )]
        public bool DrawRangeGizmo = false;
        public bool InputActive = true;
        public bool DebugCatch = false;

        TriggerHelper leftHandTouch;
        TriggerHelper rightHandTouch;

        List<FootHelper> feet = new List<FootHelper>();
        public Vector3 SpawnPosition { get; set; }
        public float InitialMass { get; set; }

        List<TriggerHelper> catchedBy = new List<TriggerHelper>();

        private IEnumerator Start() // IEnumerator in case someone is using ragdoll animator with custom update order
        {
            SpawnPosition = transform.position;
            InitialMass = RagdollAnimator.Handler.ReferenceMass;

            while( RagdollAnimator.Handler.WasInitialized == false ) yield return null; // Wait for initialized ragdoll animator

            // Ensure initialization for physical ragdoll dummy existence
            RagdollAnimator.Handler.Initialize( RagdollAnimator, RagdollAnimator.gameObject );

            // If ragdoll dummy was generated, we can attach touch colliders to it
            PrepareHandComponents( RagdollAnimator.Handler.GetBoneSetupByBoneID( ERagdollBoneID.RightHand ) );
            PrepareHandComponents( RagdollAnimator.Handler.GetBoneSetupByBoneID( ERagdollBoneID.LeftHand ) );

            RagdollAnimator.Handler.AnchorUpdateMode = RagdollHandler.ERagdollAnchorUpdateMode.None;

            // Gather last rigidbodies of legs to detect grounded state if any leg is close to the ground
            for( int i = 0; i < RagdollAnimator.Handler.Chains.Count; i++ )
            {
                if( RagdollAnimator.Handler.Chains[i].ChainType.IsLeg() == false ) continue;
                int lastId = RagdollAnimator.Handler.Chains[i].BoneSetups.Count - 1;
                var footB = RagdollAnimator.Handler.Chains[i].BoneSetups[lastId];

                FootHelper footH = new FootHelper();
                footH.Init( RagdollAnimator.GetBaseTransform, footB.GameRigidbody.transform );
                feet.Add( footH );
            }


            // Add catchable references for players interactions
            RagdollAnimator.Handler.CallOnAllRagdollBones( ( RagdollChainBone bone ) =>
                {
                    bone.GameRigidbody.gameObject.AddComponent<ClayCatchable>().Moving = true;
                } );

            yield break;
        }

        public void OnRespawned()
        {
            leftHandTouch.UndoCatch();
            rightHandTouch.UndoCatch();
        }

        private void Update()
        {
            var anchor = RagdollAnimator.Handler.GetAnchorBoneController;

            if( InputActive )
            {
                CalculateMovementInput();
                CalculateActionsInput();
            }

            ComputeMovementVelocity();

            transform.position = Vector3.Lerp( transform.position, anchor.GameRigidbody.transform.position, Time.deltaTime * 30f );
        }

        #region Movement

        public Vector3 moveDirectionWorld { get; set; }
        protected Quaternion targetInstantRotation;
        public Vector3 currentWorldAccel { get; protected set; }

        protected float jumpTime = -1f;
        [NonSerialized] public float jumpRequest = 0f;

        void CalculateMovementInput()
        {
            Vector2 inputValue = Vector2.zero;
            if( Input.GetKey( KeyCode.W ) || Input.GetKey( KeyCode.UpArrow ) ) inputValue.y += 1f;
            if( Input.GetKey( KeyCode.S ) || Input.GetKey( KeyCode.DownArrow ) ) inputValue.y -= 1f;
            if( Input.GetKey( KeyCode.A ) || Input.GetKey( KeyCode.LeftArrow ) ) inputValue.x -= 1f;
            if( Input.GetKey( KeyCode.D ) || Input.GetKey( KeyCode.RightArrow ) ) inputValue.x += 1f;
            inputValue.Normalize();

            if( inputValue == Vector2.zero ) { SetMoveDirection( Vector2.zero ); return; }

            Vector3 mappedCameraInput = Quaternion.Euler( 0f, Camera.main.transform.eulerAngles.y, 0f ) * new Vector3( inputValue.x, 0, inputValue.y );
            SetMoveDirection( mappedCameraInput );
            SetTargetRotation( mappedCameraInput );
        }

        void ComputeMovementVelocity()
        {
            currentWorldAccel = Vector3.MoveTowards( currentWorldAccel, moveDirectionWorld * TargetMovementSpeed, Time.deltaTime * 8f );
            if( Mecanim ) Mecanim.SetFloat( "Speed", currentWorldAccel.magnitude );
        }

        public void SetMoveDirection( Vector3 dir, bool setDir = true )
        {
            if( dir == Vector3.zero )
            {
                if( Mecanim ) Mecanim.SetBool( "Moving", false );
            }
            else
            {
                if( Mecanim ) Mecanim.SetBool( "Moving", true );
            }

            moveDirectionWorld = dir;
        }

        public void SetTargetRotation( Vector3 dir )
        {
            targetInstantRotation = Quaternion.LookRotation( dir );
            if( currentWorldAccel == Vector3.zero ) currentWorldAccel = new Vector3( 0.0000001f, 0f, 0f );

            transform.rotation = targetInstantRotation;
        }

        public void RequestJump( float power )
        {
            jumpRequest = power;
        }

        #endregion


        #region Actions

        enum EReachingAction { None, Forward, Up }
        EReachingAction reachingAction = EReachingAction.None;

        void CalculateActionsInput()
        {
            var preReaching = reachingAction;
            reachingAction = DebugCatch ? EReachingAction.Forward : EReachingAction.None;

            if( Input.GetKey( KeyCode.Q ) ) reachingAction = EReachingAction.Up;
            if( Input.GetKey( KeyCode.E ) ) reachingAction = EReachingAction.Forward;

            if( Input.GetKey( KeyCode.R ) )
            {
                // Throw
                leftHandTouch.Throw( transform.forward * 10f + Vector3.up * 2f );
                rightHandTouch.Throw( transform.forward * 10f + Vector3.up * 2f );
                tryCatchingBlend = 0f;
                reachingAction = EReachingAction.None;
            }

            if( Input.GetKeyDown( KeyCode.Space ) ) RequestJump( JumpPower );

            if( reachingAction == EReachingAction.Forward )
            {
                leftHandTouch.reachDirection = Vector3.forward;
                rightHandTouch.reachDirection = leftHandTouch.reachDirection;
            }
            else if( reachingAction == EReachingAction.Up )
            {
                leftHandTouch.reachDirection = ( Vector3.up + Vector3.forward * 0.33f ).normalized;
                rightHandTouch.reachDirection = leftHandTouch.reachDirection;
            }
            else if( reachingAction == EReachingAction.None )
            {
                if( preReaching != EReachingAction.None )
                {
                    leftHandTouch.UndoCatch();
                    rightHandTouch.UndoCatch();
                }
            }
        }

        #endregion


        #region Fixed update movement

        public bool isGrounded { get; protected set; } = true;

        private void FixedUpdate()
        {
            var anchor = RagdollAnimator.Handler.GetAnchorBoneController;

            Vector3 newVelo = anchor.GameRigidbody.velocity;
            float posePowerMul = Vector3.Dot( Vector3.up, anchor.GameRigidbody.transform.TransformDirection( anchor.LocalUp ) );
            posePowerMul = PowerOnDeflection.Evaluate( 1f - posePowerMul );

            if( RagdollAnimator.IsInFallingOrSleepMode == false )
            {
                RagdollHandlerUtilities.AddRigidbodyTorqueToRotateTowards( anchor.GameRigidbody, anchor.BoneProcessor.AnimatorRotation, AnchorRotationPower * posePowerMul );

                newVelo.x = Mathf.MoveTowards( newVelo.x, currentWorldAccel.x, Time.fixedDeltaTime * 100f );
                newVelo.z = Mathf.MoveTowards( newVelo.z, currentWorldAccel.z, Time.fixedDeltaTime * 100f );
                anchor.GameRigidbody.velocity = newVelo;
            }
            else
            {
                RagdollHandlerUtilities.AddRigidbodyTorqueToRotateTowards( anchor.GameRigidbody, anchor.BoneProcessor.AnimatorRotation, AnchorRotationPowerOnFall );
            }

            if( jumpRequest != 0f )
            {
                if( isGrounded == true )
                {
                    isGrounded = false;
                    jumpTime = Time.time;
                    if( Mecanim ) Mecanim.SetBool( "Grounded", false );
                    newVelo = anchor.GameRigidbody.velocity;
                    newVelo.y += jumpRequest;
                    anchor.GameRigidbody.velocity = newVelo;
                    RagdollAnimator.User_SwitchFallState();
                }

                jumpRequest = 0f;
            }

            if( Time.time - jumpTime > 0.15f )
            {
                bool groundHit = false;

                for( int i = 0; i < feet.Count; i++ )
                {
                    groundHit = feet[i].RaycastHitted( GroundMask, SpherecastRadius, ( isGrounded ? 0.15f : 0f ) + ExtraRaycastDistance );
                    if( groundHit ) break;
                }

                if( groundHit == false )
                {
                    isGrounded = false;
                    RagdollAnimator.User_SwitchFallState();
                    if( Mecanim ) Mecanim.SetBool( "Grounded", false );
                }
                else if( groundHit == true )
                {
                    isGrounded = true;
                    RagdollAnimator.User_SwitchFallState( true );
                    if( Mecanim ) Mecanim.SetBool( "Grounded", true );
                }

            }

        }

        #endregion


        float tryCatchingBlend = 0f;
        private void OnAnimatorIK( int layerIndex )
        {
            if( reachingAction != EReachingAction.None )
            {
                tryCatchingBlend = Mathf.MoveTowards( tryCatchingBlend, 1f, Time.deltaTime * 3f );
            }
            else
            {
                tryCatchingBlend = Mathf.MoveTowards( tryCatchingBlend, 0f, Time.deltaTime * 7f );
            }

            if( leftHandTouch.IsCatchingSomething )
            {
                leftHandTouch.SetHandIKPosition( 1f, leftHandTouch.holderObject.transform.position );
            }
            else
            {
                if( tryCatchingBlend > 0f )
                    leftHandTouch.SetHandIKPosition( tryCatchingBlend, leftHandTouch.CalculateFrontHandPosition() );
            }

            if( rightHandTouch.IsCatchingSomething )
            {
                rightHandTouch.SetHandIKPosition( 1f, rightHandTouch.holderObject.transform.position );
            }
            else
            {
                if( tryCatchingBlend > 0f )
                    rightHandTouch.SetHandIKPosition( tryCatchingBlend, rightHandTouch.CalculateFrontHandPosition() );
            }
        }

        void PrepareHandComponents( RagdollChainBone hand )
        {
            // Generating helper trigger collider for detecting pickables around
            GameObject triggerHelper = new GameObject( "Touch Trigger" );

            triggerHelper.transform.SetParent( hand.PhysicalDummyBone, true );

            triggerHelper.transform.localPosition = TouchOffset;
            triggerHelper.transform.localScale = Vector3.one;
            triggerHelper.transform.localRotation = Quaternion.identity;

            SphereCollider sph = triggerHelper.AddComponent<SphereCollider>();
            sph.isTrigger = true;
            sph.radius = TouchRadius;

            TriggerHelper tHelper = triggerHelper.AddComponent<TriggerHelper>();
            tHelper.Initialize( this, hand );
            if( tHelper.IsRight ) rightHandTouch = tHelper; else leftHandTouch = tHelper;
        }



        class TriggerHelper : MonoBehaviour
        {
            public ClaymanController Parent;
            public bool IsRight = false;
            public AvatarIKGoal IKGoal;

            public Transform BaseTransform => Parent.RagdollAnimator.GetBaseTransform;
            public Animator Mecanim => Parent.Mecanim;

            [NonSerialized] public RagdollChainBone handBone;
            [NonSerialized] public RagdollChainBone shoulderBone;
            [NonSerialized] public float limbLength;

            public bool IsCatchingSomething => holdingRightNow != null;
            public ClayCatchable holdingRightNow = null;
            internal GameObject holderObject = null;
            internal Rigidbody holderRigidbody = null;
            ConfigurableJoint holderJoint = null;
            ConfigurableJoint generatedCatchJoint = null;

            public void SetHandIKPosition( float blend, Vector3 position )
            {
                Mecanim.SetIKPositionWeight( IKGoal, blend );
                Mecanim.SetIKPosition( IKGoal, position );
            }

            public Vector3 reachDirection = Vector3.forward;

            public Vector3 CalculateFrontHandPosition()
            {
                Vector3 targetPos = shoulderBone.BoneProcessor.AnimatorPosition;
                float yDiff = Mathf.Abs( shoulderBone.BoneProcessor.AnimatorPosition.y - handBone.BoneProcessor.AnimatorPosition.y );
                yDiff /= limbLength;

                targetPos += BaseTransform.TransformDirection( reachDirection ) * limbLength * 0.875f;
                targetPos.y += yDiff * 0.15f;

                Vector3 posInLocal = BaseTransform.InverseTransformPoint( targetPos );
                posInLocal.x *= 0.7f;

                targetPos = BaseTransform.TransformPoint( posInLocal );

                return targetPos;
            }

            public void Initialize( ClaymanController parent, RagdollChainBone bone )
            {
                Parent = parent;
                handBone = bone;
                shoulderBone = bone.ParentChain.BoneSetups[0];
                limbLength = bone.ParentChain.CalculateLength();
                IsRight = bone.ParentChain.ChainType == ERagdollChainType.RightArm;
                IKGoal = IsRight ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
            }

            private void FixedUpdate()
            {
                //if ( holdingRightNow && holderRigidbody )
                //{
                //    holderRigidbody.automaticCenterOfMass = false;
                //    holderRigidbody.centerOfMass = Vector3.zero; //holderRigidbody.transform.InverseTransformPoint( handBone.GameRigidbody.position);
                //}
            }

            private void OnTriggerEnter( Collider other )
            {
                if( holdingRightNow != null ) return;

                if( Parent.reachingAction != EReachingAction.None && Parent.tryCatchingBlend > 0.5f )
                {
                    if( Parent.RagdollAnimator.Handler.ContainsPhysicalBoneTransform( other.transform ) ) return; // Dont catch self!

                    ClayCatchable catched = other.gameObject.GetComponent<ClayCatchable>();

                    if( catched )
                    {
                        StartHolding( catched, other );

                        var rBone = catched.GetComponent<RagdollAnimator2BoneIndicator>();
                        if( rBone )
                        {
                            var clayman = rBone.ParentRagdollAnimator.GetBaseTransform.GetComponent<ClaymanController>();
                            if( clayman ) clayman.OnCatchedBy( this );
                        }
                    }
                }
            }

            public void DrawPlaymodeGizmos()
            {
                Gizmos.DrawLine( shoulderBone.BoneProcessor.AnimatorPosition, CalculateFrontHandPosition() );

                if( holdingRightNow )
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine( handBone.BoneProcessor.AnimatorPosition, holderObject.transform.position );
                    Gizmos.DrawLine( handBone.BoneProcessor.AnimatorPosition, holderObject.transform.position );
                }
            }

            void StartHolding( ClayCatchable catchable, Collider other )
            {
                holdingRightNow = catchable;

                GameObject catchedObject = catchable.gameObject;
                Transform catchedTransform = holdingRightNow.transform;

                if( holderObject != null ) Destroy( holderObject.gameObject );

                holderObject = new GameObject( name + "-Holder" );
                holderObject.transform.SetParent( catchedTransform, true );
                holderObject.transform.position = other.ClosestPoint( handBone.GameRigidbody.position );
                holderObject.transform.rotation = handBone.BoneProcessor.AnimatorRotation;

                holderRigidbody = holderObject.AddComponent<Rigidbody>();
                if( holderObject.isStatic ) holderRigidbody.isKinematic = true;
                holderRigidbody.constraints = RigidbodyConstraints.FreezePosition;
                holderRigidbody.useGravity = false;

                if( catchable.Moving )
                {
                    holderRigidbody.mass = 1f; // Avoiding overpulling objects which are moving
                    holderRigidbody.constraints = RigidbodyConstraints.None;
                }
                else
                    holderRigidbody.mass = 3f; // Higher mass = unity physics stronger pull hands towards attachement when using RigidbodyConstraints.FreezePosition

                holderJoint = holderObject.AddComponent<ConfigurableJoint>();
                holderJoint.connectedBody = handBone.GameRigidbody;

                RagdollHandler.SetConfigurableJointMotionLock( holderJoint, ConfigurableJointMotion.Locked );
                holderJoint.autoConfigureConnectedAnchor = false;
                holderJoint.connectedAnchor = handBone.GameRigidbody.transform.InverseTransformPoint( holderObject.transform.position );

                generatedCatchJoint = catchedObject.AddComponent<ConfigurableJoint>();
                if( generatedCatchJoint.gameObject.isStatic ) generatedCatchJoint.gameObject.GetComponent<Rigidbody>().isKinematic = true;
                generatedCatchJoint.connectedBody = holderRigidbody;
                RagdollHandler.SetConfigurableJointMotionLock( generatedCatchJoint, ConfigurableJointMotion.Locked );
            }

            public void UndoCatch()
            {
                if( holdingRightNow == null ) return;

                var rBone = holdingRightNow.GetComponent<RagdollAnimator2BoneIndicator>();
                if( rBone )
                {
                    var clayman = rBone.ParentRagdollAnimator.GetBaseTransform.GetComponent<ClaymanController>();
                    if( clayman ) clayman.OnUncatchedBy( this );
                }

                Destroy( holderObject );
                Destroy( generatedCatchJoint );

                holdingRightNow = null;
            }

            public void Throw( Vector3 pushVector )
            {
                if( holdingRightNow == null ) return;

                var holdRig = holdingRightNow.GetComponent<Rigidbody>();
                if( holdRig ) holdRig.AddForce( pushVector, ForceMode.VelocityChange );

                var indic = holdingRightNow.GetComponent<RagdollAnimator2BoneIndicator>();
                if( indic ) { indic.ParentHandler.User_AddAllBonesImpact( pushVector * 0.07f, 0.05f ); }

                UndoCatch();
            }
        }

        private void OnCatchedBy( TriggerHelper triggerHelper )
        {
            catchedBy.Add( triggerHelper );

            RagdollAnimator.Handler.ReferenceMass = InitialMass / 2f;
            RagdollAnimator.Handler.User_UpdateRigidbodyParametersForAllBones();
        }

        private void OnUncatchedBy( TriggerHelper triggerHelper )
        {
            catchedBy.Remove( triggerHelper );

            if( catchedBy.Count <= 0 )
            {
                RagdollAnimator.Handler.ReferenceMass = InitialMass;
                RagdollAnimator.Handler.User_UpdateRigidbodyParametersForAllBones();
            }
        }

        class FootHelper
        {
            public Transform bone;
            public Vector3 localUp;

            public void Init( Transform parent, Transform bone )
            {
                this.bone = bone;
                localUp = bone.InverseTransformDirection( parent.up );
            }

            public bool RaycastHitted( LayerMask mask, float radius, float extraDistance )
            {
                Vector3 upRef = bone.TransformDirection( localUp );

                // Cast in average direction of feed down and world gravity down
                Vector3 castDown = Vector3.LerpUnclamped( -upRef, Physics.gravity.normalized, 0.5f ).normalized;

                RaycastHit hit;
                return Physics.SphereCast( bone.position + upRef, radius, castDown, out hit, 1f + extraDistance - radius / 2f, mask, QueryTriggerInteraction.Ignore );
            }
        }



        private void OnDrawGizmosSelected()
        {
            if( DrawRangeGizmo == false ) return;
            if( RagdollAnimator == null ) return;

            if( RagdollAnimator.Handler.WasInitialized == false )
            {
                RagdollChainBone hand = RagdollAnimator.Handler.GetBoneSetupByBoneID( ERagdollBoneID.RightHand );
                if( hand == null ) return;
                DrawHandColliderGizmo( hand );

                hand = RagdollAnimator.Handler.GetBoneSetupByBoneID( ERagdollBoneID.LeftHand );
                if( hand == null ) return;
                DrawHandColliderGizmo( hand );
            }
            else
            {
                Gizmos.color = Color.green;
                leftHandTouch.DrawPlaymodeGizmos();
                Gizmos.color = Color.blue;
                rightHandTouch.DrawPlaymodeGizmos();
            }

            for( int i = 0; i < feet.Count; i++ )
            {
                Gizmos.DrawWireSphere( feet[i].bone.position - feet[i].bone.TransformDirection( feet[i].localUp ) * ExtraRaycastDistance, SpherecastRadius );
                Gizmos.DrawRay( feet[i].bone.position, -feet[i].bone.TransformDirection( feet[i].localUp ) * ExtraRaycastDistance );
            }
        }

        void DrawHandColliderGizmo( RagdollChainBone hand )
        {
            Gizmos.matrix = hand.SourceBone.localToWorldMatrix;
            Gizmos.color = new Color( 0f, 1f, 0f, 0.5f );
            Gizmos.DrawWireSphere( TouchOffset, TouchRadius );
        }


    }
}