using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 把 Mirror NetworkManagerHUD 从左上角挪到左下角，避免挡住顶栏计分。
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(NetworkManagerHUD))]
    public sealed class BrawlNetworkHudAnchor : MonoBehaviour
    {
        [Tooltip("相对屏幕左下的边距")]
        public Vector2 ScreenMargin = new Vector2(10f, 12f);

        [Tooltip("未连接时 Host/Client 按钮区域高度")]
        public int DisconnectedHeight = 200;

        [Tooltip("已连接时状态区域高度")]
        public int ConnectedHeight = 96;

        NetworkManagerHUD hud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureOnHud()
        {
            NetworkManagerHUD existing = FindObjectOfType<NetworkManagerHUD>();
            if (existing != null && existing.GetComponent<BrawlNetworkHudAnchor>() == null)
                existing.gameObject.AddComponent<BrawlNetworkHudAnchor>();
        }

        void Awake()
        {
            hud = GetComponent<NetworkManagerHUD>();
        }

        void LateUpdate()
        {
            if (hud == null) return;

            int height = NetworkClient.isConnected || NetworkServer.active
                ? ConnectedHeight
                : DisconnectedHeight;

            // NetworkManagerHUD 使用 Rect(10 + offsetX, 40 + offsetY, ...)
            hud.offsetX = Mathf.RoundToInt(ScreenMargin.x);
            hud.offsetY = Mathf.RoundToInt(Screen.height - 40 - height - ScreenMargin.y);
        }
    }
}
