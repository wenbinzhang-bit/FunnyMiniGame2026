using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 服务端权威的对局循环:
    /// - 不足 2 人:自由活动,掉落后回出生点
    /// - 满 2 人自动开局:掉出场地或血量归零判淘汰
    /// - 血量归零的玩家当场倒地,掉出场地的玩家传送到观战岛
    /// - 场上仅剩 1 人时该玩家得 1 分,数秒后所有人回出生点开新回合
    /// 状态文本通过 SyncVar 广播,OnGUI 显示。
    /// </summary>
    public class BrawlGameManager : NetworkBehaviour
    {
        public static BrawlGameManager Instance { get; private set; }

        [Tooltip("低于此高度判定掉出场地")]
        public float KillY = -8f;

        [Tooltip("淘汰玩家等待新回合的观战岛位置")]
        public Vector3 SpectatorIsland = new Vector3(60f, 3f, 60f);

        [Tooltip("回合结束后到下一回合的间隔秒数")]
        public float RoundRestartDelay = 4f;

        enum EState : byte { Waiting, Playing, RoundEnd }

        [SyncVar] EState state = EState.Waiting;
        [SyncVar] string statusText = "";

        class PlayerEntry
        {
            public NetworkConnectionToClient conn;
            public IBrawlPlayer motor;
            public bool alive;
        }

        readonly List<PlayerEntry> players = new List<PlayerEntry>();
        double roundEndTime;

        void Awake()
        {
            Instance = this;
            if (GetComponent<PlayerHealthHud>() == null)
                gameObject.AddComponent<PlayerHealthHud>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [Server]
        public void ServerOnPlayerJoined(NetworkConnectionToClient conn)
        {
            if (conn.identity == null) return;
            var motor = conn.identity.GetComponent<IBrawlPlayer>();
            if (motor == null) return;

            // 对局中途加入的玩家直接以存活身份参战
            players.Add(new PlayerEntry { conn = conn, motor = motor, alive = true });
        }

        [Server]
        public void ServerOnPlayerLeft(NetworkConnectionToClient conn)
        {
            players.RemoveAll(p => p.conn == conn);
        }

        [ServerCallback]
        void Update()
        {
            players.RemoveAll(p => p.motor == null || p.motor.Transform == null);

            switch (state)
            {
                case EState.Waiting:
                    ServerUpdateWaiting();
                    break;
                case EState.Playing:
                    ServerUpdatePlaying();
                    break;
                case EState.RoundEnd:
                    if (NetworkTime.localTime >= roundEndTime)
                    {
                        if (players.Count >= 2) ServerStartRound();
                        else { state = EState.Waiting; }
                    }
                    break;
            }
        }

        [Server]
        void ServerUpdateWaiting()
        {
            // 自由活动:掉落直接拉回出生点
            foreach (var p in players)
                if (p.motor.Transform.position.y < KillY)
                    p.motor.ServerTeleport(p.motor.SpawnPosition + Vector3.up * 3f);

            statusText = $"等待玩家加入 ({players.Count}/2)... 满 2 人自动开局 | {ScoreLine()}";

            if (players.Count >= 2) ServerStartRound();
        }

        [Server]
        void ServerStartRound()
        {
            state = EState.Playing;
            Debug.Log($"BRAWL_SMOKE: ROUND_STARTED players={players.Count}");

            int i = 0;
            foreach (var p in players)
            {
                p.alive = true;
                p.motor.InputActive = true;
                if (p.motor.Attributes != null)
                    p.motor.Attributes.ServerResetHealth();
                Transform start = NetworkManager.singleton.GetStartPosition();
                Vector3 pos = start != null ? start.position : new Vector3(i * 2f, 3f, 0f);
                p.motor.SpawnPosition = pos;
                p.motor.ServerTeleport(pos + Vector3.up * 1f);
                i++;
            }
        }

        [Server]
        void ServerUpdatePlaying()
        {
            int aliveCount = 0;
            PlayerEntry lastAlive = null;

            foreach (var p in players)
            {
                if (!p.alive)
                {
                    // 观战岛上也可能掉下去,拉回来
                    if (p.motor.Transform.position.y < KillY)
                        p.motor.ServerTeleport(SpectatorIsland);
                    continue;
                }

                if (p.motor.IsDead)
                {
                    p.alive = false;
                    p.motor.InputActive = false;
                    if (p.motor.Transform.position.y < KillY)
                        p.motor.ServerTeleport(SpectatorIsland);
                    continue;
                }

                if (p.motor.Transform.position.y < KillY)
                {
                    p.alive = false;
                    p.motor.ServerTeleport(SpectatorIsland);
                    continue;
                }

                aliveCount++;
                lastAlive = p;
            }

            statusText = $"对局中!存活 {aliveCount}/{players.Count} | {ScoreLine()}";

            if (players.Count >= 2 && aliveCount <= 1)
            {
                if (lastAlive != null)
                {
                    lastAlive.motor.Score++;
                    statusText = $"P{lastAlive.motor.NetId} 获胜!{RoundRestartDelay:0} 秒后开新回合 | {ScoreLine()}";
                }

                state = EState.RoundEnd;
                roundEndTime = NetworkTime.localTime + RoundRestartDelay;
            }
            else if (players.Count < 2)
            {
                state = EState.Waiting;
            }
        }

        [Server]
        string ScoreLine()
        {
            return string.Join("  ", players.Select(p => $"P{p.motor.NetId}:{p.motor.Score}分"));
        }

        void OnGUI()
        {
            if (!NetworkClient.active && !NetworkServer.active) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = Color.white;

            GUI.Label(new Rect(0, 8, Screen.width, 30), statusText, style);

            var hint = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.LowerLeft
            };
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(16, Screen.height - 168, 420, 152),
                "W S A D : Movement\nSpace : Jump\nLeft Click : Punch\nHold Right Click : Pick Up Laptop\nRelease Right Click : Put Down\nTab : Capture Mouse",
                hint);
        }
    }
}
