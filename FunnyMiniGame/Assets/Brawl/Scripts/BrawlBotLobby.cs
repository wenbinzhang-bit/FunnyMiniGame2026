using System;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 开房前指定 Bot 数量。0 不创建，1-3 在 Host 成功后自动加入。
    /// 用加减按钮，避免输入框抢走 Client 地址栏的键盘。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class BrawlBotLobby : MonoBehaviour
    {
        int botCount = 1;

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
                botCount = Mathf.Clamp(parsed, 0, BrawlBotBrain.MaxBots);
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
            float x = 250f;
            float y = Screen.height - height - 16f;

            GUI.Box(new Rect(x, y, width, height), "");
            var title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            GUI.Label(new Rect(x + 10f, y + 6f, width - 20f, 22f), "开房前指定 Bot", title);

            if (!inRoom)
            {
                if (GUI.Button(new Rect(x + 10f, y + 34f, 28f, 24f), "-"))
                    botCount = Mathf.Max(0, botCount - 1);
                GUI.Label(new Rect(x + 46f, y + 36f, 90f, 22f), $"数量 {botCount}");
                if (GUI.Button(new Rect(x + 140f, y + 34f, 28f, 24f), "+"))
                    botCount = Mathf.Min(BrawlBotBrain.MaxBots, botCount + 1);
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
            manager.PendingBotCount = botCount;
        }
    }
}
