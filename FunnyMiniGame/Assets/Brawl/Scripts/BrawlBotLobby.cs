using System;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 开房前指定 Bot 数量。0 不创建，1-3 在 Host 成功后自动加入。
    /// 进房后不再提供创建按钮，避免鼠标锁定后点不到。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class BrawlBotLobby : MonoBehaviour
    {
        string countText = "1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindObjectOfType<BrawlBotLobby>() != null) return;
            var go = new GameObject("BrawlBotLobby");
            DontDestroyOnLoad(go);
            go.AddComponent<BrawlBotLobby>();
        }

        void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            int idx = Array.IndexOf(args, "-brawlSpawnBots");
            if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int parsed))
                countText = Mathf.Clamp(parsed, 0, BrawlBotBrain.MaxBots).ToString();
            ApplyPendingCount();
        }

        void Update()
        {
            if (!NetworkServer.active && !NetworkClient.active)
                ApplyPendingCount();
        }

        void OnGUI()
        {
            bool inRoom = NetworkServer.active || NetworkClient.isConnected;
            const float width = 228f;
            const float height = 72f;
            float x = 12f;
            float y = Screen.height - 40f - 200f - 12f - height - 8f;

            GUI.Box(new Rect(x, y, width, height), "");
            var title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            GUI.Label(new Rect(x + 10f, y + 6f, width - 20f, 22f), "开房前指定 Bot", title);

            if (!inRoom)
            {
                GUI.Label(new Rect(x + 10f, y + 34f, 70f, 22f), "数量 0-3");
                GUI.SetNextControlName("BrawlBotCount");
                countText = GUI.TextField(new Rect(x + 90f, y + 34f, 40f, 22f), countText, 1);
                ApplyPendingCount();
            }
            else
            {
                GUI.Label(new Rect(x + 10f, y + 34f, width - 20f, 22f),
                    $"已自动加入 {BrawlBotBrain.AliveCount} 个 Bot");
            }
        }

        void ApplyPendingCount()
        {
            var manager = NetworkManager.singleton as BrawlNetworkManager;
            if (manager == null) return;
            manager.PendingBotCount = ParseCount();
        }

        int ParseCount()
        {
            if (!int.TryParse(countText, out int count))
                return 0;
            return Mathf.Clamp(count, 0, BrawlBotBrain.MaxBots);
        }
    }
}
