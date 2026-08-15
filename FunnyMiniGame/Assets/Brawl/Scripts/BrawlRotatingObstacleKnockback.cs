using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 服务端权威的旋转长杆命中判定。
    /// 物理接触只负责确定目标和旋转方向，实际击飞复用玩家的拳击击倒流程。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class BrawlRotatingObstacleKnockback : MonoBehaviour
    {
        [SerializeField] Rigidbody obstacleBody;
        [SerializeField, Min(0f)] float minimumPointSpeed = 1f;
        [SerializeField, Min(0.1f)] float punchEquivalentSpeed = 8.5f;
        [SerializeField, Min(0.1f)] float perPlayerCooldown = 0.8f;
        [SerializeField] bool debugHits = true;

        readonly Dictionary<int, float> lastHitTimes = new Dictionary<int, float>();
        Collider hitbox;

        void Awake()
        {
            hitbox = GetComponent<Collider>();
            if (obstacleBody == null && hitbox != null)
                obstacleBody = hitbox.attachedRigidbody;
            if (obstacleBody == null)
                obstacleBody = GetComponentInParent<Rigidbody>();
        }

        void OnTriggerEnter(Collider other)
        {
            TryHitPlayer(other, true);
        }

        void OnTriggerStay(Collider other)
        {
            // 连续检测可避免高速旋转时只收到短暂触发；玩家冷却会阻止一次扫过重复命中。
            TryHitPlayer(other, false);
        }

        void OnDisable()
        {
            lastHitTimes.Clear();
        }

        void TryHitPlayer(Collider other, bool logRejectedHit)
        {
            if (!NetworkServer.active || other == null)
                return;

            NetFAnnequinController player = other.GetComponentInParent<NetFAnnequinController>();
            if (player == null && other.attachedRigidbody != null)
                player = other.attachedRigidbody.GetComponentInParent<NetFAnnequinController>();
            if (player == null || !player.isServer || player.IsDead || player.IsGrabbed || player.IsKnockedDown)
                return;

            int playerKey = player.GetInstanceID();
            float now = Time.time;
            if (lastHitTimes.TryGetValue(playerKey, out float lastHitTime)
                && now - lastHitTime < perPlayerCooldown)
                return;

            Vector3 contactPoint = hitbox != null
                ? hitbox.ClosestPoint(player.transform.position)
                : player.transform.position;
            Vector3 pointVelocity = obstacleBody != null
                ? obstacleBody.GetPointVelocity(contactPoint)
                : Vector3.zero;
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(pointVelocity, Vector3.up);

            // 只把正在扫动的长杆视为攻击，避免角色碰到静止长杆也被击飞。
            if (horizontalVelocity.magnitude < minimumPointSpeed)
            {
                if (debugHits && logRejectedHit)
                    Debug.Log($"BRAWL_OBSTACLE: {name} detected {player.name}, but point speed {horizontalVelocity.magnitude:0.00} was below {minimumPointSpeed:0.00}.");
                return;
            }

            lastHitTimes[playerKey] = now;
            Vector3 punchDirection = (horizontalVelocity.normalized + Vector3.up * 0.45f).normalized;
            if (debugHits)
                Debug.Log($"BRAWL_OBSTACLE: {name} hit {player.name} at {horizontalVelocity.magnitude:0.00} m/s.");
            player.ServerKnockDown(punchDirection * punchEquivalentSpeed, 0);
        }
    }
}
