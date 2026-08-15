using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// Marks the laptop as the round objective and stores the shared tuning values.
    /// The authoritative network game manager should own holder detection and scores.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class KpiComputerObjective : MonoBehaviour
    {
        [Tooltip("达到这个工作量 KPI 的玩家获胜")]
        [Min(1f)] public float WinningKpi = 99f;

        [Tooltip("持有电脑每秒增加的 KPI")]
        [Min(0.01f)] public float KpiPerHeldSecond = 1f;

        public Rigidbody Body;

        void Reset()
        {
            Body = GetComponent<Rigidbody>();
        }

        void OnValidate()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
        }
    }
}
