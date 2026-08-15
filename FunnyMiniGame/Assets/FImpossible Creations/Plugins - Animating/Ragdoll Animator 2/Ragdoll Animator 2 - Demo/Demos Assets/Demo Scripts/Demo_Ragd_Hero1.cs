using FIMSpace.FProceduralAnimation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FIMSpace.RagdollAnimatorDemo
{
    //[AddComponentMenu( "FImpossible Creations/Demos/Fimpossible Demo Hero 1" )]
    [DefaultExecutionOrder( 10 )]
    public class Demo_Ragd_Hero1 : FimpossibleComponent
    {
        public FBasic_RigidbodyMover Mover;
        public Animator Mecanim;
        public RagdollAnimator2 Ragdoll;

        public LayerMask HittableLayermask;
        public GameObject PushParticle;

        [Space( 6 )]
        public float PunchPower = 10f;

        public float UppercutPower = 10f;
        public float PushForcePower = 50f;
        public float ThrowPower = 50f;
        public float GripEndPushPower = 10f;

        [Space( 6 )]
        public Transform UpperArm;

        public Transform Hand;
        public AudioSource HitAudio;

        [Header( "References" )]
        public RA2MagnetPoint CatchMagnet;

        public RA2MagnetPoint GripMagnet;

        [Header( "Input" )]
        [Tooltip( "联机时由 NetFAnnequinController 关掉,避免每台机器都读本地键盘" )]
        public bool ProcessInput = true;

        public KeyCode PunchKey = KeyCode.None;

        public KeyCode PunchUppercutKey = KeyCode.None;
        public KeyCode PushForceKey = KeyCode.None;
        public KeyCode GridForceKey = KeyCode.None;
        public KeyCode CatchKey = KeyCode.None;

        private int actionHash = Animator.StringToHash( "Action" );
        private bool InAction => Mecanim != null && Mecanim.runtimeAnimatorController != null && Mecanim.GetBool( actionHash );
        private bool Action
        { get { return Mecanim.GetBool( actionHash ); } set { Mecanim.SetBool( actionHash, value ); } }

        private List<Collider> toIgnore = new List<Collider>();

        private void Start()
        {
            if( Mover == null ) Mover = GetComponent<FBasic_RigidbodyMover>();
            if( Mover == null ) return;

            Collider[] myCol = Mover.GetComponentsInChildren<Collider>();
            foreach( var col in myCol ) toIgnore.Add( col );
            if( Ragdoll ) foreach( var col in Ragdoll.Settings.User_GetAllDummyColliders() ) toIgnore.Add( col );

            if( GripMagnet ) GripMagnet.transform.SetParent( null );
        }

        private void LateUpdate()
        {
            if( ProcessInput == false )
            {
                UpdateHoldingUp();
                return;
            }

            Vector2 moveDirectionLocal = Vector2.zero;

            if( InAction == false )
            {
                if( Input.GetKey( KeyCode.A ) || Input.GetKey( KeyCode.LeftArrow ) ) moveDirectionLocal += Vector2.left;
                else if( Input.GetKey( KeyCode.D ) || Input.GetKey( KeyCode.RightArrow ) ) moveDirectionLocal += Vector2.right;

                if( Input.GetKey( KeyCode.W ) || Input.GetKey( KeyCode.UpArrow ) ) moveDirectionLocal += Vector2.up;
                else if( Input.GetKey( KeyCode.S ) || Input.GetKey( KeyCode.DownArrow ) ) moveDirectionLocal += Vector2.down;

                if( Input.GetKeyDown( PunchKey ) ) { StartCharge( PunchKey ); }
                else if( Input.GetKeyDown( PunchUppercutKey ) ) { StartCharge( PunchUppercutKey ); }
                else if( Input.GetKeyDown( PushForceKey ) ) DoPushForce();
                else if( Input.GetKeyDown( GridForceKey ) ) DoGripForce();
                else if( Input.GetKeyDown( CatchKey ) ) DoCatch();
                else if( Input.GetKeyDown( KeyCode.Space ) ) DoJump();
            }
            else
            {
                if( chargeKey != KeyCode.None )
                {
                    if( Input.GetKeyUp( chargeKey ) )
                    {
                        float offset = Mathf.Clamp( chargeAmount, 0f, 0.125f );

                        if( chargeKey == PunchKey ) DoPunchF( offset );
                        else if( chargeKey == PunchUppercutKey ) DoPunchU( offset );
                        chargeKey = KeyCode.None;
                    }
                    else
                    {
                        rotated += Time.deltaTime * ( 110f + Mathf.Clamp( chargeAmount * 75f, 0f, 100f ) ) * 10f;
                        chargeAmount += Time.deltaTime;
                        chargedScale = 1f + ( Mathf.Clamp( chargeAmount * 0.5f, 0f, 0.8f ) );
                    }
                }
            }

            if( chargeKey == KeyCode.None )
            {
                chargedScale = Mathf.Lerp( chargedScale, 1f, Time.deltaTime * 4f );
            }

            if( Hand ) Hand.localScale = new Vector3( chargedScale, chargedScale, chargedScale );
            //if( UpperArm ) if( chargeKey != KeyCode.None ) UpperArm.rotation = Quaternion.AngleAxis( rotated * Mathf.Clamp01( chargeAmount * 2f ), Mover.transform.forward ) * ( UpperArm.parent.rotation * UpperArm.localRotation );

            UpdateHoldingUp();
            Mover.moveDirectionLocal = moveDirectionLocal;
        }

        private void StartCharge( KeyCode key )
        {
            Action = true;
            chargeKey = key;
            chargeAmount = -.2f;
            rotated = 0;
            PlayClip( "Punch Charge" );
        }

        private KeyCode chargeKey = KeyCode.None;
        private float chargedScale = 1f;
        private float chargeAmount = -1f;
        private float rotated = 0f;

        // Hero Actions -------------------------

        public void DoPunchF( float timeOffset = 0f )
        {
            PlayClip( "Punch F", timeOffset );
        }

        public void DoPunchU( float timeOffset = 0f )
        {
            PlayClip( "Punch U", timeOffset );
        }

        public void DoPushForce( float timeOffset = 0f )
        {
            PlayClip( "Force Push", timeOffset );
        }

        private RagdollHandler gripped = null;
        private Vector3 magnetPosLocal = Vector3.zero;

        public void DoGripForce()
        {
            if( GripMagnet.enabled )
            {
                GripMagnet.enabled = false;
                updateUpperBodyLayer = false;

                var head = gripped.User_GetBoneSetupByHumanoidBone( HumanBodyBones.Head );
                head.GameRigidbody.isKinematic = false;
                gripped.User_OverrideMusclesPower = null;

                gripped.Mecanim.CrossFadeInFixedTime( "Fall Pose", 0.2f );

                gripped.RigidbodyDragValue = 0f;
                gripped.User_UpdateAllBonesParametersAfterManualChanges();

                var fallpose = gripped.GetExtraFeature<RAF_FallingBlendTreePoser>();
                if( fallpose ) fallpose.Helper.Enabled = true;

                RagdollHandlerUtilities.User_AddBoneImpact( gripped, head, transform.forward * GripEndPushPower, 0.15f, ForceMode.Force, 0f, 1 );
                RagdollHandlerUtilities.User_AddBoneImpact( gripped, gripped.GetAnchorBoneController, transform.forward * GripEndPushPower * 0.75f, 0.1f, ForceMode.Force, 0f, 1 );
                gripped = null;

                return;
            }

            CastSurroundSphere();

            var ragdolls = FindRagdollsIn( surround, surroundCount );

            float angleDiff = float.MaxValue;
            RagdollHandler bestR = null;

            foreach( var ragdoll in ragdolls )
            {
                Vector3 diff = ragdoll.GetBaseTransform().position - transform.position;

                if( diff.magnitude > 10f ) continue;

                float angle = Vector3.Angle( diff.normalized, transform.forward );

                if( angle > 30f ) continue;

                if( angle < angleDiff )
                {
                    angleDiff = angle;
                    bestR = ragdoll;
                }
            }

            if( bestR != null )
            {
                gripped = bestR;

                gripped.User_SwitchFallState();
                gripped.Mecanim.CrossFadeInFixedTime( "Gripped", 0.2f );
                gripped.User_OverrideMusclesPower = 0.9f;

                var fallpose = gripped.GetExtraFeature<RAF_FallingBlendTreePoser>();
                if( fallpose ) fallpose.Helper.Enabled = false; // Avoid additive blend conflicting

                Mecanim.CrossFadeInFixedTime( "Force Grip", 0.1f, 1 );

                gripped.RigidbodyDragValue = 3f;

                GripMagnet.ToMove = gripped.User_GetBoneSetupByHumanoidBone( HumanBodyBones.Head ).GameRigidbody.transform;

                magnetPosLocal = transform.InverseTransformPoint( GripMagnet.ToMove.position + Vector3.up * 1f );
                GripMagnet.transform.position = GripMagnet.ToMove.position;
                GripMagnet.enabled = true;
                GripMagnet.DragPower = 0.0f;
                GripMagnet.RotatePower = 0f;

                updateUpperBodyLayer = true;

                gripped.User_UpdateAllBonesParametersAfterManualChanges();
            }

            PlayClip( "Force Grip" );
        }

        public bool IsHoldingUp => isHoldingUp != null;
        public bool IsThrowing { get; private set; }

        /// <summary>投掷方向覆盖。动画事件 EThrow 会用它,用完清空。</summary>
        public Vector3 PendingThrowDirection;

        public void DoRelease()
        {
            updateUpperBodyLayer = false;
            IsThrowing = false;
            PendingThrowDirection = Vector3.zero;
            if( Mecanim && Mecanim.layerCount > 1 )
                Mecanim.SetLayerWeight( 1, 0f );

            if( isHoldingUp != null )
            {
                EThrow();
                return;
            }

            if( CatchMagnet ) CatchMagnet.enabled = false;
        }

        public void SetHoldingPose( bool on )
        {
            updateUpperBodyLayer = on;
            if( Mecanim == null || Mecanim.layerCount < 2 ) return;
            if( on ) Mecanim.CrossFadeInFixedTime( "Holding", 0.1f, 1 );
            else Mecanim.SetLayerWeight( 1, 0f );
        }

        public void PlayThrowAnimation()
        {
            updateUpperBodyLayer = false;
            IsThrowing = true;
            PlayClip( "Holding Throw" );
        }

        public void ClearThrowing()
        {
            IsThrowing = false;
        }

        /// <summary>Punch Demo:抓住后再操作一次,先播 Holding Throw,等动画事件 EThrow 甩出。</summary>
        public void DoThrow()
        {
            if( IsThrowing ) return;
            if( isHoldingUp == null )
            {
                updateUpperBodyLayer = false;
                if( CatchMagnet ) CatchMagnet.enabled = false;
                return;
            }

            PlayThrowAnimation();
        }

        public void DoCatch()
        {
            DoCatch( 0.05f, 0.25f, 1f );
        }

        public void DoCatch( float width, float height, float zScale )
        {
            if( isHoldingUp == null )
            {
                CastCloseBox( 1f, width, height, zScale );
                if( Mecanim && Mecanim.layerCount > 1 )
                    Mecanim.CrossFadeInFixedTime( "Holding", 0.1f, 1 );

                var ragdoll = FindRagdollIn( close, closeCount );
                isHoldingUp = ragdoll;

                if( isHoldingUp != null )
                {
                    isHoldingUp.User_SwitchFallState();
                    if( isHoldingUp.Mecanim )
                        isHoldingUp.Mecanim.CrossFadeInFixedTime( "Gripped", 0.15f );
                    isHoldingUp.User_OverrideMusclesPower = 0.9f;

                    var head = isHoldingUp.User_GetBoneSetupByHumanoidBone( HumanBodyBones.Head );
                    if( head == null || head.GameRigidbody == null )
                    {
                        isHoldingUp = null;
                    }
                    else if( CatchMagnet )
                    {
                        CatchMagnet.DragPower = 1f;
                        CatchMagnet.ToMove = head.GameRigidbody.transform;
                        CatchMagnet.enabled = true;
                    }
                }

                updateUpperBodyLayer = isHoldingUp != null;
            }
            else // Click during Holding
            {
                DoThrow();
            }
        }

        public void DoJump()
        {
            if( Mover.isGrounded == false ) return;
            Mover.jumpRequest = Mover.JumpPower;
        }

        // Holding Up

        private RagdollHandler isHoldingUp = null;
        private bool updateUpperBodyLayer = false;

        private float _sd_layer = 0f;

        private void UpdateHoldingUp()
        {
            if( CatchMagnet && CatchMagnet.enabled )
            {
                CatchMagnet.DragPower = Mathf.Min( 3f, CatchMagnet.DragPower + Time.deltaTime * 6f );
                CatchMagnet.RotatePower = CatchMagnet.DragPower;
            }

            if( GripMagnet )
                if( GripMagnet.enabled && gripped != null )
                {
                    GripMagnet.DragPower = Mathf.MoveTowards( GripMagnet.DragPower, 0.5f, Time.deltaTime * 1f );
                    GripMagnet.RotatePower = GripMagnet.DragPower * 5f;
                    gripped.OverrideSpringsValueOnFall = 4000f;

                    if( Camera.main == null ) return;
                    Transform cam = Camera.main.transform;
                    Vector3 targetPosition = transform.TransformPoint( magnetPosLocal );
                    targetPosition = cam.InverseTransformPoint( targetPosition );
                    targetPosition.x = 0f;
                    targetPosition.z = magnetPosLocal.z + 1f;

                    if( cam.TransformPoint( new Vector3( targetPosition.x, 1f, targetPosition.z ) ).y > transform.TransformPoint( magnetPosLocal ).y )
                    {
                        targetPosition.y = 1f;
                    }

                    GripMagnet.transform.position = Vector3.MoveTowards( GripMagnet.transform.position, cam.TransformPoint( targetPosition ), Time.deltaTime * 30f );
                    GripMagnet.transform.rotation = Quaternion.LookRotation( cam.position - GripMagnet.transform.position );
                }

            float targetWeight = -0.001f;
            float dur = 0.045f;
            if( updateUpperBodyLayer )
            {
                targetWeight = 1.001f;
                dur = 0.07f;
                if( GripMagnet ) if( GripMagnet.enabled ) dur = 0.4f;
            }

            if( Mecanim && Mecanim.layerCount > 1 )
            {
                float newWeight = Mecanim.GetLayerWeight( 1 );
                newWeight = Mathf.SmoothDamp( newWeight, targetWeight, ref _sd_layer, dur, 100000f, Time.deltaTime );
                Mecanim.SetLayerWeight( 1, newWeight );
            }
        }

        // Utilities ---------------------

        public void PlayClip( string state, float timeOffset = 0f )
        {
            if( Mecanim == null ) return;
            Mecanim.CrossFadeInFixedTime( state, 0.145f, 0, timeOffset );
        }

        // Animation Events -------------------

        public void EThrow()
        {
            Vector3 dir = PendingThrowDirection.sqrMagnitude > 0.0001f ? PendingThrowDirection : transform.forward;
            PendingThrowDirection = Vector3.zero;
            ApplyThrow( dir );
        }

        public void EThrow( Vector3 worldDir )
        {
            PendingThrowDirection = Vector3.zero;
            ApplyThrow( worldDir );
        }

        void ApplyThrow( Vector3 worldDir )
        {
            IsThrowing = false;
            if( CatchMagnet ) CatchMagnet.enabled = false;
            if( isHoldingUp == null ) return;

            if( worldDir.sqrMagnitude < 0.0001f ) worldDir = transform.forward;
            worldDir.Normalize();

            var head = isHoldingUp.User_GetBoneSetupByHumanoidBone( HumanBodyBones.Head );
            if( head == null || head.GameRigidbody == null )
            {
                isHoldingUp = null;
                return;
            }
            head.GameRigidbody.isKinematic = false;

            isHoldingUp.User_OverrideMusclesPower = null;

            if( isHoldingUp.Mecanim )
                isHoldingUp.Mecanim.CrossFadeInFixedTime( "Fall Pose", 0.2f );

            RagdollHandlerUtilities.User_AddBoneImpact( isHoldingUp, head, worldDir * ThrowPower, 0.15f, ForceMode.Force, 0f, 1 );
            RagdollHandlerUtilities.User_AddBoneImpact( isHoldingUp, isHoldingUp.GetAnchorBoneController, worldDir * ThrowPower * 0.75f, 0.1f, ForceMode.Force, 0f, 1 );

            isHoldingUp = null;
        }

        float lastPunchHitTime = -1f;

        /// <summary>出拳命中帧回调,联机玩家受击走这条,和打 NPC 同一时机。</summary>
        public System.Action<Vector3> OnMeleeHit;

        public void EPunchForward()
        {
            if( Time.time - lastPunchHitTime < 0.12f ) return;
            lastPunchHitTime = Time.time;

            CastCloseBox( 1f, 0.55f, 0.7f, 1.6f );
            RagdollHandler rag = FindRagdollIn( close, closeCount );
            Vector3 impactDirection = transform.forward + new Vector3( 0f, 0.33f, 0f );

            if( rag != null && !IsNetworkedPlayerRagdoll( rag ) )
            {
                if( HitAudio ) HitAudio.Play();

                var rigidbody = rag.User_GetNearestRagdollRigidbodyToPosition( transform.TransformPoint( new Vector3( 0f, 1.45f, 0.2f ) ), true, ERagdollChainType.Core );
                if( rigidbody != null )
                {
                    rag.User_SwitchFallState();

                    float chargeMul = 1f + chargeAmount * 0.4f;
                    rag.User_AddAllBonesImpact( impactDirection * ( PunchPower * 0.5f * chargeMul ), 0.05f, ForceMode.Impulse );
                    rag.User_AddRigidbodyImpact( rigidbody, impactDirection * ( PunchPower * 1.5f * chargeMul ), 0.0f, ForceMode.Impulse );
                }
            }

            OnMeleeHit?.Invoke( impactDirection );
            HitMeleeVictims( impactDirection );
        }

        public void EPunchUp()
        {
            if( Time.time - lastPunchHitTime < 0.12f ) return;
            lastPunchHitTime = Time.time;

            CastCloseBox( 1f, 0.45f, 0.7f, 1.4f );
            RagdollHandler rag = FindRagdollIn( close, closeCount );
            Vector3 impactDirection = Vector3.up;

            if( rag != null && !IsNetworkedPlayerRagdoll( rag ) )
            {
                if( HitAudio ) HitAudio.Play();

                var rigidbody = rag.User_GetNearestRagdollRigidbodyToPosition( transform.TransformPoint( new Vector3( 0f, 1.45f, 0.2f ) ), true, ERagdollChainType.Core );
                if( rigidbody != null )
                {
                    rag.User_SwitchFallState();

                    float chargeMul = 1f + chargeAmount * 0.3f;
                    rag.User_AddAllBonesImpact( impactDirection * ( UppercutPower * 0.55f * chargeMul ), 0f, ForceMode.VelocityChange );
                    rag.User_AddRigidbodyImpact( rigidbody, impactDirection * ( UppercutPower * 2.1f * chargeMul ), 0f, ForceMode.Impulse, 0.05f );
                }
            }

            OnMeleeHit?.Invoke( impactDirection );
            HitMeleeVictims( impactDirection );
        }

        void HitMeleeVictims( Vector3 impactDirection )
        {
            var hit = new HashSet<FAnnequinMeleeVictim>();

            // 和打 Characters 同一记出拳盒:玩家胶囊体在层 0,本来就会被扫到,只是没有布娃娃所以之前被丢掉了
            for( int i = 0; i < closeCount; i++ )
            {
                Collider col = close[i];
                if( col == null || toIgnore.Contains( col ) ) continue;
                FAnnequinMeleeVictim victim = col.GetComponentInParent<FAnnequinMeleeVictim>();
                if( victim == null || victim.BelongsTo( transform ) ) continue;
                hit.Add( victim );
            }

            if( hit.Count == 0 )
            {
                FAnnequinMeleeVictim[] victims = FindObjectsOfType<FAnnequinMeleeVictim>();
                Vector3 origin = transform.position;
                Vector3 face = transform.forward;
                face.y = 0f;
                if( face.sqrMagnitude < 0.0001f ) face = Vector3.forward;
                face.Normalize();

                for( int i = 0; i < victims.Length; i++ )
                {
                    FAnnequinMeleeVictim victim = victims[i];
                    if( victim == null || victim.BelongsTo( transform ) ) continue;

                    Vector3 to = victim.transform.position - origin;
                    to.y = 0f;
                    float dist = to.magnitude;
                    if( dist > 2.8f ) continue;
                    if( dist > 1.6f && Vector3.Dot( face, to / Mathf.Max( dist, 0.001f ) ) < 0.15f ) continue;
                    hit.Add( victim );
                }
            }

            foreach( FAnnequinMeleeVictim victim in hit )
                victim.OnHitByMelee?.Invoke( impactDirection );
        }

        private Collider[] surround = new Collider[64];
        private int surroundCount = 0;
        private Collider[] far = new Collider[32];
        private int farCount = 0;
        private Collider[] mid = new Collider[32];
        private int midCount = 0;
        private Collider[] close = new Collider[16];
        private int closeCount = 0;

        private List<Collider> used = new List<Collider>();

        public void EPushForce()
        {
            CastFarSphere( 3f, 1.5f );
            CastMidBox( 1f, 1.4f, 1f, 4f );

            if( PushParticle )
            {
                GameObject fx = Instantiate( PushParticle );
                fx.transform.position = transform.position + Vector3.up + transform.forward;
                fx.transform.rotation = transform.rotation;
            }

            used.Clear();

            StartCoroutine( _IE_CallAfter( 0.06f, () =>
            {
                for( int i = 0; i < farCount; i++ ) AddForce( far[i] );
                for( int i = 0; i < midCount; i++ ) if( !used.Contains( mid[i] ) ) AddForce( mid[i] );
            } ) );

            var ragdolls = FindRagdollsIn( far, farCount );
            //ragdolls = FindRagdollsIn( mid, midCount, false );

            for( int r = 0; r < ragdolls.Count; r++ )
            {
                var ragdoll = ragdolls[r];

                ragdoll.User_SwitchFallState( RagdollHandler.EAnimatingMode.Falling );

                Rigidbody nearest = ragdoll.User_GetNearestRagdollRigidbodyToPosition( transform.TransformPoint( Vector3.up * 1.5f ), true, ERagdollChainType.Core );

                if( nearest == null ) continue;

                Vector3 dir = ( ragdoll.User_GetPosition_Center() - Mover.transform.position ).normalized;

                ragdoll.User_AddRigidbodyImpact( nearest, ( dir + new Vector3( 0f, .4f, 0f ) ) * ( PushForcePower * .5f ), 0.14f, ForceMode.Impulse, 0.06f );

                //ragdoll.Mecanim.CrossFadeInFixedTime( "Fall", 0.05f );
                //ragdoll.Mecanim.SetBool( actionHash, true );
            }
        }

        private IEnumerator _IE_CallAfter( float delay, System.Action act )
        {
            if( act == null ) yield break;
            if( delay > 0 ) yield return new WaitForSeconds( delay );
            act.Invoke();
            yield break;
        }

        private void CastSurroundSphere( float forwardDistance = 6f, float radius = 8f )
        {
            Vector3 maxRange = transform.TransformPoint( new Vector3( 0f, 1f, forwardDistance ) );

            surroundCount = Mathf.Min( surround.Length - 1, Physics.OverlapSphereNonAlloc( maxRange, radius, surround, HittableLayermask ) );
        }

        private void CastFarSphere( float distance = 3f, float radius = 1f )
        {
            Vector3 maxRange = transform.TransformPoint( new Vector3( 0f, 1f, distance ) );

            farCount = Mathf.Min( far.Length - 1, Physics.OverlapSphereNonAlloc( maxRange, radius, far, HittableLayermask ) );
        }

        private void CastMidBox( float y = 1f, float width = 1.5f, float height = 1f, float zScale = 2f )
        {
            Vector3 midRange = transform.TransformPoint( new Vector3( 0f, y, 0.5f + zScale * 0.5f ) );
            midCount = Mathf.Min( mid.Length - 1, Physics.OverlapBoxNonAlloc( midRange, new Vector3( width, height, zScale ), mid, transform.rotation, HittableLayermask ) );
        }

        private void CastCloseBox( float y = 1f, float width = 0.05f, float height = 0.25f, float zScale = 1f )
        {
            Vector3 closeRange = transform.TransformPoint( new Vector3( 0f, y, 0.5f * zScale ) );
            closeCount = Mathf.Min( close.Length - 1, Physics.OverlapBoxNonAlloc( closeRange, new Vector3( width, height, zScale ), close, transform.rotation, HittableLayermask ) );
        }

        private void AddForce( Collider c )
        {
            if( toIgnore.Contains( c ) ) return;
            if( c == null ) return;

            used.Add( c );
            Rigidbody rig = c.attachedRigidbody;
            if( rig == null ) return;

            Vector3 force = transform.forward;
            force = Vector3.Lerp( force, ( c.bounds.center - transform.TransformPoint( Vector3.up ) + new Vector3( 0f, Random.Range( 0f, 0.5f ) ) ).normalized, Random.Range( 0.6f, 1f ) ).normalized;

            rig.AddForce( force * ( PushForcePower * Random.Range( 0.6f, 0.8f ) ), ForceMode.Impulse );
            rig.AddTorque( force * ( PushForcePower * 0.5f * Random.Range( 0.9f, 1.1f ) ), ForceMode.Impulse );
        }

        private RagdollHandler FindRagdollIn( Collider[] c, int length )
        {
            for( int i = 0; i < length; i++ )
            {
                RagdollHandler handler = GetRagdollFromCollider( c[i] );
                if( handler != null ) return handler;
            }

            return null;
        }

        RagdollHandler GetRagdollFromCollider( Collider hit )
        {
            if( hit == null ) return null;
            if( toIgnore.Contains( hit ) ) return null;

            RagdollHandler handler = null;
            RagdollAnimator2BoneIndicator ind = hit.GetComponent<RagdollAnimator2BoneIndicator>();
            if( ind != null ) handler = ind.ParentHandler;

            if( handler == null )
            {
                RagdollAnimator2 ra = hit.GetComponentInParent<RagdollAnimator2>();
                if( ra != null ) handler = ra.Settings;
            }

            if( handler == null ) return null;
            if( Ragdoll != null && handler == Ragdoll.Settings ) return null;
            return handler;
        }

        static bool IsNetworkedPlayerRagdoll( RagdollHandler rag )
        {
            Transform root = rag != null ? rag.GetBaseTransform() : null;
            return root != null && root.GetComponentInParent<FAnnequinMeleeVictim>() != null;
        }

        private List<RagdollHandler> detectedRagdolls = new List<RagdollHandler>();

        private List<RagdollHandler> FindRagdollsIn( Collider[] c, int length, bool clear = true )
        {
            if( clear ) detectedRagdolls.Clear();

            for( int i = 0; i < length; i++ )
            {
                if( c[i] == null ) continue;

                if( toIgnore.Contains( c[i] ) ) continue;

                RagdollHandler handler = GetRagdollFromCollider( c[i] );
                if( handler != null && detectedRagdolls.Contains( handler ) == false )
                    detectedRagdolls.Add( handler );
            }

            return detectedRagdolls;
        }
    }
}