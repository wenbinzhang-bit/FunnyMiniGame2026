using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl
{
    /// <summary>
    /// 跨关卡对局：Launcher 大厅全员准备后进入 MiniGame_00 → MiniGame_01，共 2 关；
    /// 每关先显示规则，再空气墙等待其他玩家进场景，倒计时结束才开打。
    /// 第 2 关结束后汇总整场 KPI。
    /// </summary>
    public class BrawlGameManager : NetworkBehaviour
    {
        const float RulesIntroDurationSeconds = 10f;

        public static BrawlGameManager Instance { get; private set; }

        [Tooltip("低于此高度判定掉出场地，玩家回出生点，电脑回原位")]
        public float KillY = -8f;

        [Tooltip("兼容旧场景字段，本玩法不再把玩家送去观战岛")]
        public Vector3 SpectatorIsland = new Vector3(60f, 3f, 60f);

        [Tooltip("回合结束后选择下一局的倒计时，超时未点则结束对局")]
        [Min(1f)] public float ContinueDecisionSeconds = 30f;

        [Tooltip("兼容旧字段，结算等待改用 ContinueDecisionSeconds")]
        public float RoundRestartDelay = 30f;

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

        [Tooltip("进场后空气墙持续时间，倒计时结束且人数足够后开局并撤墙")]
        [Min(1f)] public float WaitingDurationSeconds = 5f;

        [Tooltip("每局开局前规则说明显示秒数，到时自动消失并进入准备")]
        [Min(1f)] public float RulesDurationSeconds = RulesIntroDurationSeconds;

        [Tooltip("Launcher 大厅等待其他玩家加入的秒数，到时进入第一关")]
        [Min(1f)] public float LobbyWaitSeconds = 30f;

        [Tooltip("场景里的空气墙实体，可在 Hierarchy 里拖墙调整")]
        public BrawlAirWall AirWall;

        enum EState : byte { Waiting, Playing, RoundEnd, Rules, Lobby, FinalKpi }

        public bool HudIsPlaying => IsHudState(EState.Playing);
        public bool HudIsWaiting => IsHudState(EState.Waiting);
        public bool HudIsRoundEnd => IsHudState(EState.RoundEnd);
        public bool HudIsShowingRules => IsHudState(EState.Rules);
        public bool HudIsLobby =>
            state == EState.Lobby
            && !changingScene
            && BrawlLevelCatalog.ActiveSceneIsLauncher();
        public bool HudIsFinalKpi => state == EState.FinalKpi && !changingScene;
        public int HudMatchSeq => matchSeq;
        public bool HudHasNextLevel => BrawlLevelCatalog.HasNextLevel(currentLevelName);
        public string HudRulesTitle => string.IsNullOrEmpty(rulesTitle) ? "本局规则" : rulesTitle;
        public string HudRulesBody => rulesBody;
        public string HudKpiBoardText => kpiBoardText;
        public bool HudAirWallActive => airWallActive;
        public bool HudContinueRequested => nextRoundRequested;
        public bool HudIsHost => NetworkServer.active;
        public bool HudShowLobbyActions =>
            BrawlLevelCatalog.ActiveSceneIsLauncher()
            && ServerCanAcceptLobbyReady();

        public bool ServerCanAcceptLobbyReady()
        {
            return state == EState.Lobby && !changingScene;
        }
        public bool HudLobbyAllReady => lobbyAllReady;
        public string HudStatusText => statusText;
        public string HudLobbyReadyLine => lobbyReadyLine;
        public float HudRemainingSeconds
        {
            get
            {
                if (changingScene) return 0f;
                if (state == EState.Playing)
                    return Mathf.Max(0f, (float)(roundEndsAt - NetworkTime.time));
                if (state == EState.Lobby)
                    return 0f;
                if (state == EState.Rules)
                {
                    if (rulesEndsAt > 0)
                        return Mathf.Max(0f, (float)(rulesEndsAt - NetworkTime.time));
                    return RulesDurationSeconds;
                }
                if (state == EState.Waiting)
                {
                    if (waitingEndsAt > 0)
                        return Mathf.Max(0f, (float)(waitingEndsAt - NetworkTime.time));
                    return 0f;
                }
                if (state == EState.RoundEnd)
                {
                    if (continueEndsAt > 0)
                        return Mathf.Max(0f, (float)(continueEndsAt - NetworkTime.time));
                    return ContinueDecisionSeconds;
                }
                return 0f;
            }
        }

        bool IsHudState(EState want)
        {
            if (state != want || changingScene) return false;
            if (want == EState.Lobby)
                return BrawlLevelCatalog.ActiveSceneIsLauncher();
            if (want == EState.FinalKpi)
                return true;
            return BrawlLevelCatalog.ActiveSceneIsLevel()
                && BrawlLevelCatalog.NormalizeName(currentLevelName) == BrawlLevelCatalog.ActiveSceneName();
        }

        [SyncVar] EState state = EState.Waiting;
        [SyncVar] string statusText = "";
        [SyncVar] double roundEndsAt;
        [SyncVar] double waitingEndsAt;
        [SyncVar] double rulesEndsAt;
        [SyncVar] double continueEndsAt;
        [SyncVar] double lobbyEndsAt;
        [SyncVar] bool nextRoundRequested;
        [SyncVar(hook = nameof(OnAirWallActiveChanged))] bool airWallActive = true;
        [SyncVar] string rankText = "";
        [SyncVar] string currentLevelName = "";
        [SyncVar] string rulesTitle = "本局规则";
        [SyncVar] string rulesBody = "";
        [SyncVar] string kpiBoardText = "";
        [SyncVar] string lobbyReadyLine = "";
        [SyncVar] bool lobbyAllReady;
        [SyncVar] int matchSeq;

        class PlayerEntry
        {
            public NetworkConnectionToClient conn;
            public IBrawlPlayer motor;
            public int connectionId = -1;
            public int botIndex = -1;
        }

        readonly List<PlayerEntry> players = new List<PlayerEntry>();
        int nextBotIndex;
        double nextScoreTime;
        bool stoppingSession;
        [SyncVar] bool changingScene;
        string pendingLevelName = "";
        bool levelSessionStarted;

        void Awake()
        {
            // 旧 Prefab 和脚本热重载会保留曾经的 3 秒序列化值，规则页现统一固定为 10 秒。
            RulesDurationSeconds = RulesIntroDurationSeconds;
            if (Instance != null && Instance != this)
                return;
            Instance = this;
            BrawlRunRecord.Ensure(BrawlSession.Instance != null ? BrawlSession.Instance.transform : transform);
            ApplyAirWall();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            BrawlRunRecord.Ensure(BrawlSession.Instance != null ? BrawlSession.Instance.transform : transform);
            Record.BeginNewRun();
            currentLevelName = SceneManager.GetActiveScene().name;
            if (BrawlLevelCatalog.IsLauncher(currentLevelName) || !BrawlLevelCatalog.IsLevel(currentLevelName))
                ServerEnterLobby();
            else
                ServerEnterMatchHold(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyAirWall();
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

            if (players.Exists(p => p.motor == motor))
            {
                motor.InputActive = state == EState.Lobby || state == EState.Waiting || state == EState.Playing;
                return;
            }

            players.Add(new PlayerEntry
            {
                conn = conn,
                motor = motor,
                connectionId = conn.connectionId
            });
            Record.EnsureSeat(conn.connectionId, -1, BrawlHudNames.Label(motor.NetId, PlayersForHud()));
            motor.InputActive = state == EState.Lobby || state == EState.Waiting || state == EState.Playing;
            if (state == EState.Lobby && motor is NetFAnnequinController fan)
                BrawlLobbyReady.ApplyForLobby(fan, false);
        }

        [Server]
        public void ServerOnBotJoined(IBrawlPlayer motor)
        {
            if (!IsLiveMotor(motor)) return;
            if (players.Exists(p => p.motor == motor)) return;

            int botIndex = nextBotIndex++;
            players.Add(new PlayerEntry
            {
                conn = null,
                motor = motor,
                connectionId = -1,
                botIndex = botIndex
            });
            Record.EnsureSeat(-1, botIndex, BrawlHudNames.Label(motor.NetId, PlayersForHud()));
            motor.InputActive = state == EState.Lobby || state == EState.Waiting || state == EState.Playing;
            if (motor is NetFAnnequinController bot)
                BrawlLobbyReady.ApplyForLobby(bot, true);

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

        void Update()
        {
            if (!NetworkServer.active) return;

            if (changingScene)
                RecoverStuckSceneChange();

            if (TryOpenArrivedLevel())
                return;

            if (changingScene)
                return;

            PurgeDeadPlayers();

            switch (state)
            {
                case EState.Lobby:
                    ServerUpdateLobby();
                    break;
                case EState.Rules:
                    ServerUpdateRules();
                    break;
                case EState.Waiting:
                    ServerUpdateWaiting();
                    break;
                case EState.Playing:
                    ServerUpdatePlaying();
                    break;
                case EState.RoundEnd:
                    ServerUpdateRoundEnd();
                    break;
            }
        }

        void LateUpdate()
        {
            bool showWall = ShouldShowMatchAirWall();
            if (showWall)
                BrawlAirWall.EnsureInLevel(this);
            BrawlAirWall.SetAllActive(showWall);
        }

        bool ShouldShowMatchAirWall()
        {
            if (BrawlLevelCatalog.ActiveSceneIsLauncher())
                return false;
            if (!BrawlLevelCatalog.ActiveSceneIsLevel())
                return false;
            return state != EState.Playing && state != EState.RoundEnd && state != EState.FinalKpi;
        }

        [Server]
        public void ServerPrepareSceneChange()
        {
            changingScene = true;
            levelSessionStarted = false;
            BrawlSession.AdoptAllPlayers();
            ServerDropAllComputers();
            PurgeDeadPlayers();
            ServerClearRoundRuntime("正在进入下一关");
        }

        [Server]
        public void ServerOnSceneReady(string sceneName)
        {
            string name = BrawlLevelCatalog.NormalizeName(sceneName);
            changingScene = false;
            pendingLevelName = "";
            currentLevelName = name;
            AirWall = null;
            BrawlAirWall.ClearStale();
            BrawlLobbyStage.Ensure();
            try
            {
                ServerRebuildPlayers();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("BRAWL_SMOKE: REBUILD_PLAYERS " + ex.Message);
                PurgeDeadPlayers();
            }

            if (BrawlLevelCatalog.IsLauncher(name))
            {
                levelSessionStarted = true;
                ServerEnterLobby();
                return;
            }

            if (BrawlLevelCatalog.IsLevel(name))
                ServerBeginNewLevel();
        }

        bool TryOpenArrivedLevel()
        {
            if (SceneLoadInProgress())
                return changingScene;

            string active = BrawlLevelCatalog.ActiveSceneName();
            string pending = BrawlLevelCatalog.NormalizeName(pendingLevelName);
            bool arrivedPending = !string.IsNullOrEmpty(pending) && pending == active;
            bool arrivedUnknownLevel = string.IsNullOrEmpty(pending)
                && BrawlLevelCatalog.IsLevel(active)
                && BrawlLevelCatalog.NormalizeName(currentLevelName) != active;
            bool stuckOnNewScene = string.IsNullOrEmpty(pending)
                && BrawlLevelCatalog.IsLevel(active)
                && !levelSessionStarted
                && (state == EState.RoundEnd || state == EState.FinalKpi || nextRoundRequested);

            if (!arrivedPending && !arrivedUnknownLevel && !stuckOnNewScene)
                return false;

            Debug.Log($"BRAWL_SMOKE: OPEN_ARRIVED_LEVEL active={active} pending={pending} state={state} started={levelSessionStarted}");
            ServerOnSceneReady(active);
            return true;
        }

        static bool SceneLoadInProgress()
        {
            return NetworkManager.loadingSceneAsync != null && !NetworkManager.loadingSceneAsync.isDone;
        }

        void ServerClearRoundRuntime(string message)
        {
            nextRoundRequested = false;
            stoppingSession = false;
            roundEndsAt = 0;
            waitingEndsAt = 0;
            rulesEndsAt = 0;
            continueEndsAt = 0;
            lobbyEndsAt = 0;
            lobbyAllReady = false;
            lobbyReadyLine = "";
            rankText = "";
            if (!string.IsNullOrEmpty(message))
                statusText = message;
        }

        [Server]
        void ServerBeginNewLevel()
        {
            levelSessionStarted = true;
            pendingLevelName = "";
            matchSeq++;
            ServerClearRoundRuntime("");
            PurgeDeadPlayers();
            foreach (var p in players)
            {
                if (!IsLiveMotor(p?.motor)) continue;
                p.motor.Score = 0;
                p.motor.InputActive = false;
                if (p.motor is NetFAnnequinController fan)
                {
                    BrawlLobbyReady.Clear(fan);
                    fan.ServerResetTurbo();
                    fan.ServerForceDropComputer();
                }
            }

            Record.ResetCurrentRoundScores();
            ServerPlacePlayersInLevel();
            Debug.Log($"BRAWL_SMOKE: NEW_LEVEL_RESET {currentLevelName} match={matchSeq} players={players.Count} duration={RoundDurationSeconds}");
            ServerEnterMatchHold(true);
            StartCoroutine(RebuildPlayersAfterLevelLoad());
        }

        void ServerPlacePlayersInLevel()
        {
            int i = 0;
            foreach (var p in players)
            {
                if (!IsLiveMotor(p?.motor)) continue;
                Transform start = NetworkManager.singleton != null
                    ? NetworkManager.singleton.GetStartPosition()
                    : null;
                Vector3 pos = start != null ? start.position : new Vector3(i * 2f, 3f, 0f);
                p.motor.SpawnPosition = pos;
                p.motor.ServerTeleport(pos + Vector3.up * 1f);
                i++;
            }
        }

        System.Collections.IEnumerator RebuildPlayersAfterLevelLoad()
        {
            for (int i = 0; i < 8; i++)
            {
                yield return null;
                if (!NetworkServer.active || !levelSessionStarted) yield break;
                ServerRebuildPlayers();
                if (BrawlNetworkManager.SingletonBrawl != null)
                    BrawlNetworkManager.SingletonBrawl.ServerEnsureMatchActors();
            }
        }

        [Server]
        void ServerEnterLobby()
        {
            state = EState.Lobby;
            pendingLevelName = "";
            levelSessionStarted = true;
            currentLevelName = BrawlLevelCatalog.LauncherScene;
            lobbyEndsAt = 0;
            rulesEndsAt = 0;
            waitingEndsAt = 0;
            lobbyAllReady = false;
            Record.BeginNewRun();
            ServerSetAirWall(false);
            foreach (var p in players)
            {
                if (p?.motor != null)
                    p.motor.InputActive = true;
                if (p?.motor is NetFAnnequinController fan)
                    BrawlLobbyReady.ApplyForLobby(fan, !IsHumanPlayer(p));
            }
            RefreshLobbyReadyStatus();
            Debug.Log("BRAWL_SMOKE: LOBBY_STARTED wait_for_all_ready");
        }

        [Server]
        void ServerUpdateLobby()
        {
            ServerRebuildPlayers();
            foreach (var p in players)
            {
                ServerRescueIfNeeded(p);
                if (p?.motor != null)
                    p.motor.InputActive = true;
                if (p?.motor is NetFAnnequinController fan)
                    BrawlLobbyReady.KeepBotReady(fan, !IsHumanPlayer(p));
            }

            BrawlLobbyReady.Tally tally = TallyLobbyReady();
            lobbyReadyLine = tally.Line;
            lobbyAllReady = tally.CanEnterFirstLevel(MinPlayersToStart);
            if (lobbyAllReady)
            {
                statusText = $"{lobbyReadyLine}    全员已准备，正在进入第一关";
                ServerLoadFirstLevel();
                return;
            }

            statusText = $"{lobbyReadyLine}    全员准备后进入第一关";
        }

        [Server]
        void ServerLoadFirstLevel()
        {
            string first = BrawlLevelCatalog.GetFirstLevel();
            if (string.IsNullOrEmpty(first))
            {
                statusText = "没有找到 MiniGame 关卡，请把 MiniGame_00 加入 Build Settings";
                Debug.LogWarning("BrawlGameManager: 没有可用关卡");
                return;
            }

            ServerChangeToLevel(first);
        }

        void ServerChangeToLevel(string sceneName)
        {
            Debug.Log($"BRAWL_SMOKE: CHANGE_SCENE_ENTER name={sceneName} changing={changingScene} nm={NetworkManager.singleton}");
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("BrawlGameManager: 切关名为空");
                return;
            }

            pendingLevelName = BrawlLevelCatalog.NormalizeName(sceneName);
            levelSessionStarted = false;
            changingScene = true;
            ServerClearRoundRuntime($"正在进入 {BrawlLevelCatalog.GetLevelTitle(sceneName)}");
            ClearAllLobbyReady();
            Debug.Log("BRAWL_SMOKE: CHANGE_SCENE " + pendingLevelName);

            if (NetworkManager.singleton == null)
            {
                Debug.LogError("BrawlGameManager: NetworkManager.singleton 为空，改用本地切关");
                StartCoroutine(ForceLoadLevel(pendingLevelName));
                return;
            }

            var brawlNet = NetworkManager.singleton as BrawlNetworkManager;
            bool started = brawlNet != null
                ? brawlNet.TryChangeLevel(pendingLevelName)
                : StartFallbackSceneChange(pendingLevelName);
            if (started) return;

            Debug.LogWarning("BRAWL_SMOKE: Mirror 切关失败，改用 ForceLoadLevel " + pendingLevelName);
            StartCoroutine(ForceLoadLevel(pendingLevelName));
        }

        bool StartFallbackSceneChange(string sceneName)
        {
            NetworkManager.singleton.ServerChangeScene(sceneName);
            return NetworkManager.loadingSceneAsync != null;
        }

        System.Collections.IEnumerator ForceLoadLevel(string sceneName)
        {
            int buildIndex = BrawlLevelCatalog.GetBuildIndex(sceneName);
            Debug.Log($"BRAWL_SMOKE: FORCE_LOAD {sceneName} buildIndex={buildIndex}");
            if (buildIndex < 0)
            {
                changingScene = false;
                statusText = $"切关失败：Build Settings 没有 {sceneName}";
                yield break;
            }

            changingScene = true;
            BrawlSession.AdoptAllPlayers();
            AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            if (op == null)
            {
                changingScene = false;
                statusText = $"LoadSceneAsync 失败 {sceneName}";
                yield break;
            }

            while (!op.isDone)
                yield return null;

            NetworkServer.SpawnObjects();
            if (NetworkManager.singleton != null)
                NetworkManager.singleton.OnClientSceneChanged();
            ServerOnSceneReady(sceneName);
        }

        void RecoverStuckSceneChange()
        {
            if (!changingScene && string.IsNullOrEmpty(pendingLevelName)) return;
            if (SceneLoadInProgress()) return;
            string active = BrawlLevelCatalog.ActiveSceneName();
            string pending = BrawlLevelCatalog.NormalizeName(pendingLevelName);
            if (!string.IsNullOrEmpty(pending) && pending == active)
            {
                Debug.Log("BRAWL_SMOKE: RECOVER_ARRIVED " + active);
                ServerOnSceneReady(active);
                return;
            }

            if (!string.IsNullOrEmpty(pending) && pending != active)
            {
                Debug.LogWarning("BRAWL_SMOKE: RECOVER_FORCE_LOAD " + pending);
                StartCoroutine(ForceLoadLevel(pending));
                return;
            }

            Debug.Log("BRAWL_SMOKE: RECOVER_STUCK_SCENE_CHANGE");
            changingScene = false;
        }

        [Server]
        void ServerEnterMatchHold(bool resetRank)
        {
            levelSessionStarted = true;
            ClearAllLobbyReady();
            AirWall = null;
            BrawlAirWall.ClearStale();
            BrawlAirWall.EnsureInLevel(this);
            ServerEnterRules(resetRank);
        }

        [Server]
        void ServerEnterRules(bool resetRank)
        {
            state = EState.Rules;
            currentLevelName = SceneManager.GetActiveScene().name;
            RulesDurationSeconds = RulesIntroDurationSeconds;
            rulesEndsAt = NetworkTime.time + Mathf.Max(1f, RulesDurationSeconds);
            waitingEndsAt = 0;
            if (resetRank) rankText = "";
            ServerSetAirWall(true);
            BindLevelRules();
            foreach (var p in players)
            {
                if (p?.motor != null)
                    p.motor.InputActive = false;
            }
            statusText = $"请阅读{HudRulesTitle}";
            Debug.Log($"BRAWL_SMOKE: RULES_STARTED level={currentLevelName} duration={RulesDurationSeconds}");
        }

        [Server]
        void ServerUpdateRules()
        {
            foreach (var p in players)
            {
                ServerRescueIfNeeded(p);
                ServerContainInAirWall(p);
                if (p?.motor != null)
                    p.motor.InputActive = false;
            }

            ServerRescueLooseComputers();
            if (rulesEndsAt <= 0)
                rulesEndsAt = NetworkTime.time + Mathf.Max(1f, RulesDurationSeconds);

            float remain = Mathf.Max(0f, (float)(rulesEndsAt - NetworkTime.time));
            statusText = $"请阅读本局规则，{Mathf.CeilToInt(remain)} 秒后进入空气墙等待区";
            if (remain <= 0f)
                ServerEnterWaiting(false);
        }

        [Server]
        void ServerEnterWaiting(bool resetRank)
        {
            state = EState.Waiting;
            rulesEndsAt = 0;
            waitingEndsAt = 0;
            if (resetRank) rankText = "";
            ServerSetAirWall(true);
            foreach (var p in players)
            {
                if (p?.motor != null)
                    p.motor.InputActive = true;
            }
            statusText = "空气墙等待区：等待其他玩家进入场景";
            Debug.Log("BRAWL_SMOKE: WAITING_STARTED hold_for_scene_ready");
        }

        [Server]
        void ServerUpdateWaiting()
        {
            foreach (var p in players)
            {
                ServerRescueIfNeeded(p);
                ServerContainInAirWall(p);
            }

            ServerRescueLooseComputers();
            rankText = RankLine();
            ServerSetAirWall(true);

            if (!AllHumansInScene())
            {
                waitingEndsAt = 0;
                statusText = $"空气墙等待区  已进入 {CountHumanPlayers()}/{ExpectedHumanConnections()}  等待其他玩家场景加载";
                return;
            }

            if (waitingEndsAt <= 0)
            {
                waitingEndsAt = NetworkTime.time + Mathf.Max(1f, WaitingDurationSeconds);
                Debug.Log($"BRAWL_SMOKE: WAITING_COUNTDOWN duration={WaitingDurationSeconds}");
            }

            float remain = Mathf.Max(0f, (float)(waitingEndsAt - NetworkTime.time));
            statusText = $"空气墙等待中，结束后正式开始  玩家 {CountHumanPlayers()}";

            if (remain <= 0f)
            {
                ServerRebuildPlayers();
                if (!AllHumansInScene())
                {
                    waitingEndsAt = 0;
                    statusText = "空气墙等待区：等待其他玩家进入场景";
                    return;
                }

                ServerStartRound();
            }
        }

        [Server]
        void ServerUpdateRoundEnd()
        {
            if (stoppingSession || changingScene || !levelSessionStarted) return;
            if (!string.IsNullOrEmpty(pendingLevelName)) return;

            if (nextRoundRequested)
            {
                nextRoundRequested = false;
                ServerAdvanceAfterRound();
                return;
            }

            if (continueEndsAt <= 0)
                continueEndsAt = NetworkTime.time + ResolveContinueSeconds();

            float remain = Mathf.Max(0f, (float)(continueEndsAt - NetworkTime.time));
            string action = HudHasNextLevel ? "下一关" : "查看总成绩";
            statusText = $"点击「{action}」继续，否则 {FormatTime(remain)} 后自动继续";
            if (remain <= 0f)
                ServerAdvanceAfterRound();
        }

        public void RequestLobbyStart()
        {
            if (!HudShowLobbyActions) return;

            NetFAnnequinController local = LocalLobbyPlayer();
            if (local != null)
                local.CmdSetLobbyReady(true);

            if (NetworkServer.active)
            {
                ServerTryStartFromLobby();
                return;
            }

            if (local != null)
                local.CmdRequestLobbyStart();
        }

        [Server]
        public void ServerTryStartFromLobby()
        {
            if (state != EState.Lobby || changingScene) return;
            ServerRebuildPlayers();
            BrawlLobbyReady.Tally tally = TallyLobbyReady();
            lobbyReadyLine = tally.Line;
            lobbyAllReady = tally.CanEnterFirstLevel(MinPlayersToStart);
            if (!lobbyAllReady)
            {
                statusText = $"{lobbyReadyLine}    需全员准备后才能进入第一关";
                return;
            }

            ServerLoadFirstLevel();
        }

        public void RequestLobbyReadyToggle()
        {
            if (!HudShowLobbyActions) return;
            NetFAnnequinController local = LocalLobbyPlayer();
            if (local == null) return;
            local.CmdSetLobbyReady(!local.LobbyReady);
        }

        public bool HudLocalIsReady()
        {
            NetFAnnequinController local = LocalLobbyPlayer();
            return local != null && local.LobbyReady;
        }

        static NetFAnnequinController LocalLobbyPlayer()
        {
            if (NetworkClient.localPlayer != null)
            {
                NetFAnnequinController fromIdentity = NetworkClient.localPlayer.GetComponent<NetFAnnequinController>();
                if (fromIdentity != null)
                    return fromIdentity;
            }

            foreach (NetFAnnequinController fan in FindObjectsOfType<NetFAnnequinController>())
            {
                if (fan != null && fan.isLocalPlayer)
                    return fan;
            }

            return null;
        }

        public void DebugSetRemainingSeconds(float seconds)
        {
            seconds = Mathf.Max(0.1f, seconds);
            if (NetworkServer.active)
            {
                ServerDebugSetRemainingSeconds(seconds);
                return;
            }

            NetFAnnequinController local = NetworkClient.localPlayer != null
                ? NetworkClient.localPlayer.GetComponent<NetFAnnequinController>()
                : null;
            if (local != null)
                local.CmdDebugSetRemainingSeconds(seconds);
        }

        [Server]
        public void ServerDebugSetRemainingSeconds(float seconds)
        {
            if (state != EState.Playing) return;
            roundEndsAt = NetworkTime.time + Mathf.Max(0.1f, seconds);
            Debug.Log($"BRAWL_DEBUG: current round remaining={seconds:0.#}s");
        }

        public void RequestNextRound()
        {
            RecoverStuckSceneChange();
            Debug.Log($"BRAWL_SMOKE: REQUEST_NEXT_ROUND state={state} changing={changingScene} scene={BrawlLevelCatalog.ActiveSceneName()}");

            if (NetworkServer.active)
            {
                ServerOnNextRoundRequested();
                return;
            }

            NetFAnnequinController local = NetworkClient.localPlayer != null
                ? NetworkClient.localPlayer.GetComponent<NetFAnnequinController>()
                : null;
            if (local != null)
                local.CmdRequestNextRound();
        }

        public void ServerOnNextRoundRequested()
        {
            if (!NetworkServer.active || stoppingSession) return;
            RecoverStuckSceneChange();
            if (changingScene) return;
            if (state != EState.RoundEnd && state != EState.FinalKpi)
            {
                Debug.LogWarning($"BrawlGameManager: 下一关被忽略，当前状态 {state}");
                return;
            }

            nextRoundRequested = true;
            statusText = HudHasNextLevel ? "已确认下一关" : "已确认查看总成绩";
            Debug.Log("BRAWL_SMOKE: NEXT_ROUND_REQUESTED");
            ServerAdvanceAfterRound();
        }

        void ServerAdvanceAfterRound()
        {
            if (!NetworkServer.active) return;
            RecoverStuckSceneChange();
            if (changingScene)
            {
                Debug.LogWarning("BRAWL_SMOKE: ADVANCE blocked, still changingScene");
                return;
            }

            string current = BrawlLevelCatalog.ActiveSceneIsLevel()
                ? BrawlLevelCatalog.ActiveSceneName()
                : BrawlLevelCatalog.NormalizeName(currentLevelName);
            string next = BrawlLevelCatalog.GetNextLevel(current);
            Debug.Log($"BRAWL_SMOKE: ADVANCE_AFTER_ROUND current={current} next={next}");
            if (string.IsNullOrEmpty(next))
            {
                ServerEnterFinalKpi();
                return;
            }

            ServerChangeToLevel(next);
        }

        [Server]
        void ServerEnterFinalKpi()
        {
            state = EState.FinalKpi;
            nextRoundRequested = false;
            continueEndsAt = 0;
            kpiBoardText = FormatKpiBoard();
            foreach (var p in players)
            {
                if (p?.motor != null)
                    p.motor.InputActive = false;
            }
            statusText = "2 关全部结束，这是整场 KPI 汇总";
            Debug.Log("BRAWL_SMOKE: FINAL_KPI\n" + kpiBoardText);
        }

        float ResolveContinueSeconds()
        {
            if (ContinueDecisionSeconds >= 1f) return ContinueDecisionSeconds;
            if (RoundRestartDelay >= 1f) return RoundRestartDelay;
            return 30f;
        }

        [Server]
        void ServerStopSession()
        {
            ServerEnterRules(true);
        }

        [Server]
        void ServerStartRound()
        {
            ServerRebuildPlayers();
            state = EState.Playing;
            waitingEndsAt = 0;
            rulesEndsAt = 0;
            continueEndsAt = 0;
            airWallActive = false;
            BrawlAirWall.SetAllActive(false);
            ServerSetAirWall(false);
            roundEndsAt = NetworkTime.time + Mathf.Max(5f, RoundDurationSeconds);
            nextScoreTime = NetworkTime.time + HoldScoreInterval;
            Debug.Log($"BRAWL_SMOKE: ROUND_STARTED players={players.Count} duration={RoundDurationSeconds}");

            ServerDropAllComputers();
            Record.ResetCurrentRoundScores();

            int i = 0;
            foreach (var p in players)
            {
                if (p?.motor == null) continue;
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
            nextRoundRequested = false;
            continueEndsAt = NetworkTime.time + ResolveContinueSeconds();

            foreach (var p in players)
                p.motor.InputActive = false;

            ServerDropAllComputers();
            ServerRecordLevelScores();
            rankText = RankLine();
            kpiBoardText = FormatKpiBoard();

            string winner = players.Count == 0
                ? "无人参赛"
                : DescribeWinner();
            string reason = reachedScoreCap
                ? $"{PlayerLabel(capWinnerNetId)} 达到 {HudScoreMax} 分!"
                : "时间到!";
            statusText = $"{reason} {winner}  |  点击「下一局」继续，否则 {ResolveContinueSeconds():0} 秒后回到等待";
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
            PlayerEntry entry = holder ?? FindPlayer(netId);
            if (entry != null)
            {
                Record.SetCurrentRoundScore(
                    entry.connectionId,
                    entry.botIndex,
                    BrawlHudNames.Label(motor.NetId, PlayersForHud()),
                    motor.Score);
            }
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
            if (!IsLiveMotor(p?.motor) || p.motor.Transform == null) return;
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
            Vector3 dest = spawn + Vector3.up * 1f;
            if (airWallActive)
            {
                BrawlAirWall wall = BrawlAirWall.Ensure(this);
                if (wall != null && !wall.Contains(dest))
                    dest = wall.ClampInside(dest);
            }
            p.motor.ServerTeleport(dest);
            p.motor.InputActive = state == EState.Playing || state == EState.Waiting;
        }

        [Server]
        void ServerContainInAirWall(PlayerEntry p)
        {
            if (!airWallActive || !IsLiveMotor(p?.motor) || p.motor.Transform == null) return;

            BrawlAirWall wall = BrawlAirWall.Ensure(this);
            if (wall == null) return;
            Vector3 pos = p.motor.Transform.position;
            if (wall.Contains(pos)) return;
            p.motor.ServerTeleport(wall.ClampInside(pos));
        }

        [Server]
        void ServerSetAirWall(bool active)
        {
            bool changed = airWallActive != active;
            airWallActive = active;
            ApplyAirWall();
            if (changed)
                Debug.Log($"BRAWL_SMOKE: AIR_WALL {(active ? "ON" : "OFF")}");
        }

        void OnAirWallActiveChanged(bool _, bool __)
        {
            ApplyAirWall();
        }

        void ApplyAirWall()
        {
            bool show = ShouldShowMatchAirWall();
            BrawlAirWall.ClearStale();
            BrawlAirWall wall = show ? BrawlAirWall.EnsureInLevel(this) : BrawlAirWall.Ensure(this);
            if (wall != null)
                wall.SetActiveWall(show);
            BrawlAirWall.SetAllActive(show);
        }

        static bool IsLiveMotor(IBrawlPlayer motor)
        {
            return motor is Object obj && obj != null;
        }

        static bool IsDeadPlayer(PlayerEntry player)
        {
            return player == null || !IsLiveMotor(player.motor);
        }

        void PurgeDeadPlayers()
        {
            players.RemoveAll(IsDeadPlayer);
        }

        [Server]
        void ServerRebuildPlayers()
        {
            PurgeDeadPlayers();
            foreach (var pair in NetworkServer.connections)
            {
                NetworkConnectionToClient conn = pair.Value;
                if (conn?.identity == null) continue;
                ServerOnPlayerJoined(conn);
            }

            foreach (NetFAnnequinController fan in FindObjectsOfType<NetFAnnequinController>())
            {
                if (fan == null || fan.netId == 0) continue;
                if (players.Exists(p => p.motor == fan)) continue;
                if (fan.connectionToClient != null)
                {
                    ServerOnPlayerJoined(fan.connectionToClient);
                    continue;
                }

                if (fan.GetComponent<BrawlBotBrain>() != null)
                    ServerOnBotJoined(fan);
            }
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

        void BindLevelRules()
        {
            string title = BrawlLevelCatalog.GetLevelTitle(currentLevelName);
            BrawlLevelInfo info = BrawlLevelInfo.FindInScene();
            rulesTitle = info != null && !string.IsNullOrEmpty(info.Title)
                ? $"{title}  {info.Title}"
                : $"{title}  本局规则";
            rulesBody = info != null && !string.IsNullOrEmpty(info.Rules)
                ? info.Rules
                : DefaultRulesText();
        }

        static string DefaultRulesText()
        {
            return
                "抱住笔记本电脑并坚持不放，就能持续得分。\n" +
                "被拳头打中会丢掉电脑，自己也会被打飞。\n" +
                "先到 99 分，或时间结束时按分数排名。\n" +
                "掉出场地会送回出生点，不会淘汰。\n\n" +
                "WASD 移动　　空格 跳跃　　Shift 加速\n" +
                "左键 出拳　　按住右键 抱起电脑　　松开右键 放下\n" +
                "Esc 释放鼠标　　Alt 重新捕获鼠标\n\n" +
                "开局有空气墙，倒计时结束后撤墙，正式开打。";
        }

        static BrawlRunRecord Record => BrawlRunRecord.Ensure(BrawlSession.Instance != null ? BrawlSession.Instance.transform : null);

        [Server]
        void ServerRecordLevelScores()
        {
            var roster = PlayersForHud();
            foreach (var p in players)
            {
                if (p?.motor == null) continue;
                Record.SetCurrentRoundScore(
                    p.connectionId,
                    p.botIndex,
                    BrawlHudNames.Label(p.motor.NetId, roster),
                    p.motor.Score);
            }

            Record.CommitLevel(currentLevelName);
        }

        string FormatKpiBoard()
        {
            return Record.FormatBoard();
        }

        IEnumerable<IBrawlPlayer> PlayersForHud()
        {
            return players.Select(p => p.motor);
        }

        void RefreshLobbyReadyStatus()
        {
            BrawlLobbyReady.Tally tally = TallyLobbyReady();
            lobbyReadyLine = tally.Line;
            lobbyAllReady = tally.CanEnterFirstLevel(MinPlayersToStart);
            statusText = $"{lobbyReadyLine}    全员准备后进入第一关";
        }

        BrawlLobbyReady.Tally TallyLobbyReady()
        {
            var tally = new BrawlLobbyReady.Tally();
            var counted = new HashSet<NetFAnnequinController>();

            foreach (var pair in NetworkServer.connections)
            {
                NetworkConnectionToClient conn = pair.Value;
                if (conn?.identity == null) continue;
                NetFAnnequinController fan = conn.identity.GetComponent<NetFAnnequinController>();
                if (fan == null || !counted.Add(fan)) continue;
                tally.Add(true, false, fan.LobbyReady);
            }

            foreach (var p in players)
            {
                if (IsHumanPlayer(p)) continue;
                if (p?.motor is NetFAnnequinController fan && counted.Add(fan))
                    tally.Add(true, true, true);
            }

            return tally;
        }

        void ClearAllLobbyReady()
        {
            lobbyAllReady = false;
            lobbyReadyLine = "";
            foreach (var p in players)
            {
                if (p?.motor is NetFAnnequinController fan)
                    BrawlLobbyReady.Clear(fan);
            }
        }

        int CountHumanPlayers()
        {
            int total = 0;
            foreach (var p in players)
            {
                if (IsHumanPlayer(p))
                    total++;
            }

            return total;
        }

        static bool IsHumanPlayer(PlayerEntry player)
        {
            return player?.motor != null && player.botIndex < 0;
        }

        int ExpectedHumanConnections()
        {
            int count = 0;
            foreach (var pair in NetworkServer.connections)
            {
                if (pair.Value != null)
                    count++;
            }

            return count;
        }

        bool AllHumansInScene()
        {
            int expected = ExpectedHumanConnections();
            int present = CountHumanPlayers();
            if (present < Mathf.Max(MinPlayersToStart, expected) || present <= 0)
                return false;

            foreach (var pair in NetworkServer.connections)
            {
                NetworkConnectionToClient conn = pair.Value;
                if (conn == null) continue;
                if (!conn.isReady || conn.identity == null)
                    return false;
            }

            return true;
        }
    }
}
