using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 场景伤害区:玩家留在区内按时间扣血,离开后停止。只在服务端结算。
    /// </summary>
    public class KillerZone : MonoBehaviour
    {
        [Tooltip("每秒扣除的生命")]
        public float DamagePerSecond = 20f;

        readonly Dictionary<PlayerAttributes, float> leftover = new Dictionary<PlayerAttributes, float>();
        readonly HashSet<PlayerAttributes> damagedThisStay = new HashSet<PlayerAttributes>();

        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                Shader shader = Shader.Find("Transparent/Diffuse");
                if (shader == null) shader = Shader.Find("Standard");
                var mat = new Material(shader);
                mat.color = new Color(1f, 0.12f, 0.08f, 0.35f);
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void FixedUpdate()
        {
            damagedThisStay.Clear();
        }

        void OnTriggerStay(Collider other)
        {
            if (!NetworkServer.active || other == null) return;

            var attr = other.GetComponentInParent<PlayerAttributes>();
            if (attr == null || attr.IsDead) return;
            if (!damagedThisStay.Add(attr)) return;

            leftover.TryGetValue(attr, out float acc);
            acc += Mathf.Max(0f, DamagePerSecond) * Time.fixedDeltaTime;
            int damage = Mathf.FloorToInt(acc);
            if (damage > 0)
            {
                acc -= damage;
                attr.ServerTakeDamage(damage);
            }

            leftover[attr] = acc;
        }

        void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            var attr = other.GetComponentInParent<PlayerAttributes>();
            if (attr != null) leftover.Remove(attr);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.85f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
