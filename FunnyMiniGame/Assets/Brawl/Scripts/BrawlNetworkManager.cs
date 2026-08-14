using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 局域网对抗游戏的网络管理器:玩家加入/离开时通知对局管理器。
    /// 玩家出生点使用场景中的 NetworkStartPosition(轮转)。
    /// </summary>
    public class BrawlNetworkManager : NetworkManager
    {
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);

            Debug.Log($"BRAWL_SMOKE: SERVER_PLAYER_ADDED conn={conn.connectionId} netId={conn.identity?.netId}");

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
