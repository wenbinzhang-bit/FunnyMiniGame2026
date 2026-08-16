using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Brawl
{
    /// <summary>
    /// MiniGame_01 对局 HUD。刷新已有 UGUI，并在旧 HUD Prefab 缺少时补建本地 Turbo 条。
    /// 血量玩法已移除，旧 Prefab 中残留的 Health 节点会在启动时隐藏。
    /// </summary>
    [DefaultExecutionOrder(100)]
    [ExecuteAlways]
    public sealed class BrawlMatchHud : MonoBehaviour
    {
        const int ScoreBarMaxFallback = 99;
        const string Level01RulesArtworkResource = "UI/Rules/Level01Briefing";
        const string Level02RulesArtworkResource = "UI/Rules/Level02Briefing";
        const string ResultPaperResource = "UI/Results/ResumePaper";
        const float ResultRevealInterval = 0.65f;
        const float ResultRevealDuration = 0.28f;

        static readonly Color[] SlotBarColors =
        {
            new Color(0.20f, 0.66f, 0.92f, 1f),
            new Color(0.26f, 0.72f, 0.40f, 1f),
            new Color(0.94f, 0.72f, 0.12f, 1f),
            new Color(0.84f, 0.32f, 0.24f, 1f)
        };

        [Serializable]
        public sealed class PlayerSlot
        {
            public GameObject Root;
            public Text Name;
            public Text Score;
            public Image BarFill;
            public Image Frame;
            public Image Avatar;
            public Image ReadyMark;
        }

        sealed class RoundResultCard
        {
            public GameObject Root;
            public CanvasGroup Canvas;
            public GameObject Content;
            public Text Name;
            public Text Rank;
            public Text Score;
            public Text Performance;
            public Text TotalKpi;
            public Text Comment;
            public Image Avatar;
            public Image Stamp;
        }

        [Header("Top")]
        public Text TimerText;
        public Text StatusText;
        public PlayerSlot[] Slots = new PlayerSlot[4];

        [Header("Local Turbo")]
        public Text TurboTitle;
        public Text TurboValue;
        public Image TurboFill;

        [Header("Other")]
        public GameObject RankingRoot;
        public Text RankingBody;
        public Text ControlsText;
        public Text CursorHintText;
        public Button NextRoundButton;
        public Text NextRoundLabel;
        public Button LobbyButton;
        public Text LobbyLabel;
        public Button LobbyStartButton;
        public Text LobbyStartLabel;
        public GameObject LobbyReadyRoot;
        public Text LobbyReadyStatus;
        public GameObject LobbyBotRoot;
        public Text LobbyBotValue;
        public Button LobbyBotMinusButton;
        public Button LobbyBotPlusButton;
        public Button LobbyBotAddButton;
        public GameObject LobbyStartConfirmRoot;
        public Text LobbyStartConfirmMessage;
        public Button LobbyStartConfirmYesButton;
        public Button LobbyStartConfirmNoButton;
        public GameObject RulesRoot;
        public Text RulesTitle;
        public Text RulesBody;
        public Text RulesCountdown;
        public RawImage RulesArtwork;
        public Button DebugTimerButton;
        public Text DebugTimerLabel;
        public RectTransform PassCrosshairRoot;
        public Image[] PassCrosshairMarks;

        [Header("Colors")]
        public Color IdleFrameColor = new Color(0.08f, 0.08f, 0.08f, 0.72f);
        public Color HoldingFrameColor = new Color(1f, 0.86f, 0.28f, 0.95f);
        public Color TurboReadyColor = new Color(0.12f, 0.86f, 1f);
        public Color TurboLowColor = new Color(1f, 0.55f, 0.12f);
        public Color TurboEmptyColor = new Color(0.85f, 0.18f, 0.14f);
        public Color TimerNormalColor = Color.white;
        public Color TimerWarningColor = new Color(1f, 0.22f, 0.18f);
        public Color TimerWarningFlashColor = new Color(1f, 0.82f, 0.2f);

        [Header("Countdown Warning")]
        [Min(1f)] public float WarningSeconds = 10f;
        [Min(0.5f)] public float WarningBlinkSpeed = 6f;
        [Range(0f, 1f)] public float BeepVolume = 0.55f;

        readonly List<IBrawlPlayer> hudPlayers = new List<IBrawlPlayer>();
        bool fontApplied;
        AudioSource beepSource;
        AudioClip beepClip;
        int lastBeepSecond = -1;
        bool playedFinalKpiReaction;
        static readonly Dictionary<BrawlRoundResultRules.Grade, AudioClip> resultReactionClips =
            new Dictionary<BrawlRoundResultRules.Grade, AudioClip>();
        Image timerRing;
        Image timerFill;
        Color timerRingBase;
        Color timerFillBase;
        Color statusTextBase;
        bool hasStatusTextBase;
        float displayedTurbo = 1f;
        bool hasDisplayedTurbo;
        string lastHudScene;
        int lastMatchSeq = -1;
        Sprite emptyAvatarSprite;
        int lobbyBotSelection = 1;
        string lobbyTransientStatus;
        float lobbyTransientStatusUntil;
        bool lobbyStartConfirmVisible;
        Image rulesCountdownBackdrop;
        GameObject roundResultRoot;
        Text roundResultTitle;
        Text roundResultCountdown;
        Text roundResultReadyStatus;
        readonly RoundResultCard[] roundResultCards = new RoundResultCard[4];
        Sprite resultPaperSprite;
        readonly Dictionary<BrawlRoundResultRules.Grade, Sprite> resultStampSprites = new Dictionary<BrawlRoundResultRules.Grade, Sprite>();

        void Awake()
        {
            HideLegacyHealthUi();
            EnsureSceneHudWidgets();
            ApplyCjkFont();
            CacheTimerVisuals();
            EnsureBeepSource();
            if (Application.isPlaying && RankingRoot != null)
                RankingRoot.SetActive(false);
        }

        void OnEnable()
        {
            EnsureSceneHudWidgets();
            if (!Application.isPlaying)
                ShowEditorPreviewWidgets();
        }

        void EnsureSceneHudWidgets()
        {
            EnsureTurboVisuals();
            EnsureCursorHint();
            EnsureNextRoundButton();
            EnsureRoundResultPanel();
            EnsureLobbyButton();
            EnsureLobbyReadyPanel();
            EnsureLobbyStartConfirm();
            EnsureRulesPanel();
            EnsureDebugTimerButton();
            EnsurePassCrosshair();
            ApplyHudVisualStyle();
        }

        void ShowEditorPreviewWidgets()
        {
            if (RankingRoot != null)
                RankingRoot.SetActive(true);
            if (NextRoundButton != null)
                NextRoundButton.gameObject.SetActive(true);
            if (LobbyStartButton != null)
                LobbyStartButton.gameObject.SetActive(true);
            if (LobbyButton != null)
                LobbyButton.gameObject.SetActive(false);
            if (LobbyReadyRoot != null)
                LobbyReadyRoot.SetActive(true);
            if (LobbyStartConfirmRoot != null)
                LobbyStartConfirmRoot.SetActive(false);
            if (RulesRoot != null)
                RulesRoot.SetActive(true);
            if (DebugTimerButton != null)
                DebugTimerButton.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                ShowEditorPreviewWidgets();
                return;
            }

            BrawlMatchHud keep = SessionHud();
            if (keep != null && keep != this)
            {
                gameObject.SetActive(false);
                return;
            }

            bool online = NetworkClient.active || NetworkServer.active;
            BrawlGameManager gm = BrawlGameManager.Instance;
            bool showRules = online && gm != null && gm.HudIsShowingRules;
            bool showRoundResult = online && gm != null && (gm.HudIsRoundEnd || gm.HudIsFinalKpi);
            if (ControlsText != null)
                ControlsText.gameObject.SetActive(online && !showRules && !showRoundResult);
            if (!online) return;
            ResetHudIfSceneChanged();
            Refresh();

            if (gm != null && gm.HudShowLobbyActions)
            {
                if (lobbyStartConfirmVisible)
                {
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                        OnLobbyStartConfirmYesClicked();
                    else if (Input.GetKeyDown(KeyCode.Escape))
                        OnLobbyStartConfirmNoClicked();
                }
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    if (gm.HudIsHost)
                        OnLobbyStartClicked();
                    else
                        gm.RequestLobbyReadyToggle();
                }
            }

            if (gm != null && gm.HudIsRoundEnd && !gm.HudContinueRequested
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.N)))
                gm.RequestNextRound();

            if (gm != null && gm.HudIsPlaying && Input.GetKeyDown(KeyCode.F9))
                gm.DebugSetRemainingSeconds(10f);
        }

        void ResetHudIfSceneChanged()
        {
            string scene = BrawlLevelCatalog.ActiveSceneName();
            BrawlGameManager gm = BrawlGameManager.Instance;
            int seq = gm != null ? gm.HudMatchSeq : 0;
            if (scene == lastHudScene && seq == lastMatchSeq) return;
            lastHudScene = scene;
            lastMatchSeq = seq;
            lastBeepSecond = -1;
            hasDisplayedTurbo = false;
            if (RankingRoot != null)
                RankingRoot.SetActive(false);
            if (NextRoundButton != null)
                NextRoundButton.gameObject.SetActive(false);
            if (LobbyButton != null)
                LobbyButton.gameObject.SetActive(false);
            if (LobbyStartButton != null)
                LobbyStartButton.gameObject.SetActive(false);
            HideLobbyStartConfirm();
            if (RulesRoot != null)
                RulesRoot.SetActive(false);
            if (roundResultRoot != null)
                roundResultRoot.SetActive(false);
            HideForeignMatchHuds();
        }

        void HideForeignMatchHuds()
        {
            BrawlMatchHud keep = SessionHud();
            BrawlMatchHud[] huds = FindObjectsOfType<BrawlMatchHud>(true);
            for (int i = 0; i < huds.Length; i++)
            {
                if (huds[i] == null) continue;
                if (huds[i] == keep)
                {
                    if (!huds[i].gameObject.activeSelf)
                        huds[i].gameObject.SetActive(true);
                    continue;
                }

                huds[i].gameObject.SetActive(false);
            }
        }

        static BrawlMatchHud SessionHud()
        {
            if (BrawlSession.Instance != null)
            {
                BrawlMatchHud sessionHud = BrawlSession.Instance.GetComponentInChildren<BrawlMatchHud>(true);
                if (sessionHud != null)
                    return sessionHud;
            }

            return FindObjectOfType<BrawlMatchHud>();
        }

        void Refresh()
        {
            BrawlGameManager gm = BrawlGameManager.Instance;
            int scoreMax = gm != null ? Mathf.Max(1, gm.HudScoreMax) : ScoreBarMaxFallback;
            CollectPlayers();

            float remaining = gm != null ? gm.HudRemainingSeconds : 0f;
            if (TimerText != null)
                TimerText.text = FormatTime(remaining);

            ApplyTimerWarning(gm != null && (gm.HudIsPlaying || (gm.HudIsWaiting && remaining > 0f) || gm.HudIsRoundEnd), remaining);

            bool launcherLobby = gm != null && gm.HudIsLobby && BrawlLevelCatalog.ActiveSceneIsLauncher();
            if (StatusText != null)
            {
                StatusText.gameObject.SetActive(!launcherLobby && (gm == null || (!gm.HudIsRoundEnd && !gm.HudIsFinalKpi)));
                if (gm != null && gm.HudIsLobby)
                    StatusText.text = string.IsNullOrEmpty(gm.HudStatusText)
                        ? "大厅等待加入，房主点击开始进入第一关"
                        : TrimStatus(gm.HudStatusText);
                else if (gm != null && gm.HudIsShowingRules)
                    StatusText.text = $"请阅读{gm.HudRulesTitle}，{Mathf.CeilToInt(remaining)} 秒后开始";
                else if (gm != null && gm.HudIsWaiting)
                    StatusText.text = string.IsNullOrEmpty(gm.HudStatusText)
                        ? "空气墙等待中，结束后正式开始"
                        : TrimStatus(gm.HudStatusText);
                else if (gm != null && gm.HudIsFinalKpi)
                    StatusText.text = "3 关全部结束，这是整场 KPI 汇总";
                else if (gm != null && gm.HudIsRoundEnd)
                    StatusText.text = gm.HudContinueRequested
                        ? (gm.HudHasNextLevel ? "已确认下一关" : "已确认查看总成绩")
                        : $"点击「{(gm.HudHasNextLevel ? "下一关" : "查看总成绩")}」或按 Enter 继续，否则 {FormatTime(remaining)} 后自动继续";
                else
                    StatusText.text = TrimStatus(gm != null ? gm.HudStatusText : "");

                ApplyPassTheBuckDumpStatusColor(gm != null && gm.HudIsPlaying && gm.IsPassTheBuckDumpPhase);
            }

            int slotCount = Slots != null ? Slots.Length : 0;
            for (int i = 0; i < slotCount; i++)
                BindSlot(Slots[i], i, i < hudPlayers.Count ? hudPlayers[i] : null, scoreMax);

            BindLocalTurbo();
            BindRanking(gm);
            BindRoundResultPanel(gm);
            BindNextRoundButton(gm);
            BindLobbyButton(gm);
            BindRulesPanel(gm);
            BindDebugTimerButton(gm);
            BindCursorHint();
            BindPassCrosshair(gm);
            if (NextRoundButton != null && NextRoundButton.gameObject.activeSelf)
                NextRoundButton.transform.SetAsLastSibling();
            if (roundResultRoot != null && roundResultRoot.activeSelf)
                roundResultRoot.transform.SetAsLastSibling();
        }

        void BindSlot(PlayerSlot slot, int index, IBrawlPlayer player, int scoreMax)
        {
            if (slot == null) return;
            if (slot.Root != null) slot.Root.SetActive(true);

            EnsureSlotVisualReferences(slot);

            int score = player != null ? player.Score : 0;
            if (slot.Name != null)
            {
                string label = player != null ? BrawlHudNames.Label(player.NetId, hudPlayers) : $"Player {index + 1}";
                if (player != null && player.IsDead)
                    label += " 淘汰";
                slot.Name.text = label;
            }
            if (slot.Score != null) slot.Score.text = $"{score}/{scoreMax}";
            if (slot.BarFill != null)
            {
                if (index >= 0 && index < SlotBarColors.Length)
                    slot.BarFill.color = SlotBarColors[index];
                RectTransform fill = slot.BarFill.rectTransform;
                fill.anchorMin = new Vector2(0f, 0f);
                fill.anchorMax = new Vector2(Mathf.Clamp01(score / (float)scoreMax), 1f);
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;
            }

            if (slot.Frame != null)
            {
                bool holding = player is NetFAnnequinController fan && fan.IsHoldingComputer;
                bool eliminated = player != null && player.IsDead;
                slot.Frame.color = eliminated
                    ? new Color(0.25f, 0.25f, 0.25f, 0.85f)
                    : holding ? HoldingFrameColor : IdleFrameColor;
            }

            if (slot.Avatar != null)
            {
                if (player == null)
                {
                    slot.Avatar.sprite = ResolveEmptyAvatarSprite(slot);
                    slot.Avatar.color = new Color(0.68f, 0.70f, 0.74f, 1f);
                }
                else
                {
                    Sprite avatar = ResolvePlayerAvatar(player, index);
                    if (avatar != null)
                        slot.Avatar.sprite = avatar;
                    slot.Avatar.color = Color.white;
                }
                slot.Avatar.preserveAspect = true;
            }

            BindSlotReadyMark(slot, player);
        }

        void BindSlotReadyMark(PlayerSlot slot, IBrawlPlayer player)
        {
            if (player == null)
            {
                if (slot?.ReadyMark != null)
                    slot.ReadyMark.gameObject.SetActive(false);
                return;
            }

            Image mark = EnsureReadyMark(slot);
            if (mark == null) return;

            bool lobby = BrawlLevelCatalog.ActiveSceneIsLauncher()
                && BrawlGameManager.Instance != null
                && BrawlGameManager.Instance.HudIsLobby;
            bool ready = lobby && player is NetFAnnequinController fan && fan.LobbyReady;
            mark.gameObject.SetActive(ready);
        }

        Image EnsureReadyMark(PlayerSlot slot)
        {
            if (slot == null) return null;
            if (slot.ReadyMark != null) return slot.ReadyMark;
            if (slot.Avatar == null && slot.Root != null)
                slot.Avatar = slot.Root.transform.Find("Avatar")?.GetComponent<Image>();
            if (slot.Avatar == null) return null;

            Transform existing = slot.Avatar.transform.Find("ReadyMark");
            Image mark = existing != null ? existing.GetComponent<Image>() : null;
            if (mark == null)
            {
                GameObject markObject = new GameObject("ReadyMark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                markObject.transform.SetParent(slot.Avatar.transform, false);
                mark = markObject.GetComponent<Image>();
                mark.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
                mark.raycastTarget = false;

                Font font = TimerText != null && TimerText.font != null
                    ? TimerText.font
                    : Resources.GetBuiltinResource<Font>("Arial.ttf");
                Text check = CreatePlainText(markObject.transform, "Check", font, 16, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
                SetHudRect(check.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero);
                check.text = "✓";
                check.color = Color.white;
            }

            mark.color = new Color(0.18f, 0.72f, 0.28f, 0.96f);
            EnsureGraphicOutline(mark, new Color(0.04f, 0.08f, 0.04f, 0.9f), 1f);
            SetHudRect(mark.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(20f, 20f));
            mark.transform.SetAsLastSibling();
            slot.ReadyMark = mark;
            return mark;
        }

        static Sprite ResolvePlayerAvatar(IBrawlPlayer player, int fallbackIndex)
        {
            BrawlCharacterCatalog catalog = BrawlCharacterCatalog.Load();
            return catalog != null
                ? catalog.ResolveAvatar(player != null ? player.Transform : null, fallbackIndex)
                : null;
        }

        Sprite ResolveEmptyAvatarSprite(PlayerSlot slot)
        {
            if (emptyAvatarSprite == null)
                emptyAvatarSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            if (emptyAvatarSprite == null && slot?.Avatar != null)
                emptyAvatarSprite = slot.Avatar.sprite;
            return emptyAvatarSprite;
        }

        void EnsureSlotVisualReferences(PlayerSlot slot)
        {
            if (slot?.Root == null) return;
            Transform root = slot.Root.transform;
            if (slot.Avatar == null) slot.Avatar = root.Find("Avatar")?.GetComponent<Image>();
            if (slot.ReadyMark == null && slot.Avatar != null)
                slot.ReadyMark = slot.Avatar.transform.Find("ReadyMark")?.GetComponent<Image>();
            if (slot.Frame == null) slot.Frame = root.Find("Frame")?.GetComponent<Image>();
            if (slot.Name == null) slot.Name = root.Find("Name")?.GetComponent<Text>();
            if (slot.Score == null) slot.Score = root.Find("Score")?.GetComponent<Text>();
            if (slot.BarFill == null) slot.BarFill = root.Find("BarBack/BarFill")?.GetComponent<Image>();
        }

        void BindLocalTurbo()
        {
            NetFAnnequinController local = null;
            for (int i = 0; i < hudPlayers.Count; i++)
            {
                if (hudPlayers[i] is NetFAnnequinController fan && fan.isLocalPlayer)
                {
                    local = fan;
                    break;
                }
            }

            if (TurboTitle != null) TurboTitle.text = "SHIFT TURBO";
            if (local == null)
            {
                if (TurboValue != null) TurboValue.text = "--";
                SetFill(TurboFill, 0f);
                hasDisplayedTurbo = false;
                return;
            }

            float target = local.TurboNormalized;
            if (!hasDisplayedTurbo)
            {
                displayedTurbo = target;
                hasDisplayedTurbo = true;
            }
            else
            {
                // 网络值按发送间隔更新，HUD 做一层快速平滑，避免进度条阶梯式跳动。
                displayedTurbo = Mathf.MoveTowards(displayedTurbo, target, Time.unscaledDeltaTime * 2f);
            }

            if (TurboValue != null)
                TurboValue.text = target <= 0.001f ? "EMPTY" : $"{local.TurboRemainingSeconds:0.0}s";
            if (TurboFill != null)
            {
                TurboFill.color = target <= 0.001f
                    ? TurboEmptyColor
                    : target <= 0.25f ? TurboLowColor : TurboReadyColor;
                SetFill(TurboFill, displayedTurbo);
            }
        }

        void EnsureRoundResultPanel()
        {
            if (roundResultRoot != null && roundResultCards[0] != null) return;

            Transform stale = transform.Find("RoundResult");
            if (stale != null)
            {
                if (Application.isPlaying) Destroy(stale.gameObject);
                else DestroyImmediate(stale.gameObject);
            }
            if (NextRoundButton == null || NextRoundLabel == null)
                EnsureNextRoundButton();

            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
            resultPaperSprite = Resources.Load<Sprite>(ResultPaperResource);

            GameObject rootObject = new GameObject("RoundResult", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform root = rootObject.GetComponent<RectTransform>();
            root.SetParent(transform, false);
            SetHudRect(root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image backdrop = rootObject.GetComponent<Image>();
            backdrop.color = new Color(0.012f, 0.016f, 0.022f, 0.985f);
            backdrop.raycastTarget = true;
            roundResultRoot = rootObject;

            CreateResultPanelImage(root, "InnerFrame", new Vector2(0.008f, 0.012f), new Vector2(0.992f, 0.988f), new Color(0.36f, 0.34f, 0.28f, 0.82f));
            CreateResultPanelImage(root, "InnerFill", new Vector2(0.011f, 0.016f), new Vector2(0.989f, 0.984f), new Color(0.018f, 0.024f, 0.032f, 1f));
            CreateResultPanelImage(root, "TitleBar", new Vector2(0.03f, 0.865f), new Vector2(0.97f, 0.965f), new Color(0.015f, 0.025f, 0.18f, 0.96f));
            CreateResultPanelImage(root, "TitleTopLine", new Vector2(0.03f, 0.961f), new Vector2(0.97f, 0.967f), new Color(0.72f, 0.66f, 0.48f, 0.92f));
            CreateResultPanelImage(root, "TitleBottomLine", new Vector2(0.03f, 0.859f), new Vector2(0.97f, 0.865f), new Color(0.72f, 0.66f, 0.48f, 0.92f));

            roundResultTitle = CreateResultText(root, "Title", fallbackFont, 40, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.94f, 0.90f, 0.78f, 1f), true);
            SetHudRect(roundResultTitle.rectTransform, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.96f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            roundResultTitle.text = "第一关：绩效考核";

            float[] left = { 0.035f, 0.272f, 0.509f, 0.746f };
            const float cardWidth = 0.219f;
            for (int i = 0; i < roundResultCards.Length; i++)
                roundResultCards[i] = CreateRoundResultCard(root, fallbackFont, i, left[i], left[i] + cardWidth);

            roundResultReadyStatus = CreateResultText(root, "ReadyStatus", fallbackFont, 16, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.88f, 0.86f, 0.78f, 0.96f), true);
            SetHudRect(roundResultReadyStatus.rectTransform, new Vector2(0.38f, 0.165f), new Vector2(0.62f, 0.205f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            roundResultReadyStatus.text = "已确认 0/0";

            if (NextRoundButton != null)
            {
                RectTransform buttonRect = NextRoundButton.transform as RectTransform;
                buttonRect.SetParent(root, false);
                SetHudRect(buttonRect, new Vector2(0.38f, 0.075f), new Vector2(0.62f, 0.155f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Image buttonImage = NextRoundButton.GetComponent<Image>();
                if (buttonImage != null)
                {
                    buttonImage.color = new Color(0.88f, 0.63f, 0.03f, 1f);
                    EnsureGraphicOutline(buttonImage, new Color(0.58f, 0.52f, 0.38f, 1f), 2f);
                }
                if (NextRoundLabel != null)
                {
                    NextRoundLabel.fontSize = 28;
                    NextRoundLabel.color = new Color(0.04f, 0.04f, 0.035f, 1f);
                }
            }

            roundResultCountdown = CreateResultText(root, "Countdown", fallbackFont, 23, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.20f, 1f), true);
            SetHudRect(roundResultCountdown.rectTransform, new Vector2(0.30f, 0.015f), new Vector2(0.70f, 0.07f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            roundResultCountdown.text = "15s后自动进入下一关";
            rootObject.SetActive(false);
        }

        RoundResultCard CreateRoundResultCard(RectTransform parent, Font font, int index, float minX, float maxX)
        {
            var card = new RoundResultCard();
            GameObject cardObject = new GameObject($"Resume{index + 1}", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform cardRect = cardObject.GetComponent<RectTransform>();
            cardRect.SetParent(parent, false);
            SetHudRect(cardRect, new Vector2(minX, 0.22f), new Vector2(maxX, 0.83f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            card.Root = cardObject;
            card.Canvas = cardObject.GetComponent<CanvasGroup>();
            card.Canvas.interactable = false;
            card.Canvas.blocksRaycasts = false;

            Image paper = CreateResultPanelImage(cardRect, "Paper", Vector2.zero, Vector2.one, Color.white);
            paper.sprite = resultPaperSprite;
            paper.preserveAspect = true;

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(cardRect, false);
            SetHudRect(content, new Vector2(0.08f, 0.055f), new Vector2(0.92f, 0.94f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            card.Content = contentObject;

            Text header = CreateResultText(content, "Header", font, 21, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.08f, 0.07f, 0.055f, 1f), false);
            SetHudRect(header.rectTransform, new Vector2(0.20f, 0.91f), new Vector2(0.80f, 0.99f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            header.text = "员工简历";
            CreateResultPanelImage(content, "HeaderLine", new Vector2(0.22f, 0.895f), new Vector2(0.78f, 0.900f), new Color(0.36f, 0.31f, 0.23f, 0.55f));

            card.Avatar = CreateResultPanelImage(content, "Avatar", new Vector2(0.10f, 0.65f), new Vector2(0.46f, 0.88f), Color.white);
            card.Avatar.preserveAspect = true;

            card.Name = CreateResultText(content, "PlayerName", font, 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.12f, 0.10f, 0.07f, 0.94f), false);
            SetHudRect(card.Name.rectTransform, new Vector2(0.48f, 0.82f), new Vector2(0.92f, 0.89f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            card.Name.horizontalOverflow = HorizontalWrapMode.Overflow;

            card.Rank = CreateResultText(content, "Rank", font, 21, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.08f, 0.07f, 0.055f, 1f), false);
            SetHudRect(card.Rank.rectTransform, new Vector2(0.48f, 0.70f), new Vector2(0.94f, 0.82f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

            CreateResultPanelImage(content, "AvatarLine", new Vector2(0.08f, 0.63f), new Vector2(0.92f, 0.636f), new Color(0.38f, 0.33f, 0.25f, 0.42f));
            card.Score = CreateResultText(content, "Score", font, 19, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.08f, 0.07f, 0.055f, 1f), false);
            SetHudRect(card.Score.rectTransform, new Vector2(0.08f, 0.565f), new Vector2(0.92f, 0.635f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            card.Performance = CreateResultText(content, "Performance", font, 17, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.10f, 0.08f, 0.06f, 0.92f), false);
            SetHudRect(card.Performance.rectTransform, new Vector2(0.08f, 0.505f), new Vector2(0.92f, 0.57f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

            card.Stamp = CreateResultPanelImage(content, "Stamp", new Vector2(0.18f, 0.235f), new Vector2(0.82f, 0.51f), Color.white);
            card.Stamp.preserveAspect = true;

            CreateResultPanelImage(content, "KpiLine", new Vector2(0.08f, 0.218f), new Vector2(0.92f, 0.224f), new Color(0.38f, 0.33f, 0.25f, 0.42f));
            card.TotalKpi = CreateResultText(content, "TotalKpi", font, 23, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.73f, 0.52f, 0.02f, 1f), false);
            SetHudRect(card.TotalKpi.rectTransform, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.215f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            card.Comment = CreateResultText(content, "Comment", font, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.10f, 0.08f, 0.06f, 0.96f), false);
            SetHudRect(card.Comment.rectTransform, new Vector2(0.08f, 0.065f), new Vector2(0.92f, 0.145f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            CreateResultPanelImage(content, "CommentUnderline", new Vector2(0.08f, 0.055f), new Vector2(0.92f, 0.060f), new Color(0.18f, 0.14f, 0.09f, 0.72f));
            return card;
        }

        static Image CreateResultPanelImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetHudRect(rect, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text CreateResultText(Transform parent, string name, Font font, int size, FontStyle style, TextAnchor alignment, Color color, bool outline)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            if (outline)
            {
                Outline textOutline = go.AddComponent<Outline>();
                textOutline.effectColor = new Color(0f, 0f, 0f, 0.82f);
                textOutline.effectDistance = new Vector2(1f, -1f);
            }
            return text;
        }

        Sprite ResultStampSprite(BrawlRoundResultRules.Grade grade)
        {
            if (resultStampSprites.TryGetValue(grade, out Sprite loaded) && loaded != null)
                return loaded;
            loaded = Resources.Load<Sprite>(BrawlRoundResultRules.StampResource(grade));
            resultStampSprites[grade] = loaded;
            return loaded;
        }

        void BindRoundResultPanel(BrawlGameManager gm)
        {
            bool final = gm != null && gm.HudIsFinalKpi;
            bool show = gm != null && (gm.HudIsRoundEnd || final);
            if (!final)
                playedFinalKpiReaction = false;
            if (roundResultRoot != null)
                roundResultRoot.SetActive(show);
            if (!show || roundResultRoot == null) return;

            roundResultRoot.transform.SetAsLastSibling();
            if (roundResultTitle != null)
                roundResultTitle.text = final
                    ? "整场 KPI 汇总"
                    : $"{BrawlLevelCatalog.GetLevelTitle(BrawlLevelCatalog.ActiveSceneName())}：绩效考核";

            var ordered = new List<IBrawlPlayer>(hudPlayers);
            ordered.Sort((a, b) =>
            {
                int cmp = ResultSortScore(gm, a, final).CompareTo(ResultSortScore(gm, b, final));
                return cmp != 0 ? -cmp : a.NetId.CompareTo(b.NetId);
            });
            if (ordered.Count > roundResultCards.Length)
                ordered.RemoveRange(roundResultCards.Length, ordered.Count - roundResultCards.Length);

            float elapsed = gm.HudRoundResultElapsedSeconds;
            for (int i = 0; i < roundResultCards.Length; i++)
            {
                RoundResultCard card = roundResultCards[i];
                if (card?.Root == null) continue;
                bool occupied = i < ordered.Count;
                if (card.Content != null)
                    card.Content.SetActive(occupied);

                if (!occupied)
                {
                    card.Canvas.alpha = 0.64f;
                    card.Root.transform.localScale = Vector3.one;
                    continue;
                }

                IBrawlPlayer player = ordered[i];
                BrawlRoundResultRules.Grade grade = BrawlRoundResultRules.ResolveGrade(i, ordered.Count);
                string playerName = BrawlHudNames.Label(player.NetId, hudPlayers);
                if (card.Name != null) card.Name.text = playerName;
                if (card.Stamp != null) card.Stamp.sprite = ResultStampSprite(grade);
                if (card.Comment != null) card.Comment.text = $"评语：{BrawlRoundResultRules.Comment(grade)}";
                if (card.Avatar != null)
                {
                    card.Avatar.sprite = ResolvePlayerAvatar(player, i);
                    card.Avatar.color = Color.white;
                }

                if (final)
                    BindFinalKpiCard(card, gm, player, i);
                else
                    BindRoundKpiCard(card, gm, player, i);

                float revealStart = i * ResultRevealInterval;
                float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - revealStart) / ResultRevealDuration));
                card.Canvas.alpha = reveal;
                card.Root.transform.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, reveal);
            }

            if (roundResultReadyStatus != null)
            {
                roundResultReadyStatus.gameObject.SetActive(!final);
                if (!final)
                    roundResultReadyStatus.text = $"已确认 {gm.HudRoundContinueReadyCount}/{gm.HudRoundContinueHumanCount}";
            }

            if (roundResultCountdown != null)
            {
                RectTransform countdownRect = roundResultCountdown.rectTransform;
                if (final)
                {
                    SetHudRect(countdownRect, new Vector2(0.30f, 0.075f), new Vector2(0.70f, 0.155f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    roundResultCountdown.fontSize = 28;
                    roundResultCountdown.text = "3 关全部结束";
                }
                else
                {
                    SetHudRect(countdownRect, new Vector2(0.30f, 0.015f), new Vector2(0.70f, 0.07f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    roundResultCountdown.fontSize = 23;
                    int seconds = Mathf.Max(0, Mathf.CeilToInt(gm.HudRemainingSeconds));
                    string destination = gm.HudHasNextLevel ? "下一关" : "总成绩";
                    roundResultCountdown.text = $"{seconds}s后自动进入{destination}";
                }
            }

            TryPlayFinalResultReaction(gm, ordered, final);
        }

        void TryPlayFinalResultReaction(BrawlGameManager gm, List<IBrawlPlayer> ordered, bool final)
        {
            if (!final || playedFinalKpiReaction || gm == null || ordered == null)
                return;

            NetFAnnequinController local = FindLocalPlayer();
            if (local == null) return;

            int rank = -1;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i] != null && ordered[i].NetId == local.NetId)
                {
                    rank = i;
                    break;
                }
            }

            if (rank < 0) return;
            if (gm.HudRoundResultElapsedSeconds < rank * ResultRevealInterval)
                return;

            playedFinalKpiReaction = true;
            PlayResultReaction(BrawlRoundResultRules.ResolveGrade(rank, ordered.Count));
        }

        void PlayResultReaction(BrawlRoundResultRules.Grade grade)
        {
            EnsureBeepSource();
            if (beepSource == null) return;
            if (!resultReactionClips.TryGetValue(grade, out AudioClip clip) || clip == null)
            {
                clip = CreateResultReactionClip(grade);
                resultReactionClips[grade] = clip;
            }

            beepSource.pitch = 1f;
            beepSource.PlayOneShot(clip, 0.85f);
        }

        static AudioClip CreateResultReactionClip(BrawlRoundResultRules.Grade grade)
        {
            switch (grade)
            {
                case BrawlRoundResultRules.Grade.S:
                    return CreateCheerClip();
                case BrawlRoundResultRules.Grade.AMinus:
                    return CreatePraiseClip();
                case BrawlRoundResultRules.Grade.BPlus:
                    return CreateEncourageClip();
                default:
                    return CreateSympathyClip();
            }
        }

        static AudioClip CreateCheerClip()
        {
            return ComposeSting("ResultCheer", 1.05f, new[]
            {
                new Tone(523.25f, 0.00f, 0.22f, 0.28f),
                new Tone(659.25f, 0.10f, 0.22f, 0.26f),
                new Tone(783.99f, 0.20f, 0.24f, 0.26f),
                new Tone(1046.5f, 0.32f, 0.42f, 0.32f),
                new Tone(1318.5f, 0.40f, 0.28f, 0.16f)
            }, crowd: 0.22f, descend: false);
        }

        static AudioClip CreatePraiseClip()
        {
            return ComposeSting("ResultPraise", 0.72f, new[]
            {
                new Tone(523.25f, 0.00f, 0.20f, 0.26f),
                new Tone(659.25f, 0.12f, 0.22f, 0.24f),
                new Tone(783.99f, 0.24f, 0.32f, 0.28f)
            }, crowd: 0.10f, descend: false);
        }

        static AudioClip CreateEncourageClip()
        {
            return ComposeSting("ResultEncourage", 0.7f, new[]
            {
                new Tone(392.00f, 0.00f, 0.22f, 0.24f),
                new Tone(523.25f, 0.16f, 0.36f, 0.26f)
            }, crowd: 0.04f, descend: false);
        }

        static AudioClip CreateSympathyClip()
        {
            return ComposeSting("ResultSympathy", 0.85f, new[]
            {
                new Tone(392.00f, 0.00f, 0.24f, 0.24f),
                new Tone(329.63f, 0.16f, 0.26f, 0.22f),
                new Tone(246.94f, 0.34f, 0.42f, 0.26f)
            }, crowd: 0f, descend: true);
        }

        readonly struct Tone
        {
            public readonly float Freq;
            public readonly float Start;
            public readonly float Duration;
            public readonly float Volume;

            public Tone(float freq, float start, float duration, float volume)
            {
                Freq = freq;
                Start = start;
                Duration = duration;
                Volume = volume;
            }
        }

        static AudioClip ComposeSting(string name, float duration, Tone[] tones, float crowd, bool descend)
        {
            const int sampleRate = 22050;
            int samples = Mathf.RoundToInt(sampleRate * duration);
            var data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float sample = 0f;
                for (int n = 0; n < tones.Length; n++)
                {
                    Tone tone = tones[n];
                    float local = t - tone.Start;
                    if (local < 0f || local > tone.Duration) continue;
                    float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(local / tone.Duration));
                    float freq = tone.Freq;
                    if (descend)
                        freq *= Mathf.Lerp(1f, 0.82f, local / tone.Duration);
                    sample += Mathf.Sin(2f * Mathf.PI * freq * t) * env * tone.Volume;
                }

                if (crowd > 0f)
                {
                    float burst = Mathf.Exp(-t * 3.2f);
                    sample += (float)(rng.NextDouble() * 2.0 - 1.0) * burst * crowd;
                }

                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static int ResultSortScore(BrawlGameManager gm, IBrawlPlayer player, bool final)
        {
            if (player == null) return 0;
            if (final && gm != null && gm.TryHudFinalKpi(player.NetId, out _, out int total))
                return total;
            return player.Score;
        }

        static void BindRoundKpiCard(RoundResultCard card, BrawlGameManager gm, IBrawlPlayer player, int rankIndex)
        {
            if (card.Rank != null) card.Rank.text = $"本关排名：{rankIndex + 1}";
            if (card.Score != null) card.Score.text = $"本关得分：+{Mathf.Max(0, player.Score)}分";
            if (card.Performance != null) card.Performance.text = "当季绩效：";
            if (card.TotalKpi != null)
                card.TotalKpi.text = $"综合KPI：{gm.HudRoundTotalKpi(player.NetId, player.Score)}分";
        }

        static void BindFinalKpiCard(RoundResultCard card, BrawlGameManager gm, IBrawlPlayer player, int rankIndex)
        {
            int total = player.Score;
            int[] levelScores = { Mathf.Max(0, player.Score) };
            if (gm.TryHudFinalKpi(player.NetId, out int[] snapshotScores, out int snapshotTotal))
            {
                levelScores = snapshotScores;
                total = snapshotTotal;
            }
            else
            {
                total = gm.HudRoundTotalKpi(player.NetId, player.Score);
            }

            if (card.Rank != null) card.Rank.text = $"总排名：{rankIndex + 1}";
            SplitLevelScoreLines(levelScores, out string scoreLine, out string performanceLine);
            if (card.Score != null) card.Score.text = scoreLine;
            if (card.Performance != null) card.Performance.text = performanceLine;
            if (card.TotalKpi != null) card.TotalKpi.text = $"综合KPI：{total}分";
        }

        static void SplitLevelScoreLines(int[] levelScores, out string scoreLine, out string performanceLine)
        {
            if (levelScores == null || levelScores.Length == 0)
            {
                scoreLine = "各关得分：暂无";
                performanceLine = "";
                return;
            }

            var lines = new List<string>(levelScores.Length);
            for (int i = 0; i < levelScores.Length; i++)
                lines.Add($"第{i + 1}关 +{Mathf.Max(0, levelScores[i])}分");

            if (lines.Count == 1)
            {
                scoreLine = lines[0];
                performanceLine = "";
                return;
            }

            scoreLine = lines[0];
            performanceLine = string.Join("    ", lines.GetRange(1, lines.Count - 1));
        }

        void EnsureTurboVisuals()
        {
            if (TurboTitle != null && TurboValue != null && TurboFill != null)
            {
                PositionTurboPanel(TurboTitle.transform);
                return;
            }

            Transform existing = transform.Find("Turbo");
            if (existing != null)
            {
                TurboTitle = existing.Find("Title")?.GetComponent<Text>();
                TurboValue = existing.Find("BarBack/Value")?.GetComponent<Text>();
                TurboFill = existing.Find("BarBack/Fill")?.GetComponent<Image>();
                if (TurboTitle != null && TurboValue != null && TurboFill != null)
                {
                    PositionTurboPanel(existing);
                    return;
                }
            }

            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject panelObject = new GameObject("Turbo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(transform, false);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(24f, -168f);
            panel.sizeDelta = new Vector2(268f, 48f);
            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.72f);
            panelImage.raycastTarget = false;

            GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(panel, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.anchoredPosition = new Vector2(10f, 0f);
            titleRect.sizeDelta = new Vector2(96f, 0f);
            TurboTitle = titleObject.GetComponent<Text>();
            TurboTitle.font = fallbackFont;
            TurboTitle.fontSize = 17;
            TurboTitle.fontStyle = FontStyle.Bold;
            TurboTitle.alignment = TextAnchor.MiddleLeft;
            TurboTitle.color = Color.white;
            TurboTitle.raycastTarget = false;
            TurboTitle.text = "SHIFT TURBO";

            GameObject backObject = new GameObject("BarBack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform back = backObject.GetComponent<RectTransform>();
            back.SetParent(panel, false);
            back.anchorMin = new Vector2(0f, 0.5f);
            back.anchorMax = new Vector2(0f, 0.5f);
            back.pivot = new Vector2(0f, 0.5f);
            back.anchoredPosition = new Vector2(106f, 0f);
            back.sizeDelta = new Vector2(150f, 20f);
            Image backImage = backObject.GetComponent<Image>();
            backImage.color = new Color(0.12f, 0.12f, 0.12f, 0.96f);
            backImage.raycastTarget = false;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform fill = fillObject.GetComponent<RectTransform>();
            fill.SetParent(back, false);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);
            TurboFill = fillObject.GetComponent<Image>();
            TurboFill.color = TurboReadyColor;
            TurboFill.raycastTarget = false;

            GameObject valueObject = new GameObject("Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform valueRect = valueObject.GetComponent<RectTransform>();
            valueRect.SetParent(back, false);
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            TurboValue = valueObject.GetComponent<Text>();
            TurboValue.font = fallbackFont;
            TurboValue.fontSize = 13;
            TurboValue.fontStyle = FontStyle.Bold;
            TurboValue.alignment = TextAnchor.MiddleCenter;
            TurboValue.color = Color.white;
            TurboValue.raycastTarget = false;
            TurboValue.text = "5.0s";
            Outline outline = valueObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
            PositionTurboPanel(panel);
        }

        void BindNextRoundButton(BrawlGameManager gm)
        {
            bool show = gm != null && gm.HudIsRoundEnd && !gm.HudIsFinalKpi;
            if (NextRoundButton != null)
                NextRoundButton.gameObject.SetActive(show);
            if (!show || NextRoundLabel == null) return;

            bool localReady = gm.HudContinueRequested;
            NextRoundButton.interactable = !localReady;
            Image image = NextRoundButton.GetComponent<Image>();
            if (image != null)
                image.color = localReady
                    ? new Color(0.34f, 0.33f, 0.29f, 0.96f)
                    : new Color(0.88f, 0.63f, 0.03f, 1f);
            NextRoundButton.transform.SetAsLastSibling();
            NextRoundLabel.text = localReady
                ? "已确认"
                : gm.HudHasNextLevel ? "下一关" : "查看总成绩";
        }

        void OnNextRoundClicked()
        {
            Debug.Log("BRAWL_SMOKE: NEXT_ROUND_BUTTON_CLICKED");
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.RequestNextRound();
        }

        void BindDebugTimerButton(BrawlGameManager gm)
        {
            bool show = gm != null && gm.HudIsPlaying;
            if (DebugTimerButton != null)
                DebugTimerButton.gameObject.SetActive(show);
            if (!show) return;
            if (DebugTimerLabel != null)
                DebugTimerLabel.text = "当局剩30秒";
            DebugTimerButton.transform.SetAsLastSibling();
        }

        void OnDebugTimerClicked()
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.DebugSetRemainingSeconds(30f);
        }

        void EnsureDebugTimerButton()
        {
            if (DebugTimerButton != null && DebugTimerLabel != null) return;

            Transform existing = transform.Find("DebugTimer");
            if (existing != null)
            {
                DebugTimerButton = existing.GetComponent<Button>();
                DebugTimerLabel = existing.Find("Label")?.GetComponent<Text>();
                if (DebugTimerButton != null && DebugTimerLabel != null)
                {
                    DebugTimerButton.onClick.RemoveListener(OnDebugTimerClicked);
                    DebugTimerButton.onClick.AddListener(OnDebugTimerClicked);
                    DebugTimerButton.gameObject.SetActive(false);
                    return;
                }
            }

            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject buttonObject = new GameObject("DebugTimer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(transform, false);
            buttonRect.anchorMin = new Vector2(1f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = new Vector2(-24f, -24f);
            buttonRect.sizeDelta = new Vector2(168f, 40f);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.72f, 0.28f, 0.12f, 0.92f);
            image.raycastTarget = true;
            DebugTimerButton = buttonObject.GetComponent<Button>();
            DebugTimerButton.onClick.AddListener(OnDebugTimerClicked);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            DebugTimerLabel = labelObject.GetComponent<Text>();
            DebugTimerLabel.font = fallbackFont;
            DebugTimerLabel.fontSize = 16;
            DebugTimerLabel.fontStyle = FontStyle.Bold;
            DebugTimerLabel.alignment = TextAnchor.MiddleCenter;
            DebugTimerLabel.color = Color.white;
            DebugTimerLabel.raycastTarget = false;
            DebugTimerLabel.text = "当局剩30秒";
            Outline outline = labelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(1f, -1f);
            buttonObject.SetActive(false);
        }

        void EnsureNextRoundButton()
        {
            if (NextRoundButton != null && NextRoundLabel != null) return;

            Transform existing = transform.Find("NextRound");
            if (existing != null)
            {
                NextRoundButton = existing.GetComponent<Button>();
                NextRoundLabel = existing.Find("Label")?.GetComponent<Text>();
                if (NextRoundButton != null && NextRoundLabel != null)
                {
                    NextRoundButton.onClick.RemoveListener(OnNextRoundClicked);
                    NextRoundButton.onClick.AddListener(OnNextRoundClicked);
                    NextRoundButton.gameObject.SetActive(false);
                    return;
                }
            }

            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject buttonObject = new GameObject("NextRound", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(transform, false);
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(0f, -240f);
            buttonRect.sizeDelta = new Vector2(240f, 56f);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.16f, 0.72f, 0.38f, 0.96f);
            image.raycastTarget = true;
            NextRoundButton = buttonObject.GetComponent<Button>();
            NextRoundButton.onClick.AddListener(OnNextRoundClicked);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            NextRoundLabel = labelObject.GetComponent<Text>();
            NextRoundLabel.font = fallbackFont;
            NextRoundLabel.fontSize = 26;
            NextRoundLabel.fontStyle = FontStyle.Bold;
            NextRoundLabel.alignment = TextAnchor.MiddleCenter;
            NextRoundLabel.color = Color.white;
            NextRoundLabel.raycastTarget = false;
            NextRoundLabel.text = "下一局";
            Outline outline = labelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(1f, -1f);
            buttonObject.SetActive(false);
        }

        void BindLobbyButton(BrawlGameManager gm)
        {
            bool show = gm != null && gm.HudShowLobbyActions && BrawlLevelCatalog.ActiveSceneIsLauncher();
            bool isHost = show && gm.HudIsHost && NetworkClient.isConnected;
            if (LobbyReadyRoot != null)
                LobbyReadyRoot.SetActive(show);
            if (!show)
                HideLobbyStartConfirm();

            SetLobbyButtonActive(LobbyButton, show && !isHost);
            SetLobbyButtonActive(LobbyStartButton, show && isHost);
            HideLooseLobbyButtons(show, isHost);
            if (!show) return;

            if (isHost)
            {
                if (LobbyStartLabel != null)
                    LobbyStartLabel.text = "开始  START";
                Image startImage = LobbyStartButton != null ? LobbyStartButton.GetComponent<Image>() : null;
                if (startImage != null)
                    startImage.color = new Color(0.22f, 0.72f, 0.32f, 0.96f);
                if (LobbyStartButton != null)
                {
                    LobbyStartButton.interactable = !lobbyStartConfirmVisible;
                    LobbyStartButton.transform.SetAsLastSibling();
                }
            }
            else
            {
                bool ready = gm.HudLocalIsReady();
                if (LobbyLabel != null)
                    LobbyLabel.text = ready ? "取消准备" : "准备  READY";
                Image readyImage = LobbyButton != null ? LobbyButton.GetComponent<Image>() : null;
                if (readyImage != null)
                    readyImage.color = ready
                        ? new Color(0.42f, 0.43f, 0.40f, 0.92f)
                        : new Color(0.92f, 0.70f, 0.04f, 0.96f);
                if (LobbyButton != null)
                {
                    LobbyButton.interactable = true;
                    LobbyButton.transform.SetAsLastSibling();
                }
            }

            bool showTransient = Time.unscaledTime < lobbyTransientStatusUntil && !string.IsNullOrEmpty(lobbyTransientStatus);
            if (LobbyReadyStatus != null)
            {
                string readyLine = string.IsNullOrEmpty(gm.HudLobbyReadyLine) ? "已准备 0/0" : gm.HudLobbyReadyLine;
                string hint = gm.HudLobbyAllReady
                    ? "全员已准备，等待房主开始"
                    : "还有人未准备，房主可选择直接开始";
                LobbyReadyStatus.text = showTransient ? lobbyTransientStatus : readyLine + "    " + hint;
            }

            bool hostCanAddBots = NetworkServer.active && NetworkClient.isConnected;
            if (LobbyBotRoot != null)
                LobbyBotRoot.SetActive(hostCanAddBots);
            if (LobbyBotValue != null)
                LobbyBotValue.text = lobbyBotSelection.ToString();

            int aliveBots = BrawlBotBrain.AliveCount;
            bool canAdd = hostCanAddBots && aliveBots < BrawlBotBrain.MaxBots;
            if (LobbyBotMinusButton != null)
                LobbyBotMinusButton.interactable = hostCanAddBots && lobbyBotSelection > 1;
            if (LobbyBotPlusButton != null)
                LobbyBotPlusButton.interactable = hostCanAddBots && lobbyBotSelection < BrawlBotBrain.MaxBots;
            if (LobbyBotAddButton != null)
            {
                LobbyBotAddButton.interactable = canAdd;
                Text addLabel = LobbyBotAddButton.GetComponentInChildren<Text>();
                if (addLabel != null)
                    addLabel.text = canAdd ? "添加" : "已满";
            }

            if (lobbyStartConfirmVisible && LobbyStartConfirmRoot != null)
                LobbyStartConfirmRoot.transform.SetAsLastSibling();
        }

        static void SetLobbyButtonActive(Button button, bool show)
        {
            if (button != null)
                button.gameObject.SetActive(show);
        }

        void HideLooseLobbyButtons(bool show, bool isHost)
        {
            HideNamedChild("LobbyAction", show && !isHost ? LobbyButton : null);
            HideNamedChild("LobbyStart", show && isHost ? LobbyStartButton : null);
        }

        void HideNamedChild(string name, Button keep)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child.name != name) continue;
                if (keep != null && child == keep.transform) continue;
                child.gameObject.SetActive(false);
            }
        }

        void OnLobbyReadyClicked()
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.RequestLobbyReadyToggle();
        }

        void OnLobbyStartClicked()
        {
            BrawlGameManager gm = BrawlGameManager.Instance;
            if (gm == null || !gm.HudIsHost) return;
            if (!gm.HudLobbyAllReady)
            {
                ShowLobbyStartConfirm();
                return;
            }

            gm.RequestLobbyStart(true);
        }

        void ShowLobbyStartConfirm()
        {
            EnsureLobbyStartConfirm();
            lobbyStartConfirmVisible = true;
            if (LobbyStartConfirmRoot != null)
            {
                LobbyStartConfirmRoot.SetActive(true);
                LobbyStartConfirmRoot.transform.SetAsLastSibling();
            }
        }

        void HideLobbyStartConfirm()
        {
            lobbyStartConfirmVisible = false;
            if (LobbyStartConfirmRoot != null)
                LobbyStartConfirmRoot.SetActive(false);
        }

        void OnLobbyStartConfirmYesClicked()
        {
            HideLobbyStartConfirm();
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.RequestLobbyStart(true);
        }

        void OnLobbyStartConfirmNoClicked()
        {
            HideLobbyStartConfirm();
        }

        void EnsureLobbyButton()
        {
            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            BindOrCreateLobbyButton(
                "LobbyAction",
                Vector2.zero,
                new Color(0.92f, 0.70f, 0.04f, 0.96f),
                "准备  READY",
                OnLobbyReadyClicked,
                fallbackFont,
                ref LobbyButton,
                ref LobbyLabel);

            BindOrCreateLobbyButton(
                "LobbyStart",
                Vector2.zero,
                new Color(0.22f, 0.72f, 0.32f, 0.96f),
                "开始  START",
                OnLobbyStartClicked,
                fallbackFont,
                ref LobbyStartButton,
                ref LobbyStartLabel);
        }

        void BindOrCreateLobbyButton(
            string name,
            Vector2 position,
            Color color,
            string text,
            UnityEngine.Events.UnityAction onClick,
            Font font,
            ref Button button,
            ref Text label)
        {
            if (button != null && label != null)
            {
                button.onClick.RemoveListener(onClick);
                button.onClick.AddListener(onClick);
                return;
            }

            Transform existing = transform.Find(name);
            if (existing != null)
            {
                button = existing.GetComponent<Button>();
                label = existing.Find("Label")?.GetComponent<Text>();
                if (button != null && label != null)
                {
                    button.onClick.RemoveListener(onClick);
                    button.onClick.AddListener(onClick);
                    button.gameObject.SetActive(false);
                    return;
                }
            }

            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(transform, false);
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = position;
            buttonRect.sizeDelta = new Vector2(220f, 56f);
            Image image = buttonObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label = labelObject.GetComponent<Text>();
            label.font = font;
            label.fontSize = 24;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = text;
            Outline outline = labelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(1f, -1f);
            buttonObject.SetActive(false);
        }

        void EnsureLobbyReadyPanel()
        {
            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            RectTransform root;
            Transform existing = transform.Find("LobbyReadyPanel");
            if (existing is RectTransform existingRect)
            {
                root = existingRect;
            }
            else
            {
                GameObject rootObject = new GameObject("LobbyReadyPanel", typeof(RectTransform));
                root = rootObject.GetComponent<RectTransform>();
                root.SetParent(transform, false);
            }

            SetHudRect(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 18f), new Vector2(360f, 156f));
            LobbyReadyRoot = root.gameObject;

            Transform statusTransform = root.Find("ReadyStatus");
            Image statusBack;
            if (statusTransform == null)
            {
                GameObject statusObject = new GameObject("ReadyStatus", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                statusTransform = statusObject.transform;
                statusTransform.SetParent(root, false);
                statusBack = statusObject.GetComponent<Image>();
            }
            else
            {
                statusBack = statusTransform.GetComponent<Image>();
                if (statusBack == null) statusBack = statusTransform.gameObject.AddComponent<Image>();
            }
            statusBack.color = new Color(0.20f, 0.12f, 0.07f, 0.68f);
            statusBack.raycastTarget = false;
            SetHudRect(statusTransform as RectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(360f, 28f));
            EnsureGraphicOutline(statusBack, new Color(0.68f, 0.60f, 0.46f, 0.82f), 1f);

            Transform readyStatusTransform = statusTransform.Find("Label");
            if (readyStatusTransform == null)
                LobbyReadyStatus = CreatePlainText(statusTransform, "Label", fallbackFont, 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            else
                LobbyReadyStatus = readyStatusTransform.GetComponent<Text>();
            SetHudRect(LobbyReadyStatus.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            LobbyReadyStatus.text = "已准备 1/2    等待房主开始";

            Transform readyFrameTransform = root.Find("ReadyButtonFrame");
            Image readyFrameImage;
            if (readyFrameTransform == null)
            {
                GameObject frameObject = new GameObject("ReadyButtonFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                readyFrameTransform = frameObject.transform;
                readyFrameTransform.SetParent(root, false);
                readyFrameImage = frameObject.GetComponent<Image>();
            }
            else
            {
                readyFrameImage = readyFrameTransform.GetComponent<Image>();
                if (readyFrameImage == null) readyFrameImage = readyFrameTransform.gameObject.AddComponent<Image>();
            }
            readyFrameImage.color = new Color(0.42f, 0.42f, 0.39f, 0.98f);
            readyFrameImage.raycastTarget = false;
            SetHudRect(readyFrameTransform as RectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(360f, 78f));
            EnsureGraphicOutline(readyFrameImage, new Color(0.035f, 0.035f, 0.03f, 1f), 2f);
            EnsureBevelEdges(readyFrameTransform as RectTransform);

            StyleLobbyFrameButton(LobbyButton, LobbyLabel, new Color(0.92f, 0.70f, 0.04f, 0.96f), readyFrameTransform);
            StyleLobbyFrameButton(LobbyStartButton, LobbyStartLabel, new Color(0.22f, 0.72f, 0.32f, 0.96f), readyFrameTransform);

            Transform botTransform = root.Find("BotRow");
            Image botBack;
            if (botTransform == null)
            {
                GameObject botObject = new GameObject("BotRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                botTransform = botObject.transform;
                botTransform.SetParent(root, false);
                botBack = botObject.GetComponent<Image>();
            }
            else
            {
                botBack = botTransform.GetComponent<Image>();
                if (botBack == null) botBack = botTransform.gameObject.AddComponent<Image>();
            }
            botBack.color = new Color(0.08f, 0.09f, 0.10f, 0.64f);
            botBack.raycastTarget = true;
            SetHudRect(botTransform as RectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(360f, 42f));
            EnsureGraphicOutline(botBack, new Color(0.54f, 0.52f, 0.46f, 0.78f), 1f);
            LobbyBotRoot = botTransform.gameObject;

            Text botTitle = botTransform.Find("Title")?.GetComponent<Text>();
            if (botTitle == null)
                botTitle = CreatePlainText(botTransform, "Title", fallbackFont, 15, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetHudRect(botTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(150f, 36f));
            botTitle.text = "开房 Bot";

            LobbyBotMinusButton = EnsureLobbyBotButton(botTransform, "Minus", "−", fallbackFont, OnLobbyBotMinusClicked);
            SetHudRect(LobbyBotMinusButton.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-166f, 0f), new Vector2(32f, 30f));

            Transform valueTransform = botTransform.Find("Value");
            if (valueTransform == null)
                LobbyBotValue = CreatePlainText(botTransform, "Value", fallbackFont, 18, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.96f, 0.76f, 0.12f, 1f));
            else
                LobbyBotValue = valueTransform.GetComponent<Text>();
            SetHudRect(LobbyBotValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-128f, 0f), new Vector2(36f, 30f));
            LobbyBotValue.text = lobbyBotSelection.ToString();

            LobbyBotPlusButton = EnsureLobbyBotButton(botTransform, "Plus", "+", fallbackFont, OnLobbyBotPlusClicked);
            SetHudRect(LobbyBotPlusButton.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-88f, 0f), new Vector2(32f, 30f));

            LobbyBotAddButton = EnsureLobbyBotButton(botTransform, "Add", "添加", fallbackFont, OnLobbyBotAddClicked, true);
            SetHudRect(LobbyBotAddButton.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(72f, 30f));

            LobbyReadyRoot.SetActive(false);
        }

        void StyleLobbyFrameButton(Button button, Text label, Color color, Transform frame)
        {
            if (button == null || frame == null) return;
            RectTransform readyRect = button.transform as RectTransform;
            readyRect.SetParent(frame, false);
            SetHudRect(readyRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8f, -8f));
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = color;
                EnsureGraphicOutline(image, new Color(0.06f, 0.06f, 0.05f, 0.95f), 1f);
            }

            if (label == null) return;
            label.fontSize = 30;
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(0.08f, 0.07f, 0.04f, 1f);
            DisableTextOutline(label);
        }

        void EnsureLobbyStartConfirm()
        {
            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (LobbyStartConfirmRoot == null)
            {
                Transform existing = transform.Find("LobbyStartConfirm");
                LobbyStartConfirmRoot = existing != null ? existing.gameObject : null;
            }

            if (LobbyStartConfirmRoot == null)
            {
                GameObject rootObject = new GameObject("LobbyStartConfirm", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                rootObject.transform.SetParent(transform, false);
                LobbyStartConfirmRoot = rootObject;
            }

            RectTransform root = LobbyStartConfirmRoot.transform as RectTransform;
            SetHudRect(root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image dim = LobbyStartConfirmRoot.GetComponent<Image>();
            if (dim == null) dim = LobbyStartConfirmRoot.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.02f, 0.03f, 0.62f);
            dim.raycastTarget = true;

            Transform cardTransform = root.Find("Card");
            if (cardTransform == null)
            {
                GameObject cardObject = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                cardTransform = cardObject.transform;
                cardTransform.SetParent(root, false);
            }

            Image cardImage = cardTransform.GetComponent<Image>();
            if (cardImage == null) cardImage = cardTransform.gameObject.AddComponent<Image>();
            cardImage.color = new Color(0.16f, 0.12f, 0.08f, 0.96f);
            cardImage.raycastTarget = true;
            SetHudRect(cardTransform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 196f));
            EnsureGraphicOutline(cardImage, new Color(0.72f, 0.64f, 0.42f, 0.9f), 1f);

            Transform messageTransform = cardTransform.Find("Message");
            if (messageTransform == null)
                LobbyStartConfirmMessage = CreatePlainText(cardTransform, "Message", fallbackFont, 22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            else
                LobbyStartConfirmMessage = messageTransform.GetComponent<Text>();
            SetHudRect(LobbyStartConfirmMessage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(420f, 72f));
            LobbyStartConfirmMessage.text = "还有人未准备，是否要继续？";

            LobbyStartConfirmYesButton = EnsureLobbyBotButton(cardTransform, "Yes", "是", fallbackFont, OnLobbyStartConfirmYesClicked, true);
            SetHudRect(LobbyStartConfirmYesButton.transform as RectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-90f, 28f), new Vector2(140f, 44f));
            Text yesLabel = LobbyStartConfirmYesButton.GetComponentInChildren<Text>();
            if (yesLabel != null)
            {
                yesLabel.fontSize = 22;
                yesLabel.text = "是";
            }

            LobbyStartConfirmNoButton = EnsureLobbyBotButton(cardTransform, "No", "否", fallbackFont, OnLobbyStartConfirmNoClicked);
            SetHudRect(LobbyStartConfirmNoButton.transform as RectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(90f, 28f), new Vector2(140f, 44f));
            Text noLabel = LobbyStartConfirmNoButton.GetComponentInChildren<Text>();
            if (noLabel != null)
            {
                noLabel.fontSize = 22;
                noLabel.text = "否";
            }

            LobbyStartConfirmRoot.SetActive(lobbyStartConfirmVisible);
        }

        Button EnsureLobbyBotButton(Transform parent, string name, string label, Font font, UnityEngine.Events.UnityAction onClick, bool accent = false)
        {
            Transform existing = parent.Find(name);
            Button button = existing != null ? existing.GetComponent<Button>() : null;
            Text text = existing != null ? existing.Find("Label")?.GetComponent<Text>() : null;
            if (button == null)
            {
                GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(parent, false);
                button = buttonObject.GetComponent<Button>();
                button.targetGraphic = buttonObject.GetComponent<Image>();
                text = CreatePlainText(buttonObject.transform, "Label", font, accent ? 17 : 22, FontStyle.Bold, TextAnchor.MiddleCenter,
                    accent ? new Color(0.08f, 0.07f, 0.04f, 1f) : Color.white);
                SetHudRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            }

            Image image = button.GetComponent<Image>();
            image.color = accent ? new Color(0.92f, 0.70f, 0.04f, 0.96f) : new Color(0.28f, 0.29f, 0.30f, 0.94f);
            image.raycastTarget = true;
            EnsureGraphicOutline(image, new Color(0.05f, 0.05f, 0.05f, 0.95f), 1f);
            EnsureBevelEdges(button.transform as RectTransform);
            text.text = label;
            text.fontSize = accent ? 15 : 18;
            text.fontStyle = FontStyle.Bold;
            text.color = accent ? new Color(0.08f, 0.07f, 0.04f, 1f) : Color.white;
            if (accent)
                DisableTextOutline(text);
            button.onClick.RemoveListener(onClick);
            button.onClick.AddListener(onClick);
            return button;
        }

        static void EnsureGraphicOutline(Graphic graphic, Color color, float distance)
        {
            if (graphic == null) return;
            Outline outline = graphic.GetComponent<Outline>();
            if (outline == null) outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true;
        }

        static void DisableTextOutline(Text text)
        {
            if (text == null) return;
            Outline outline = text.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        static void EnsureBevelEdges(RectTransform target)
        {
            if (target == null) return;
            Color light = new Color(0.88f, 0.86f, 0.78f, 0.92f);
            Color dark = new Color(0.035f, 0.035f, 0.03f, 0.96f);
            EnsureBevelEdge(target, "BevelTop", light, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 2f));
            EnsureBevelEdge(target, "BevelLeft", light, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2f, 0f));
            EnsureBevelEdge(target, "BevelBottom", dark, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f));
            EnsureBevelEdge(target, "BevelRight", dark, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(2f, 0f));
        }

        static void EnsureBevelEdge(RectTransform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            Transform existing = parent.Find(name);
            Image edge;
            if (existing == null)
            {
                GameObject edgeObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                edgeObject.transform.SetParent(parent, false);
                edge = edgeObject.GetComponent<Image>();
            }
            else
            {
                edge = existing.GetComponent<Image>();
                if (edge == null) edge = existing.gameObject.AddComponent<Image>();
            }

            edge.color = color;
            edge.raycastTarget = false;
            SetHudRect(edge.rectTransform, anchorMin, anchorMax, pivot, Vector2.zero, size);
            edge.transform.SetAsFirstSibling();
        }

        void OnLobbyBotMinusClicked()
        {
            lobbyBotSelection = Mathf.Max(1, lobbyBotSelection - 1);
        }

        void OnLobbyBotPlusClicked()
        {
            lobbyBotSelection = Mathf.Min(BrawlBotBrain.MaxBots, lobbyBotSelection + 1);
        }

        void OnLobbyBotAddClicked()
        {
            if (!NetworkServer.active || !NetworkClient.isConnected)
            {
                SetLobbyTransientStatus("只有房主可以添加 Bot");
                return;
            }

            BrawlNetworkManager manager = BrawlNetworkManager.SingletonBrawl;
            if (manager == null)
            {
                SetLobbyTransientStatus("未找到联机管理器，Bot 添加失败");
                return;
            }

            int remaining = Mathf.Max(0, BrawlBotBrain.MaxBots - BrawlBotBrain.AliveCount);
            if (remaining <= 0)
            {
                SetLobbyTransientStatus($"Bot 已达到上限 {BrawlBotBrain.MaxBots}/{BrawlBotBrain.MaxBots}");
                return;
            }

            int requested = Mathf.Min(lobbyBotSelection, remaining);
            int spawned = manager.SpawnBots(requested);
            if (spawned <= 0)
            {
                SetLobbyTransientStatus("Bot 添加失败，请稍后再试");
                return;
            }

            int alive = BrawlBotBrain.AliveCount;
            SetLobbyTransientStatus(alive >= BrawlBotBrain.MaxBots
                ? $"已添加 {spawned} 个 Bot，房间已满"
                : $"已添加 {spawned} 个 Bot，当前 Bot {alive}/{BrawlBotBrain.MaxBots}");
        }

        void SetLobbyTransientStatus(string text)
        {
            lobbyTransientStatus = text;
            lobbyTransientStatusUntil = Time.unscaledTime + 2.5f;
        }

        void BindRulesPanel(BrawlGameManager gm)
        {
            bool show = gm != null && gm.HudIsShowingRules;
            if (RulesRoot != null)
                RulesRoot.SetActive(show);
            if (!show) return;

            if (RulesRoot != null)
                RulesRoot.transform.SetAsLastSibling();

            RefreshRulesArtwork();
            bool illustrated = ShouldUseIllustratedRules();
            if (RulesArtwork != null)
                RulesArtwork.gameObject.SetActive(illustrated);
            if (rulesCountdownBackdrop != null)
                rulesCountdownBackdrop.gameObject.SetActive(illustrated);
            if (RulesTitle != null)
                RulesTitle.gameObject.SetActive(!illustrated);
            if (RulesBody != null)
                RulesBody.gameObject.SetActive(!illustrated);

            Transform card = RulesRoot != null ? RulesRoot.transform.Find("Card") : null;
            Transform accent = card != null ? card.Find("Accent") : null;
            if (accent != null)
                accent.gameObject.SetActive(!illustrated);
            if (card is RectTransform cardRect)
            {
                if (illustrated)
                {
                    SetHudRect(cardRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                }
                else
                {
                    SetHudRect(
                        cardRect,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        Vector2.zero,
                        new Vector2(640f, 430f));
                }
                Image cardImage = cardRect.GetComponent<Image>();
                if (cardImage != null)
                    cardImage.color = illustrated
                        ? new Color(0.015f, 0.018f, 0.024f, 0.98f)
                        : new Color(0.05f, 0.06f, 0.08f, 0.94f);
            }

            if (RulesTitle != null)
                RulesTitle.text = gm.HudRulesTitle;
            if (RulesBody != null && !string.IsNullOrEmpty(gm.HudRulesBody))
                RulesBody.text = gm.HudRulesBody;

            int seconds = Mathf.Max(1, Mathf.CeilToInt(gm.HudRemainingSeconds));
            if (RulesCountdown != null)
            {
                RulesCountdown.gameObject.SetActive(true);
                RulesCountdown.text = $"{seconds}s后进入关卡";
                RulesCountdown.fontSize = illustrated ? 30 : 22;
                RulesCountdown.fontStyle = FontStyle.Bold;
                RulesCountdown.alignment = TextAnchor.MiddleCenter;
                if (illustrated)
                {
                    SetHudRect(
                        RulesCountdown.rectTransform,
                        new Vector2(0.28f, 0.065f),
                        new Vector2(0.72f, 0.155f),
                        new Vector2(0.5f, 0.5f),
                        Vector2.zero,
                        Vector2.zero);
                }
                else
                {
                    SetHudRect(
                        RulesCountdown.rectTransform,
                        new Vector2(0.5f, 0f),
                        new Vector2(0.5f, 0f),
                        new Vector2(0.5f, 0f),
                        new Vector2(0f, 22f),
                        new Vector2(560f, 36f));
                }
                float pulse = Mathf.PingPong(Time.unscaledTime * 3.2f, 1f);
                RulesCountdown.color = Color.Lerp(new Color(1f, 0.84f, 0.28f, 1f), Color.white, pulse);
                RulesCountdown.transform.SetAsLastSibling();
            }
        }

        bool ShouldUseIllustratedRules()
        {
            int levelIndex = BrawlLevelCatalog.GetLevelIndex(BrawlLevelCatalog.ActiveSceneName());
            return (levelIndex == 0 || levelIndex == 1)
                && RulesArtwork != null
                && RulesArtwork.texture != null;
        }

        void RefreshRulesArtwork()
        {
            if (RulesArtwork == null) return;

            string resource = RulesArtworkResourceForActiveLevel();
            Texture2D texture = string.IsNullOrEmpty(resource)
                ? null
                : Resources.Load<Texture2D>(resource);
            if (RulesArtwork.texture != texture)
                RulesArtwork.texture = texture;
        }

        static string RulesArtworkResourceForActiveLevel()
        {
            switch (BrawlLevelCatalog.GetLevelIndex(BrawlLevelCatalog.ActiveSceneName()))
            {
                case 0: return Level01RulesArtworkResource;
                case 1: return Level02RulesArtworkResource;
                default: return null;
            }
        }

        void EnsureRulesPanel()
        {
            if (RulesRoot != null && RulesBody != null && RulesCountdown != null)
            {
                EnsureRulesArtwork(RulesRoot.transform.Find("Card") as RectTransform);
                RulesRoot.SetActive(false);
                return;
            }

            Transform existing = transform.Find("Rules");
            if (existing != null)
            {
                RulesRoot = existing.gameObject;
                RulesTitle = existing.Find("Card/Title")?.GetComponent<Text>();
                RulesBody = existing.Find("Card/Body")?.GetComponent<Text>();
                RulesCountdown = existing.Find("Card/Countdown")?.GetComponent<Text>();
                if (RulesRoot != null && RulesBody != null && RulesCountdown != null)
                {
                    EnsureRulesArtwork(existing.Find("Card") as RectTransform);
                    RulesRoot.SetActive(false);
                    return;
                }
            }

            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject root = new GameObject("Rules", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            Image dim = root.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.58f);
            dim.raycastTarget = false;
            RulesRoot = root;

            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.SetParent(rootRect, false);
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(640f, 430f);
            Image cardImage = card.GetComponent<Image>();
            cardImage.color = new Color(0.05f, 0.06f, 0.08f, 0.94f);
            cardImage.raycastTarget = false;

            GameObject accent = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.SetParent(cardRect, false);
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0f, 5f);
            accent.GetComponent<Image>().color = new Color(1f, 0.84f, 0.28f, 1f);
            accent.GetComponent<Image>().raycastTarget = false;

            RulesTitle = CreatePlainText(cardRect, "Title", fallbackFont, 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetHudRect(RulesTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(560f, 40f));
            RulesTitle.text = "本局规则";

            RulesBody = CreatePlainText(cardRect, "Body", fallbackFont, 20, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.92f, 0.93f, 0.95f, 0.96f));
            SetHudRect(RulesBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(560f, 270f));
            RulesBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            RulesBody.verticalOverflow = VerticalWrapMode.Overflow;
            RulesBody.lineSpacing = 1.12f;
            RulesBody.text = BrawlLevelInfo.HoldKpiRules;

            RulesCountdown = CreatePlainText(cardRect, "Countdown", fallbackFont, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.28f, 1f));
            SetHudRect(RulesCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(560f, 36f));
            RulesCountdown.text = "10s后进入关卡";

            EnsureRulesArtwork(cardRect);

            root.SetActive(false);
        }

        void EnsureRulesArtwork(RectTransform cardRect)
        {
            if (cardRect == null) return;

            SetHudRect(cardRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Image cardImage = cardRect.GetComponent<Image>();
            if (cardImage != null)
                cardImage.color = new Color(0.015f, 0.018f, 0.024f, 0.98f);

            Transform artworkTransform = cardRect.Find("Artwork");
            if (artworkTransform == null)
            {
                GameObject artworkObject = new GameObject("Artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                artworkTransform = artworkObject.transform;
                artworkTransform.SetParent(cardRect, false);
            }

            RulesArtwork = artworkTransform.GetComponent<RawImage>();
            if (RulesArtwork == null)
                RulesArtwork = artworkTransform.gameObject.AddComponent<RawImage>();
            SetHudRect(RulesArtwork.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RefreshRulesArtwork();
            RulesArtwork.color = Color.white;
            RulesArtwork.raycastTarget = false;
            RulesArtwork.uvRect = new Rect(0f, 0f, 1f, 1f);
            RulesArtwork.transform.SetAsFirstSibling();

            Transform backdropTransform = cardRect.Find("CountdownBackdrop");
            if (backdropTransform == null)
            {
                GameObject backdropObject = new GameObject("CountdownBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                backdropTransform = backdropObject.transform;
                backdropTransform.SetParent(cardRect, false);
            }

            rulesCountdownBackdrop = backdropTransform.GetComponent<Image>();
            if (rulesCountdownBackdrop == null)
                rulesCountdownBackdrop = backdropTransform.gameObject.AddComponent<Image>();
            SetHudRect(
                rulesCountdownBackdrop.rectTransform,
                new Vector2(0.27f, 0.06f),
                new Vector2(0.73f, 0.16f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            rulesCountdownBackdrop.color = new Color(0.025f, 0.027f, 0.032f, 1f);
            rulesCountdownBackdrop.raycastTarget = false;
            rulesCountdownBackdrop.transform.SetSiblingIndex(1);
        }

        static Text CreatePlainText(Transform parent, string name, Font font, int size, FontStyle style, TextAnchor align, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        static void SetHudRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        void BindCursorHint()
        {
            if (CursorHintText == null) return;
            CursorHintText.text = LocalCameraRig.IsCursorCaptured
                ? "Esc  退出鼠标捕获"
                : "Alt  重新捕获鼠标";
            CursorHintText.color = LocalCameraRig.IsCursorCaptured
                ? new Color(1f, 1f, 1f, 0.88f)
                : new Color(1f, 0.86f, 0.28f, 1f);
        }

        void ApplyHudVisualStyle()
        {
            IdleFrameColor = new Color(0.055f, 0.06f, 0.07f, 0.58f);
            HoldingFrameColor = new Color(0.96f, 0.74f, 0.10f, 0.86f);

            RectTransform topBar = FindNamedParent(TimerText != null ? TimerText.transform : null, "TopBar") as RectTransform;
            if (topBar != null)
                SetHudRect(topBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(0f, 94f));

            // 使用比例锚点而不是按 1920 宽度写死坐标，兼容 Launcher 现有的 1280x720 Canvas。
            // Slot 数组顺序是 P1、P2、P3、P4；视觉顺序保持 P2、P1、计时、P3、P4。
            float[] anchors = { 0.30f, 0.10f, 0.70f, 0.90f };
            int count = Slots != null ? Mathf.Min(Slots.Length, anchors.Length) : 0;
            for (int i = 0; i < count; i++)
            {
                PlayerSlot slot = Slots[i];
                if (slot?.Root == null) continue;
                EnsureSlotVisualReferences(slot);

                RectTransform root = slot.Root.transform as RectTransform;
                Vector2 slotAnchor = new Vector2(anchors[i], 1f);
                SetHudRect(root, slotAnchor, slotAnchor, new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(220f, 58f));

                if (slot.Frame != null)
                {
                    SetHudRect(slot.Frame.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    slot.Frame.color = IdleFrameColor;
                    EnsureGraphicOutline(slot.Frame, new Color(0.66f, 0.65f, 0.60f, 0.58f), 1f);
                }

                if (slot.Avatar != null)
                {
                    SetHudRect(slot.Avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28f, 0f), new Vector2(44f, 44f));
                    slot.Avatar.sprite = ResolveEmptyAvatarSprite(slot);
                    slot.Avatar.color = new Color(0.68f, 0.70f, 0.74f, 1f);
                    slot.Avatar.preserveAspect = true;
                }

                if (slot.Name != null)
                {
                    SetHudRect(slot.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(56f, 12f), new Vector2(116f, 20f));
                    slot.Name.fontSize = 14;
                    slot.Name.alignment = TextAnchor.MiddleLeft;
                }

                Transform barTransform = slot.Root.transform.Find("BarBack");
                Image barBack = barTransform != null ? barTransform.GetComponent<Image>() : null;
                if (barBack != null)
                {
                    SetHudRect(barBack.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(56f, -12f), new Vector2(104f, 9f));
                    barBack.color = new Color(0.045f, 0.05f, 0.055f, 0.94f);
                }

                if (slot.BarFill != null && i < SlotBarColors.Length)
                    slot.BarFill.color = SlotBarColors[i];

                if (slot.Score != null)
                {
                    SetHudRect(slot.Score.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(166f, -12f), new Vector2(48f, 18f));
                    slot.Score.fontSize = 12;
                    slot.Score.alignment = TextAnchor.MiddleLeft;
                }
            }

            if (TimerText != null && TimerText.transform.parent is RectTransform timer)
            {
                SetHudRect(timer, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(72f, 72f));
                Image ring = timer.Find("Ring")?.GetComponent<Image>();
                Image fill = timer.Find("Fill")?.GetComponent<Image>();
                if (ring != null)
                {
                    SetHudRect(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 72f));
                    ring.color = new Color(0.78f, 0.79f, 0.76f, 0.84f);
                }
                if (fill != null)
                {
                    SetHudRect(fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f));
                    fill.color = new Color(0.08f, 0.085f, 0.09f, 0.78f);
                }
                SetHudRect(TimerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(68f, 38f));
                TimerText.fontSize = 22;
            }

            if (StatusText != null)
                SetHudRect(StatusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -6f), new Vector2(900f, 28f));

            PositionTurboPanel(TurboTitle != null ? TurboTitle.transform : null);
        }

        static Transform FindNamedParent(Transform child, string name)
        {
            Transform current = child;
            while (current != null && current.name != name) current = current.parent;
            return current;
        }

        void BindPassCrosshair(BrawlGameManager gm)
        {
            EnsurePassCrosshair();
            if (PassCrosshairRoot == null) return;

            NetFAnnequinController local = FindLocalPlayer();
            bool show = gm != null
                && gm.HudIsPlaying
                && gm.IsPassTheBuck
                && local != null
                && local.IsHoldingComputer
                && !local.IsDead
                && LocalCameraRig.IsCursorCaptured;
            PassCrosshairRoot.anchorMin = new Vector2(NetFAnnequinController.PassAimViewportX, 0.5f);
            PassCrosshairRoot.anchorMax = new Vector2(NetFAnnequinController.PassAimViewportX, 0.5f);
            PassCrosshairRoot.anchoredPosition = Vector2.zero;
            PassCrosshairRoot.gameObject.SetActive(show);
            if (!show) return;

            bool locked = local.FindAimedPlayer() != null;
            Color color = locked
                ? new Color(1f, 0.86f, 0.35f, 0.78f)
                : new Color(1f, 1f, 1f, 0.28f);
            float scale = locked ? 1.08f : 1f;
            PassCrosshairRoot.localScale = Vector3.one * scale;
            if (PassCrosshairMarks == null) return;
            for (int i = 0; i < PassCrosshairMarks.Length; i++)
            {
                if (PassCrosshairMarks[i] != null)
                    PassCrosshairMarks[i].color = color;
            }
        }

        NetFAnnequinController FindLocalPlayer()
        {
            for (int i = 0; i < hudPlayers.Count; i++)
            {
                if (hudPlayers[i] is NetFAnnequinController fan && fan.isLocalPlayer)
                    return fan;
            }

            return null;
        }

        void EnsurePassCrosshair()
        {
            if (PassCrosshairRoot != null && PassCrosshairMarks != null && PassCrosshairMarks.Length >= 5)
                return;

            Transform existing = transform.Find("PassCrosshair");
            if (existing != null)
            {
                PassCrosshairRoot = existing.GetComponent<RectTransform>();
                PassCrosshairMarks = existing.GetComponentsInChildren<Image>(true);
                if (PassCrosshairRoot != null)
                {
                    PassCrosshairRoot.gameObject.SetActive(false);
                    return;
                }
            }

            var rootObject = new GameObject("PassCrosshair", typeof(RectTransform));
            PassCrosshairRoot = rootObject.GetComponent<RectTransform>();
            PassCrosshairRoot.SetParent(transform, false);
            PassCrosshairRoot.anchorMin = new Vector2(NetFAnnequinController.PassAimViewportX, 0.5f);
            PassCrosshairRoot.anchorMax = new Vector2(NetFAnnequinController.PassAimViewportX, 0.5f);
            PassCrosshairRoot.pivot = new Vector2(0.5f, 0.5f);
            PassCrosshairRoot.anchoredPosition = Vector2.zero;
            PassCrosshairRoot.sizeDelta = new Vector2(36f, 36f);
            PassCrosshairRoot.SetAsFirstSibling();

            PassCrosshairMarks = new Image[5];
            PassCrosshairMarks[0] = CreateCrosshairMark(PassCrosshairRoot, "Top", new Vector2(0f, 11f), new Vector2(2f, 10f));
            PassCrosshairMarks[1] = CreateCrosshairMark(PassCrosshairRoot, "Bottom", new Vector2(0f, -11f), new Vector2(2f, 10f));
            PassCrosshairMarks[2] = CreateCrosshairMark(PassCrosshairRoot, "Left", new Vector2(-11f, 0f), new Vector2(10f, 2f));
            PassCrosshairMarks[3] = CreateCrosshairMark(PassCrosshairRoot, "Right", new Vector2(11f, 0f), new Vector2(10f, 2f));
            PassCrosshairMarks[4] = CreateCrosshairMark(PassCrosshairRoot, "Dot", Vector2.zero, new Vector2(3f, 3f));
            rootObject.SetActive(false);
        }

        static Image CreateCrosshairMark(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var markObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = markObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = markObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.28f);
            image.raycastTarget = false;
            return image;
        }

        void EnsureCursorHint()
        {
            const string controlsHint =
                "W S A D : 移动\nSpace : 跳跃\nLeft Click : 攻击\nHold Right Click : 长按抓取\nRelease Right Click : 松开放下\nEsc : 退出鼠标捕获\nAlt : 重新捕获鼠标";

            if (ControlsText == null)
            {
                Transform controls = transform.Find("Controls");
                if (controls != null) ControlsText = controls.GetComponent<Text>();
            }

            if (ControlsText != null)
            {
                ControlsText.text = controlsHint;
                RectTransform controlsRect = ControlsText.rectTransform;
                controlsRect.anchorMin = Vector2.zero;
                controlsRect.anchorMax = Vector2.zero;
                controlsRect.pivot = Vector2.zero;
                controlsRect.anchoredPosition = new Vector2(24f, 160f);
                controlsRect.sizeDelta = new Vector2(420f, 180f);
                ControlsText.fontSize = 15;
                ControlsText.lineSpacing = 0.96f;
                ControlsText.color = new Color(1f, 1f, 1f, 0.82f);
                ControlsText.gameObject.SetActive(false);
            }

            if (CursorHintText == null)
            {
                Transform existing = transform.Find("CursorHint");
                if (existing != null) CursorHintText = existing.GetComponent<Text>();
            }

            if (CursorHintText != null) return;

            Font fallbackFont = TimerText != null && TimerText.font != null
                ? TimerText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject hintObject = new GameObject("CursorHint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform hintRect = hintObject.GetComponent<RectTransform>();
            hintRect.SetParent(transform, false);
            hintRect.anchorMin = new Vector2(1f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(1f, 0f);
            hintRect.anchoredPosition = new Vector2(-24f, 24f);
            hintRect.sizeDelta = new Vector2(280f, 36f);
            CursorHintText = hintObject.GetComponent<Text>();
            CursorHintText.font = fallbackFont;
            CursorHintText.fontSize = 18;
            CursorHintText.fontStyle = FontStyle.Bold;
            CursorHintText.alignment = TextAnchor.MiddleRight;
            CursorHintText.color = Color.white;
            CursorHintText.raycastTarget = false;
            CursorHintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            CursorHintText.verticalOverflow = VerticalWrapMode.Overflow;
            CursorHintText.text = "Esc  退出鼠标捕获";
            Outline outline = hintObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        void HideLegacyHealthUi()
        {
            Transform health = transform.Find("Health");
            if (health != null) health.gameObject.SetActive(false);
        }

        static void PositionTurboPanel(Transform child)
        {
            Transform current = child;
            while (current != null && current.name != "Turbo") current = current.parent;
            if (current is RectTransform panel)
            {
                panel.anchorMin = new Vector2(0.5f, 0f);
                panel.anchorMax = new Vector2(0.5f, 0f);
                panel.pivot = new Vector2(0.5f, 0f);
                panel.anchoredPosition = new Vector2(0f, 24f);
                panel.sizeDelta = new Vector2(252f, 42f);
                Image panelImage = panel.GetComponent<Image>();
                if (panelImage != null)
                    panelImage.color = new Color(0.05f, 0.05f, 0.055f, 0.56f);
            }
        }

        void BindRanking(BrawlGameManager _)
        {
            if (RankingRoot != null)
                RankingRoot.SetActive(false);
        }

        void ApplyPassTheBuckDumpStatusColor(bool dumpPhase)
        {
            if (StatusText == null) return;
            if (!hasStatusTextBase)
            {
                statusTextBase = StatusText.color;
                hasStatusTextBase = true;
            }

            if (!dumpPhase)
            {
                StatusText.color = statusTextBase;
                return;
            }

            float pulse = Mathf.PingPong(Time.unscaledTime * WarningBlinkSpeed, 1f);
            StatusText.color = Color.Lerp(TimerWarningColor, TimerWarningFlashColor, pulse);
        }

        void ApplyTimerWarning(bool playing, float remaining)
        {
            bool warning = playing && remaining > 0f && remaining <= WarningSeconds;
            Color textColor = TimerNormalColor;
            Color ringColor = timerRingBase;
            Color fillColor = timerFillBase;

            if (warning)
            {
                float pulse = Mathf.PingPong(Time.unscaledTime * WarningBlinkSpeed, 1f);
                textColor = Color.Lerp(TimerWarningColor, TimerWarningFlashColor, pulse);
                ringColor = Color.Lerp(TimerWarningColor, TimerWarningFlashColor, pulse);
                fillColor = Color.Lerp(new Color(0.35f, 0.08f, 0.08f, 0.96f), new Color(0.55f, 0.12f, 0.08f, 0.96f), pulse);

                int second = Mathf.CeilToInt(remaining);
                if (second != lastBeepSecond)
                {
                    lastBeepSecond = second;
                    PlayDidiBeep(second);
                }
            }
            else
            {
                lastBeepSecond = -1;
            }

            if (TimerText != null) TimerText.color = textColor;
            if (timerRing != null) timerRing.color = ringColor;
            if (timerFill != null) timerFill.color = fillColor;
        }

        void PlayDidiBeep(int second)
        {
            if (beepSource == null || beepClip == null) return;
            float pitch = second <= 3 ? 1.25f : 1f;
            beepSource.pitch = pitch;
            beepSource.PlayOneShot(beepClip, BeepVolume);
        }

        void CacheTimerVisuals()
        {
            if (TimerText == null) return;
            Transform parent = TimerText.transform.parent;
            if (parent == null) return;

            foreach (Image image in parent.GetComponentsInChildren<Image>(true))
            {
                if (image == null) continue;
                if (image.gameObject.name == "Ring")
                {
                    timerRing = image;
                    timerRingBase = image.color;
                }
                else if (image.gameObject.name == "Fill")
                {
                    timerFill = image;
                    timerFillBase = image.color;
                }
            }
        }

        void EnsureBeepSource()
        {
            beepSource = GetComponent<AudioSource>();
            if (beepSource == null)
                beepSource = gameObject.AddComponent<AudioSource>();
            beepSource.playOnAwake = false;
            beepSource.loop = false;
            beepSource.spatialBlend = 0f;
            beepClip = CreateDidiClip();
        }

        static AudioClip CreateDidiClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.09f;
            const float freq = 980f;
            int samples = Mathf.RoundToInt(sampleRate * duration);
            var clip = AudioClip.Create("DidiBeep", samples, 1, sampleRate, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float env = Mathf.Clamp01(1f - t / duration);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.45f;
            }

            clip.SetData(data, 0);
            return clip;
        }

        void CollectPlayers()
        {
            hudPlayers.Clear();
            foreach (NetFAnnequinController player in FindObjectsOfType<NetFAnnequinController>())
            {
                if (player != null) hudPlayers.Add(player);
            }

            foreach (NetPlayerMotor player in FindObjectsOfType<NetPlayerMotor>())
            {
                if (player != null && !hudPlayers.Contains(player))
                    hudPlayers.Add(player);
            }

            hudPlayers.Sort((a, b) => a.NetId.CompareTo(b.NetId));
        }

        void ApplyCjkFont()
        {
            if (fontApplied) return;
            fontApplied = true;
            Font font = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Microsoft YaHei",
                "微软雅黑",
                "PingFang SC",
                "SimHei",
                "Arial"
            }, 18);
            if (font == null) return;

            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                if (text != null) text.font = font;
            }
        }

        static void SetFill(Image image, float amount)
        {
            if (image == null) return;
            RectTransform fill = image.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        static string TrimStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return "";
            int cut = status.IndexOf('|');
            return cut > 0 ? status.Substring(0, cut).Trim() : status;
        }

        static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
