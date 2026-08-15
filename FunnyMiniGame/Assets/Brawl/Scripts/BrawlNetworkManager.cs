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

        int nextPlayerIndex;

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
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.ServerOnPlayerLeft(conn);

            base.OnServerDisconnect(conn);
        }
    }
}
