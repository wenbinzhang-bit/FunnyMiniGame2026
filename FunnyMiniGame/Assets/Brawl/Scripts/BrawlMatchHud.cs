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

        static readonly Color[] SlotBarColors =
        {
            new Color(0.20f, 0.66f, 0.92f, 1f),
            new Color(0.26f, 0.72f, 0.40f, 1f),
            new Color(0.94f, 0.72f, 0.12f, 1f),
            new Color(0.84f, 0.32f, 0.24f, 1f)
        };

        static readonly string[] PlayerAvatarResources =
        {
            "UI/PlayerAvatars/Player01_RedDress",
            "UI/PlayerAvatars/Player02_WhiteShirtTie",
            "UI/PlayerAvatars/Player03_GreenPlaid",
            "UI/PlayerAvatars/Player04_StripedHeavy"
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
        public GameObject RulesRoot;
        public Text RulesTitle;
        public Text RulesBody;
        public Text RulesCountdown;
        public RawImage RulesArtwork;
        public Button DebugTimerButton;
        public Text DebugTimerLabel;

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
        Sprite[] playerAvatarSprites;
        Sprite emptyAvatarSprite;
        int lobbyBotSelection = 1;
        string lobbyTransientStatus;
        float lobbyTransientStatusUntil;
        Image rulesCountdownBackdrop;

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
            EnsureLobbyButton();
            EnsureLobbyReadyPanel();
            EnsureRulesPanel();
            EnsureDebugTimerButton();
            ApplyHudVisualStyle();
        }

        void ShowEditorPreviewWidgets()
        {
            if (RankingRoot != null)
                RankingRoot.SetActive(true);
            if (NextRoundButton != null)
                NextRoundButton.gameObject.SetActive(true);
            if (LobbyButton != null)
                LobbyButton.gameObject.SetActive(true);
            if (LobbyStartButton != null)
                LobbyStartButton.gameObject.SetActive(false);
            if (LobbyReadyRoot != null)
                LobbyReadyRoot.SetActive(true);
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
            if (ControlsText != null)
                ControlsText.gameObject.SetActive(online && !showRules);
            if (!online) return;
            ResetHudIfSceneChanged();
            Refresh();

            if (gm != null && gm.HudShowLobbyActions
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                gm.RequestLobbyReadyToggle();

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
            if (RulesRoot != null)
                RulesRoot.SetActive(false);
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
                StatusText.gameObject.SetActive(!launcherLobby);
                if (gm != null && gm.HudIsLobby)
                    StatusText.text = string.IsNullOrEmpty(gm.HudStatusText)
                        ? "大厅等待加入，全员准备后进入第一关"
                        : TrimStatus(gm.HudStatusText);
                else if (gm != null && gm.HudIsShowingRules)
                    StatusText.text = $"请阅读{gm.HudRulesTitle}，{Mathf.CeilToInt(remaining)} 秒后开始";
                else if (gm != null && gm.HudIsWaiting)
                    StatusText.text = string.IsNullOrEmpty(gm.HudStatusText)
                        ? "空气墙等待中，结束后正式开始"
                        : TrimStatus(gm.HudStatusText);
                else if (gm != null && gm.HudIsFinalKpi)
                    StatusText.text = "2 关全部结束，这是整场 KPI 汇总";
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
            BindNextRoundButton(gm);
            BindLobbyButton(gm);
            BindRulesPanel(gm);
            BindDebugTimerButton(gm);
            BindCursorHint();
            if (NextRoundButton != null && NextRoundButton.gameObject.activeSelf)
                NextRoundButton.transform.SetAsLastSibling();
        }

        void BindSlot(PlayerSlot slot, int index, IBrawlPlayer player, int scoreMax)
        {
            if (slot == null) return;
            if (slot.Root != null) slot.Root.SetActive(true);

            EnsureSlotVisualReferences(slot);
            EnsurePlayerAvatarSprites();

            int score = player != null ? player.Score : 0;
            if (slot.Name != null)
                slot.Name.text = player != null ? BrawlHudNames.Label(player.NetId, hudPlayers) : $"Player {index + 1}";
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
                slot.Frame.color = holding ? HoldingFrameColor : IdleFrameColor;
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
                    int avatarIndex = ResolvePlayerAvatarIndex(player, index);
                    if (playerAvatarSprites != null && avatarIndex >= 0 && avatarIndex < playerAvatarSprites.Length)
                        slot.Avatar.sprite = playerAvatarSprites[avatarIndex];
                    slot.Avatar.color = Color.white;
                }
                slot.Avatar.preserveAspect = true;
            }
        }

        int ResolvePlayerAvatarIndex(IBrawlPlayer player, int fallbackIndex)
        {
            string actorName = player?.Transform != null ? player.Transform.name : string.Empty;
            for (int i = 0; i < PlayerAvatarResources.Length; i++)
            {
                if (actorName.IndexOf($"FAnnequinV2_New{i + 1}", StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            return Mathf.Clamp(fallbackIndex, 0, PlayerAvatarResources.Length - 1);
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
            if (slot.Frame == null) slot.Frame = root.Find("Frame")?.GetComponent<Image>();
            if (slot.Name == null) slot.Name = root.Find("Name")?.GetComponent<Text>();
            if (slot.Score == null) slot.Score = root.Find("Score")?.GetComponent<Text>();
            if (slot.BarFill == null) slot.BarFill = root.Find("BarBack/BarFill")?.GetComponent<Image>();
        }

        void EnsurePlayerAvatarSprites()
        {
            if (playerAvatarSprites != null && playerAvatarSprites.Length == PlayerAvatarResources.Length) return;
            playerAvatarSprites = new Sprite[PlayerAvatarResources.Length];
            for (int i = 0; i < PlayerAvatarResources.Length; i++)
                playerAvatarSprites[i] = Resources.Load<Sprite>(PlayerAvatarResources[i]);
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

            NextRoundButton.interactable = true;
            if (NextRoundButton.transform is RectTransform nextRect)
                nextRect.anchoredPosition = new Vector2(0f, -240f);
            NextRoundButton.transform.SetAsLastSibling();
            NextRoundLabel.text = gm.HudHasNextLevel ? "下一关" : "查看总成绩";
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
            if (LobbyReadyRoot != null)
                LobbyReadyRoot.SetActive(show);
            SetLobbyButtonActive(LobbyButton, show);
            SetLobbyButtonActive(LobbyStartButton, false);
            HideLooseLobbyButtons(show);
            if (!show) return;

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

            bool showTransient = Time.unscaledTime < lobbyTransientStatusUntil && !string.IsNullOrEmpty(lobbyTransientStatus);
            if (LobbyReadyStatus != null)
            {
                string readyLine = string.IsNullOrEmpty(gm.HudLobbyReadyLine) ? "已准备 0/0" : gm.HudLobbyReadyLine;
                LobbyReadyStatus.text = showTransient
                    ? lobbyTransientStatus
                    : gm.HudLobbyAllReady
                        ? readyLine + "    全员已准备，正在进入第一关"
                        : readyLine + "    全员准备后进入第一关";
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
        }

        static void SetLobbyButtonActive(Button button, bool show)
        {
            if (button != null)
                button.gameObject.SetActive(show);
        }

        void HideLooseLobbyButtons(bool show)
        {
            HideNamedChild("LobbyAction", show ? LobbyButton : null);
            HideNamedChild("LobbyStart", show ? LobbyStartButton : null);
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
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.RequestLobbyStart();
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

            if (LobbyStartButton != null)
                LobbyStartButton.gameObject.SetActive(false);
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
            LobbyReadyStatus.text = "已准备 1/2    全员准备后进入第一关";

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

            if (LobbyButton != null)
            {
                RectTransform readyRect = LobbyButton.transform as RectTransform;
                readyRect.SetParent(readyFrameTransform, false);
                SetHudRect(readyRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8f, -8f));
                Image readyImage = LobbyButton.GetComponent<Image>();
                if (readyImage != null)
                {
                    readyImage.color = new Color(0.92f, 0.70f, 0.04f, 0.96f);
                    EnsureGraphicOutline(readyImage, new Color(0.06f, 0.06f, 0.05f, 0.95f), 1f);
                }
                if (LobbyLabel != null)
                {
                    LobbyLabel.fontSize = 30;
                    LobbyLabel.fontStyle = FontStyle.Bold;
                    LobbyLabel.color = new Color(0.08f, 0.07f, 0.04f, 1f);
                    DisableTextOutline(LobbyLabel);
                }
            }

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
            return BrawlLevelCatalog.GetLevelIndex(BrawlLevelCatalog.ActiveSceneName()) == 0
                && RulesArtwork != null
                && RulesArtwork.texture != null;
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
            RulesArtwork.texture = Resources.Load<Texture2D>(Level01RulesArtworkResource);
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
            EnsurePlayerAvatarSprites();
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

        void BindRanking(BrawlGameManager gm)
        {
            bool showRound = gm != null && gm.HudIsRoundEnd;
            bool showFinal = gm != null && gm.HudIsFinalKpi;
            bool show = showRound || showFinal;
            if (RankingRoot != null)
            {
                RankingRoot.SetActive(show);
                foreach (Graphic graphic in RankingRoot.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic != null)
                        graphic.raycastTarget = false;
                }
            }
            if (!show || RankingBody == null) return;

            Text title = RankingRoot != null ? RankingRoot.transform.Find("Title")?.GetComponent<Text>() : null;
            if (title != null)
                title.text = showFinal ? "整场 KPI 汇总" : "本局排名";

            if (showFinal)
            {
                RankingBody.text = string.IsNullOrEmpty(gm.HudKpiBoardText) ? "还没有成绩" : gm.HudKpiBoardText;
                return;
            }

            var ordered = new List<IBrawlPlayer>(hudPlayers);
            ordered.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp != 0 ? cmp : a.NetId.CompareTo(b.NetId);
            });

            var lines = new List<string>();
            int rank = 1;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0 && ordered[i].Score < ordered[i - 1].Score)
                    rank = i + 1;
                lines.Add($"第{rank}名    {BrawlHudNames.Label(ordered[i].NetId, hudPlayers)}    {ordered[i].Score}分");
            }

            RankingBody.text = lines.Count == 0 ? "无人参赛" : string.Join("\n", lines);
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
