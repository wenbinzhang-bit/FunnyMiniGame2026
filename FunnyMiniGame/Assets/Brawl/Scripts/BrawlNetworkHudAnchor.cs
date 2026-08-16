using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 关掉 Mirror 自带的 IMGUI 联机条，改由 BrawlNetworkHud 绘制。
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(NetworkManagerHUD))]
    public sealed class BrawlNetworkHudAnchor : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureOnHud()
        {
            NetworkManagerHUD existing = FindObjectOfType<NetworkManagerHUD>();
            if (existing != null)
            {
                existing.enabled = false;
                if (existing.GetComponent<BrawlNetworkHudAnchor>() == null)
                    existing.gameObject.AddComponent<BrawlNetworkHudAnchor>();
            }
        }

        void Awake()
        {
            NetworkManagerHUD hud = GetComponent<NetworkManagerHUD>();
            if (hud != null)
                hud.enabled = false;
        }
    }
}
