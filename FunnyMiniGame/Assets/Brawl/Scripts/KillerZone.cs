using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 旧场景兼容组件。KillZone 玩法已移除，运行时自动隐藏并关闭触发器。
    /// </summary>
    public class KillerZone : MonoBehaviour
    {
        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            var rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;

            enabled = false;
        }
    }
}
