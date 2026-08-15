using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// MiniGame_01 必须在 NetworkManager.Awake 之前把玩家预制体换成 FAnnequin。
    /// PC 包里没有 AssetDatabase,不能依赖编辑器路径去找 PlayerModels。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class MiniGameNetworkHook : MonoBehaviour
    {
        void Awake()
        {
            var manager = GetComponent<NetworkManager>();
            if (manager == null) manager = NetworkManager.singleton;
            if (manager == null) return;

            GameObject fannequin = Resources.Load<GameObject>("NetFAnnequin");
            if (fannequin == null)
            {
                Debug.LogError("MiniGame_01: 未找到 Resources/NetFAnnequin");
                return;
            }

            manager.playerPrefab = fannequin;

            var brawl = manager as BrawlNetworkManager;
            if (brawl != null && brawl.playerModels == null)
                brawl.playerModels = Resources.Load<PlayerModels>("PlayerModels");
        }
    }
}
