using System.Collections;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 局域网对抗游戏的网络管理器。
    /// 玩家模型只从 PlayerModels 列表按加入顺序取,不再回退到 NetClayman。
    /// </summary>
    public class BrawlNetworkManager : NetworkManager
    {
        static readonly string[] PlayerModelsAssetPaths =
        {
            "Assets/Brawl/Configs/PlayerModels.asset",
            "Assets/Brawl/PlayerModels.asset"
        };

        public PlayerModels playerModels;

        public int PendingBotCount { get; set; }

        int nextPlayerIndex;
        bool pendingBotsSpawned;

        public override void Awake()
        {
            EnsurePlayerModels();
            ApplyFirstPrefabAsPlayerPrefab();
            RegisterPlayerModelPrefabs();
            base.Awake();
        }

        public override void OnStartServer()
        {
            nextPlayerIndex = 0;
            pendingBotsSpawned = false;
            EnsurePlayerModels();
            ApplyFirstPrefabAsPlayerPrefab();
            RegisterPlayerModelPrefabs();
            base.OnStartServer();
        }

        void EnsurePlayerModels()
        {
            if (playerModels != null)
                return;

            playerModels = Resources.Load<PlayerModels>("PlayerModels");
            if (playerModels != null)
                return;

#if UNITY_EDITOR
            foreach (string path in PlayerModelsAssetPaths)
            {
                playerModels = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerModels>(path);
                if (playerModels != null)
                    return;
            }
#endif
        }

        void ApplyFirstPrefabAsPlayerPrefab()
        {
            GameObject first = playerModels != null ? playerModels.GetPrefab(0) : null;
            if (first == null)
                first = Resources.Load<GameObject>("NetFAnnequin");
            if (first != null)
                playerPrefab = first;
        }

        void RegisterPlayerModelPrefabs()
        {
            if (spawnPrefabs == null)
                spawnPrefabs = new System.Collections.Generic.List<GameObject>();

            if (playerModels == null || playerModels.prefabs == null)
                return;

            foreach (GameObject prefab in playerModels.prefabs)
            {
                if (prefab == null || prefab == playerPrefab)
                    continue;

                if (!prefab.TryGetComponent(out NetworkIdentity identity) || identity.assetId == 0)
                    continue;

                if (!spawnPrefabs.Contains(prefab))
                    spawnPrefabs.Add(prefab);
            }
        }

        GameObject ResolvePlayerPrefab(int playerIndex)
        {
            GameObject prefab = playerModels != null ? playerModels.GetPrefab(playerIndex) : null;
            if (CanSpawnForClients(prefab))
                return prefab;
            if (CanSpawnForClients(playerPrefab))
                return playerPrefab;

            GameObject resource = Resources.Load<GameObject>("NetFAnnequin");
            if (CanSpawnForClients(resource))
                return resource;

            if (playerModels != null && playerModels.prefabs != null)
            {
                foreach (GameObject candidate in playerModels.prefabs)
                    if (CanSpawnForClients(candidate))
                        return candidate;
            }

            return playerPrefab;
        }

        bool CanSpawnForClients(GameObject prefab)
        {
            if (prefab == null) return false;
            if (!prefab.TryGetComponent(out NetworkIdentity identity)) return false;
            return prefab == playerPrefab || identity.assetId != 0;
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            EnsurePlayerModels();
            ApplyFirstPrefabAsPlayerPrefab();

            int playerIndex = nextPlayerIndex++;
            GameObject prefab = ResolvePlayerPrefab(playerIndex);
            if (prefab == null)
            {
                Debug.LogError("BrawlNetworkManager: 没有可用的玩家预制体,无法加入");
                return;
            }

            Transform startPos = GetStartPosition();
            GameObject player = startPos != null
                ? Instantiate(prefab, startPos.position, startPos.rotation)
                : Instantiate(prefab);

            player.name = $"{prefab.name} [player={playerIndex} conn={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, player);

            Debug.Log($"BRAWL: 第{playerIndex + 1}个玩家 -> {prefab.name}");
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.ServerOnPlayerJoined(conn);

            if (!pendingBotsSpawned && conn == NetworkServer.localConnection)
                StartCoroutine(SpawnPendingBotsAfterHost());
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.ServerOnPlayerLeft(conn);

            base.OnServerDisconnect(conn);
        }

        IEnumerator SpawnPendingBotsAfterHost()
        {
            yield return null;
            yield return new WaitForSeconds(0.35f);
            if (pendingBotsSpawned || !NetworkServer.active) yield break;
            pendingBotsSpawned = true;

            int count = Mathf.Clamp(PendingBotCount, 0, BrawlBotBrain.MaxBots);
            if (count <= 0)
            {
                Debug.Log("BRAWL_BOT: skip count=0");
                yield break;
            }

            SpawnBots(count);
        }

        public int SpawnBots(int count)
        {
            if (!NetworkServer.active) return 0;

            int spawned = 0;
            int want = Mathf.Clamp(count, 0, BrawlBotBrain.MaxBots);
            for (int i = 0; i < want; i++)
            {
                if (BrawlBotBrain.AliveCount >= BrawlBotBrain.MaxBots) break;
                if (!TrySpawnOneBot()) break;
                spawned++;
            }

            Debug.Log($"BRAWL_BOT: spawned={spawned} alive={BrawlBotBrain.AliveCount}");
            return spawned;
        }

        bool TrySpawnOneBot()
        {
            EnsurePlayerModels();
            ApplyFirstPrefabAsPlayerPrefab();

            int playerIndex = nextPlayerIndex++;
            GameObject prefab = ResolvePlayerPrefab(playerIndex);
            if (!CanSpawnForClients(prefab))
            {
                Debug.LogError("BrawlNetworkManager: 没有可用的 Bot 预制体");
                return false;
            }

            Transform start = GetStartPosition();
            Vector3 origin = start != null ? start.position : Vector3.up * 2f;
            Vector3 offset = Quaternion.Euler(0f, 80f * playerIndex, 0f) * Vector3.forward * 2.4f;
            Vector3 pos = origin + offset;
            pos.y = origin.y + 1f;

            GameObject bot = Instantiate(prefab, pos, Quaternion.LookRotation(-offset.normalized, Vector3.up));
            bot.name = $"{prefab.name} [bot={playerIndex}]";

            var mouse = bot.GetComponent<FAnnequinMouseActions>();
            if (mouse != null) mouse.enabled = false;

            NetworkServer.Spawn(bot);

            var fan = bot.GetComponent<NetFAnnequinController>();
            if (fan == null)
            {
                Debug.LogError("BrawlNetworkManager: Bot 预制体缺少 NetFAnnequinController");
                NetworkServer.Destroy(bot);
                return false;
            }

            fan.InputActive = true;
            if (bot.GetComponent<BrawlBotBrain>() == null)
                bot.AddComponent<BrawlBotBrain>();

            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.ServerOnBotJoined(fan);

            return true;
        }
    }
}
