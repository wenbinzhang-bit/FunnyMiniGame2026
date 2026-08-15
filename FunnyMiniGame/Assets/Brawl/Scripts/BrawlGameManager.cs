using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// MiniGame_01 服务端权威对局:
    /// 持有电脑每 0.5 秒得分；被打后电脑落地、持有者被打飞；倒计时结束按分数排名。
    /// 掉出场地会回出生点，不淘汰；受击只触发布娃娃倒地和起身。
    /// </summary>
    public class BrawlGameManager : NetworkBehaviour
    {
        public static BrawlGameManager Instance { get; private set; }

        [Tooltip("低于此高度判定掉出场地，玩家回出生点，电脑回原位")]
        public float KillY = -8f;

        [Tooltip("兼容旧场景字段，本玩法不再把玩家送去观战岛")]
        public Vector3 SpectatorIsland = new Vector3(60f, 3f, 60f);

        [Tooltip("回合结束后到下一回合的间隔秒数")]
        public float RoundRestartDelay = 6f;

        [Tooltip("一回合倒计时秒数")]
        [Min(5f)] public float RoundDurationSeconds = 60f;

        [Tooltip("持有电脑的计分间隔")]
        [Min(0.05f)] public float HoldScoreInterval = 0.5f;

        [Tooltip("每次计分间隔给持有者加的分数；电脑上的 PointsPerHoldTick 优先")]
        [Min(1)] public int HoldScorePoints = 1;

        [Tooltip("开局所需最少人数。单人 Host 调试时用 1")]
        [Min(1)] public int MinPlayersToStart = 1;

        [Tooltip("得分上限，也是进度条满分。任一玩家达到后立即结束并按当前分数结算")]
        [Min(1)] public int HudScoreMax = 99;

        enum EState : byte { Waiting, Playing, RoundEnd }

        public bool HudIsPlaying => state == EState.Playing;
        public bool HudIsRoundEnd => state == EState.RoundEnd;
        public string HudStatusText => statusText;
        public float HudRemainingSeconds
        {
            get
            {
                if (state == EState.Playing)
                    return Mathf.Max(0f, (float)(roundEndsAt - NetworkTime.time));
                return state == EState.Waiting ? RoundDurationSeconds : 0f;
            }
        }

        [SyncVar] EState state = EState.Waiting;
        [SyncVar] string statusText = "";
        [SyncVar] double roundEndsAt;
        [SyncVar] string rankText = "";

        class PlayerEntry
        {
            public NetworkConnectionToClient conn;
            public IBrawlPlayer motor;
        }

        readonly List<PlayerEntry> players = new List<PlayerEntry>();
        double nextScoreTime;
        double roundEndTime;

        void Awake()
        {
            Instance = this;
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

            players.Add(new PlayerEntry { conn = conn, motor = motor });
        }

        [Server]
        public void ServerOnBotJoined(IBrawlPlayer motor)
        {
            if (motor == null || motor.Transform == null) return;
            if (players.Exists(p => p.motor == motor)) return;

            players.Add(new PlayerEntry { conn = null, motor = motor });
            motor.InputActive = state != EState.RoundEnd;

            Transform start = NetworkManager.singleton != null
                ? NetworkManager.singleton.GetStartPosition()
                : null;
            Vector3 pos = start != null ? start.position : motor.Transform.position;
            motor.SpawnPosition = pos;
        }

        [Server]
        public void ServerOnPlayerLeft(NetworkConnectionToClient conn)
        {
            if (conn == null) return;
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
                    if (NetworkTime.time >= roundEndTime)
                    {
                        if (players.Count >= MinPlayersToStart) ServerStartRound();
                        else { state = EState.Waiting; rankText = ""; }
                    }
                    break;
            }
        }

        [Server]
        void ServerUpdateWaiting()
        {
            foreach (var p in players)
                ServerRescueIfNeeded(p);

            ServerRescueLooseComputers();
            ServerTickHoldScores();
            rankText = RankLine();
            statusText = $"等待玩家加入 ({players.Count}/{MinPlayersToStart})... 满 {MinPlayersToStart} 人自动开局 | 持电脑每{HoldScoreInterval:0.##}秒得分，倒计时结束后按分数排名";

            if (players.Count >= MinPlayersToStart) ServerStartRound();
        }

        [Server]
        void ServerStartRound()
        {
            state = EState.Playing;
            roundEndsAt = NetworkTime.time + RoundDurationSeconds;
            nextScoreTime = NetworkTime.time + HoldScoreInterval;
            Debug.Log($"BRAWL_SMOKE: ROUND_STARTED players={players.Count} duration={RoundDurationSeconds}");

            ServerDropAllComputers();

            int i = 0;
            foreach (var p in players)
            {
                p.motor.Score = 0;
                p.motor.InputActive = true;
                if (p.motor is NetFAnnequinController fan)
                    fan.ServerResetTurbo();
                Transform start = NetworkManager.singleton.GetStartPosition();
                Vector3 pos = start != null ? start.position : new Vector3(i * 2f, 3f, 0f);
                p.motor.SpawnPosition = pos;
                p.motor.ServerTeleport(pos + Vector3.up * 1f);
                i++;
            }

            ServerResetAllComputers();
            rankText = RankLine();
            statusText = FormatPlayingStatus();
        }

        [Server]
        void ServerUpdatePlaying()
        {
            float remaining = (float)(roundEndsAt - NetworkTime.time);
            if (remaining <= 0f)
            {
                ServerFinishRound(false, 0);
                return;
            }

            ServerTickHoldScores();
            if (ServerTryFinishByScoreCap())
                return;

            foreach (var p in players)
                ServerRescueIfNeeded(p);

            ServerRescueLooseComputers();
            rankText = RankLine();
            statusText = FormatPlayingStatus();
        }

        [Server]
        bool ServerTryFinishByScoreCap()
        {
            IBrawlPlayer first = null;
            int cap = Mathf.Max(1, HudScoreMax);
            foreach (var p in players)
            {
                if (p?.motor == null) continue;
                if (p.motor.Score < cap) continue;
                p.motor.Score = cap;
                if (first == null) first = p.motor;
            }

            if (first == null) return false;
            ServerFinishRound(true, first.NetId);
            return true;
        }

        [Server]
        void ServerFinishRound(bool reachedScoreCap, uint capWinnerNetId)
        {
            state = EState.RoundEnd;
            roundEndsAt = 0;
            roundEndTime = NetworkTime.time + RoundRestartDelay;

            foreach (var p in players)
                p.motor.InputActive = false;

            ServerDropAllComputers();
            rankText = RankLine();

            string winner = players.Count == 0
                ? "无人参赛"
                : DescribeWinner();
            string reason = reachedScoreCap
                ? $"{PlayerLabel(capWinnerNetId)} 达到 {HudScoreMax} 分!"
                : "时间到!";
            statusText = $"{reason} {winner}  |  {RoundRestartDelay:0} 秒后开新回合";
            Debug.Log($"BRAWL_SMOKE: ROUND_ENDED {statusText} | {rankText}");
        }

        [Server]
        void ServerTickHoldScores()
        {
            if (nextScoreTime <= 0)
                nextScoreTime = NetworkTime.time + HoldScoreInterval;

            while (NetworkTime.time >= nextScoreTime)
            {
                ServerAwardHoldScores();
                nextScoreTime += Mathf.Max(0.05f, HoldScoreInterval);
            }
        }

        [Server]
        void ServerAwardHoldScores()
        {
            var awarded = new HashSet<uint>();

            foreach (KpiComputerObjective computer in AllComputers())
            {
                if (computer == null || !computer.IsHeld) continue;
                ServerAddHoldScore(computer.HolderNetId, awarded);
            }

            foreach (var p in players)
            {
                if (p?.motor is NetFAnnequinController fan && fan.IsHoldingComputer)
                    ServerAddHoldScore(fan.NetId, awarded);
            }
        }

        [Server]
        void ServerAddHoldScore(uint netId, HashSet<uint> awarded)
        {
            if (netId == 0u || !awarded.Add(netId)) return;

            PlayerEntry holder = FindPlayer(netId);
            IBrawlPlayer motor = holder != null ? holder.motor : FindSpawnedPlayer(netId);
            if (motor == null) return;
            if (motor is NetFAnnequinController fan && fan.IsKnockedDown) return;

            int cap = Mathf.Max(1, HudScoreMax);
            motor.Score = Mathf.Min(cap, motor.Score + HoldScorePoints);
        }

        [Server]
        static IBrawlPlayer FindSpawnedPlayer(uint netId)
        {
            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
            {
                if (player != null && player.NetId == netId)
                    return player;
            }

            return null;
        }

        [Server]
        void ServerRescueIfNeeded(PlayerEntry p)
        {
            if (p?.motor == null || p.motor.Transform == null) return;
            if (p.motor.Transform.position.y < KillY)
                ServerRescue(p);
        }

        [Server]
        void ServerRescue(PlayerEntry p)
        {
            if (p.motor is NetFAnnequinController fan)
            {
                fan.ServerForceDropComputer();
                fan.ServerResetTurbo();
            }
            Vector3 spawn = p.motor.SpawnPosition;
            if (spawn.sqrMagnitude < 0.01f)
                spawn = new Vector3(0f, 3f, 0f);
            p.motor.ServerTeleport(spawn + Vector3.up * 1f);
            p.motor.InputActive = state == EState.Playing || state == EState.Waiting;
        }

        [Server]
        void ServerDropAllComputers()
        {
            foreach (var p in players)
            {
                if (p.motor is NetFAnnequinController fan)
                    fan.ServerForceDropComputer();
            }
        }

        [Server]
        void ServerResetAllComputers()
        {
            foreach (KpiComputerObjective computer in AllComputers())
            {
                if (computer != null)
                    computer.ServerResetToSpawn();
            }
        }

        [Server]
        void ServerRescueLooseComputers()
        {
            foreach (KpiComputerObjective computer in AllComputers())
            {
                if (computer != null && computer.ServerIsBelow(KillY))
                    computer.ServerResetToSpawn();
            }
        }

        string PlayerLabel(uint netId)
        {
            return BrawlHudNames.Label(netId, players.Select(p => p.motor));
        }

        [Server]
        PlayerEntry FindPlayer(uint netId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].motor != null && players[i].motor.NetId == netId)
                    return players[i];
            }

            return null;
        }

        [Server]
        static KpiComputerObjective[] AllComputers()
        {
            return FindObjectsOfType<KpiComputerObjective>();
        }

        [Server]
        string FormatPlayingStatus()
        {
            float remaining = Mathf.Max(0f, (float)(roundEndsAt - NetworkTime.time));
            string holders = HolderLine();
            return $"剩余 {FormatTime(remaining)} | {holders} | 持电脑每{HoldScoreInterval:0.##}秒+{HoldScorePoints}分";
        }

        [Server]
        string HolderLine()
        {
            var names = new List<string>();
            foreach (KpiComputerObjective computer in AllComputers())
            {
                if (computer == null || !computer.IsHeld) continue;
                names.Add(PlayerLabel(computer.HolderNetId));
            }

            if (names.Count == 0) return "电脑无人持有";
            return names.Count == 1
                ? $"{names[0]} 持有电脑"
                : string.Join("、", names) + " 持有电脑";
        }

        [Server]
        string DescribeWinner()
        {
            var ranked = RankedPlayers(players.Select(p => p.motor));
            if (ranked.Count == 0) return "无人参赛";

            int topScore = ranked[0].score;
            var winners = ranked.Where(p => p.score == topScore).ToList();
            if (winners.Count == 1)
                return $"第1名 {PlayerLabel(winners[0].id)} {winners[0].score}分";
            return "并列第1名 " + string.Join("、", winners.Select(p => $"{PlayerLabel(p.id)} {p.score}分"));
        }

        [Server]
        string RankLine()
        {
            return FormatRankLine(players.Select(p => p.motor));
        }

        static string FormatRankLine(IEnumerable<IBrawlPlayer> source)
        {
            var ranked = RankedPlayers(source);
            if (ranked.Count == 0) return "";
            var roster = source;
            return string.Join("   ", ranked.Select(p => $"{p.rank}.{BrawlHudNames.Label(p.id, roster)}:{p.score}分"));
        }

        static List<(int rank, uint id, int score)> RankedPlayers(IEnumerable<IBrawlPlayer> source)
        {
            var ordered = source
                .Where(p => p != null)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.NetId)
                .ToList();

            var result = new List<(int rank, uint id, int score)>(ordered.Count);
            int rank = 1;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0 && ordered[i].Score < ordered[i - 1].Score)
                    rank = i + 1;
                result.Add((rank, ordered[i].NetId, ordered[i].Score));
            }

            return result;
        }

        static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
