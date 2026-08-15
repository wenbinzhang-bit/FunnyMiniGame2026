using UnityEngine;

namespace FIMSpace.RagdollAnimatorDemo
{
    /// <summary>
    /// 联机玩家受击盒。出拳扫到胶囊体时回调,由玩家自己处理倒地,不要再转发出拳。
    /// </summary>
    public class FAnnequinMeleeVictim : MonoBehaviour
    {
        public System.Action<Vector3> OnHitByMelee;

        public bool BelongsTo(Transform body)
        {
            if (body == null) return false;
            return transform == body || transform.IsChildOf(body);
        }
    }
}
