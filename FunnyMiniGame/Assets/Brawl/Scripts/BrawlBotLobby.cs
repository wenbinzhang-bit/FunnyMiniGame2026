using System;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 保存自动化测试用的开房 Bot 数量。正常大厅不再预生成 Bot，
    /// 房主改为在准备区选择数量并点击“添加”。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class BrawlBotLobby : MonoBehaviour
    {
        public static BrawlBotLobby Instance { get; private set; }

        int botCount;

        public int BotCount => botCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindObjectOfType<BrawlBotLobby>() != null) return;
            var go = new GameObject("BrawlBotLobby");
            if (BrawlSession.Instance != null)
                go.transform.SetParent(BrawlSession.Instance.transform, false);
            else
                DontDestroyOnLoad(go);
            go.AddComponent<BrawlBotLobby>();
        }

        void Awake()
        {
            Instance = this;
            string[] args = Environment.GetCommandLineArgs();
            int idx = Array.IndexOf(args, "-brawlSpawnBots");
            if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int parsed))
                botCount = Mathf.Clamp(parsed, 0, BrawlBotBrain.MaxBots);
            ApplyPendingCount();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (!NetworkServer.active && !NetworkClient.active)
                ApplyPendingCount();
        }

        public void Adjust(int delta)
        {
            botCount = Mathf.Clamp(botCount + delta, 0, BrawlBotBrain.MaxBots);
            ApplyPendingCount();
        }

        void ApplyPendingCount()
        {
            var manager = NetworkManager.singleton as BrawlNetworkManager;
            if (manager == null) return;
            manager.PendingBotCount = botCount;
        }
    }
}
