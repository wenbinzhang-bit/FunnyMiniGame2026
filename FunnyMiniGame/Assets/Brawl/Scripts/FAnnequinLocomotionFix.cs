using FIMSpace.RagdollAnimatorDemo;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 出拳动画带根运动时,FBasic_RigidbodyMover 会跳过改速度,松键后角色会一直往前滑。
    /// 在 Mover 之后把无输入时的水平速度清掉。
    /// </summary>
    [DefaultExecutionOrder(250)]
    public class FAnnequinLocomotionFix : MonoBehaviour
    {
        NetFAnnequinController owner;
        FBasic_RigidbodyMover mover;
        Animator mecanim;

        void Awake()
        {
            owner = GetComponent<NetFAnnequinController>();
            mover = GetComponent<FBasic_RigidbodyMover>();
            // 换皮角色的有效 Animator 在 Man/Girl 子节点上；根 Animator 只是关闭的旧人偶壳。
            mecanim = owner != null && owner.Mecanim != null
                ? owner.Mecanim
                : (mover != null && mover.Mecanim != null ? mover.Mecanim : GetComponent<Animator>());
        }

        void Start()
        {
            if (mecanim) mecanim.applyRootMotion = false;
            if (mover) mover.DisableRootMotion = true;
        }

        void FixedUpdate()
        {
            if (owner == null || !owner.IsMoveAuthority) return;
            if (owner.IsGrabbed) return;
            if (mover == null || mover.Rigb == null || mover.Rigb.isKinematic) return;

            if (owner.IsInKnockback)
            {
                Vector3 knockback = owner.KnockbackVelocity;
                Vector3 velocity = mover.Rigb.velocity;
                velocity.x = knockback.x;
                velocity.z = knockback.z;
                mover.Rigb.velocity = velocity;
                owner.TickKnockback(Time.fixedDeltaTime);
                return;
            }

            if (owner.IsKnockedDown) return;

            SeparateFromOtherPlayers();

            if (owner.WantsToMove) return;

            Vector3 stop = mover.Rigb.velocity;
            stop.x = 0f;
            stop.z = 0f;
            mover.Rigb.velocity = stop;

            if (mecanim && mecanim.isActiveAndEnabled && mecanim.runtimeAnimatorController != null)
            {
                mecanim.SetBool("Moving", false);
                mecanim.SetFloat("Speed", 0f);
            }
        }

        void SeparateFromOtherPlayers()
        {
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            float radius = capsule != null ? capsule.radius : 0.38f;
            float minDistance = radius * 2f + 0.06f;

            NetFAnnequinController[] others = FindObjectsOfType<NetFAnnequinController>();
            foreach (NetFAnnequinController other in others)
            {
                if (other == null || other == owner || other.IsGrabbed) continue;

                Vector3 delta = mover.Rigb.position - other.transform.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance >= minDistance) continue;
                if (distance < 0.001f) delta = transform.right;

                Vector3 push = delta.normalized * (minDistance - distance) * 0.25f;
                mover.Rigb.MovePosition(mover.Rigb.position + push);
            }
        }
    }
}
