using System;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 玩家属性。服务端改血量,SyncVar 同步到所有端。
    /// </summary>
    public class PlayerAttributes : NetworkBehaviour
    {
        [Header("生命")]
        [Tooltip("最大生命值")]
        public int MaxHealth = 100;

        [Header("攻击")]
        [Tooltip("出拳/投掷命中时造成的伤害")]
        public int AttackDamage = 10;

        [SyncVar(hook = nameof(OnHealthSynced))]
        int currentHealth;

        public int CurrentHealth => currentHealth;
        public bool IsDead => currentHealth <= 0;
        public float HealthNormalized => MaxHealth > 0 ? (float)currentHealth / MaxHealth : 0f;

        public event Action<int, int> HealthChanged;
        public event Action Died;

        public override void OnStartServer()
        {
            if (currentHealth <= 0)
                currentHealth = Mathf.Max(1, MaxHealth);
        }

        [Server]
        public void ServerResetHealth()
        {
            ServerSetHealth(MaxHealth);
        }

        [Server]
        public void ServerSetHealth(int value)
        {
            int clamped = Mathf.Clamp(value, 0, Mathf.Max(1, MaxHealth));
            bool wasAlive = currentHealth > 0;
            currentHealth = clamped;
            if (wasAlive && currentHealth <= 0)
                Died?.Invoke();
        }

        [Server]
        public bool ServerTakeDamage(int amount)
        {
            if (amount <= 0 || IsDead) return false;
            ServerSetHealth(currentHealth - amount);
            return IsDead;
        }

        [Server]
        public void ServerHeal(int amount)
        {
            if (amount <= 0 || IsDead) return;
            ServerSetHealth(currentHealth + amount);
        }

        void OnHealthSynced(int oldValue, int newValue)
        {
            HealthChanged?.Invoke(newValue, MaxHealth);
        }
    }
}
