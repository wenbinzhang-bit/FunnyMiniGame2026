using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 玩家攻击参数。血量/死亡玩法已移除；保留这个组件是为了兼容现有 Prefab 和接口。
    /// </summary>
    public class PlayerAttributes : NetworkBehaviour
    {
        [Header("攻击")]
        [Tooltip("保留的攻击强度配置；当前玩法只用于击倒冲量，不再扣血")]
        public int AttackDamage = 10;

        // 兼容旧调用。玩家永远处于可行动状态，不再同步生命值。
        public int MaxHealth => 1;
        public int CurrentHealth => 1;
        public bool IsDead => false;
        public float HealthNormalized => 1f;

        [Server]
        public void ServerResetHealth() { }

        [Server]
        public void ServerSetHealth(int value) { }

        [Server]
        public bool ServerTakeDamage(int amount) => false;

        [Server]
        public void ServerHeal(int amount) { }
    }
}
