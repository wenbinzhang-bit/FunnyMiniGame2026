using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 旧组件兼容壳。血量 UI 已从当前玩法移除。
    /// </summary>
    public class PlayerHealthHud : MonoBehaviour
    {
        void Awake() => enabled = false;
    }
}
