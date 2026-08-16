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
    public sealed class BrawlMatchHud : MonoBehaviour
    {
        const int ScoreBarMaxFallback = 99;

        [Serializable]
        public sealed class PlayerSlot
        {
            public GameObject Root;
            public Text Name;
            public Text Score;
            public Image BarFill;
            public Image Frame;
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
        public GameObject RulesRoot;
        public Text RulesTitle;
        public Text RulesBody;
        public Text RulesCountdown;
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
        float displayedTurbo = 1f;
        bool hasDisplayedTurbo;
        string lastHudScene;
        int lastMatchSeq = -1;

        void Awake()
        {
            HideLegacyHealthUi();
            EnsureTurboVisuals();
            EnsureCursorHint();
            EnsureNextRoundButton();
            EnsureLobbyButton();
            EnsureRulesPanel();
            EnsureDebugTimerButton();
            ApplyCjkFont();
            CacheTimerVisuals();
            EnsureBeepSource();
            if (RankingRoot != null)
                RankingRoot.SetActive(false);
        }

        void LateUpdate()
        {
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

            if (StatusText != null)
            {
                if (gm != null && gm.HudIsLobby)
                    StatusText.text = string.IsNullOrEmpty(gm.HudStatusText)
                        ? "大厅等待加入，全员准备后进入第一关"
                        : TrimStatus(gm.HudStatusText);
                else if (gm != null && gm.HudIsShowingRules)
                    StatusText.text = $"请阅读{gm.HudRulesTitle}，{Mathf.CeilToInt(remaining)} 秒后开始";
                else if (gm != null && gm.HudIsWaiting)
                    StatusText.text = string.IsNullOrEmpty(gm.HudStatusText)
                        ? $"空气墙倒计时 {FormatTime(remaining)} 后正式开始"
                        : TrimStatus(gm.HudStatusText);
                else if (gm != null && gm.HudIsFinalKpi)
                    StatusText.text = "2 关全部结束，这是整场 KPI 汇总";
                else if (gm != null && gm.HudIsRoundEnd)
                    StatusText.text = gm.HudContinueRequested
                        ? (gm.HudHasNextLevel ? "已确认下一关" : "已确认查看总成绩")
                        : $"点击「{(gm.HudHasNextLevel ? "下一关" : "查看总成绩")}」或按 Enter 继续，否则 {FormatTime(remaining)} 后自动继续";
                else
                    StatusText.text = TrimStatus(gm != null ? gm.HudStatusText : "");
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

            int score = player != null ? player.Score : 0;
            if (slot.Name != null)
                slot.Name.text = player != null ? BrawlHudNames.Label(player.NetId, hudPlayers) : $"Player {index + 1}";
            if (slot.Score != null) slot.Score.text = $"{score}/{scoreMax}";
            if (slot.BarFill != null)
            {
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
            DebugTimerButton.transform.SetAsLastSibling();
        }

        void OnDebugTimerClicked()
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.DebugSetRemainingSeconds(10f);
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
            DebugTimerLabel.text = "当局剩10秒";
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
            SetLobbyButtonActive(LobbyButton, show);
            SetLobbyButtonActive(LobbyStartButton, show);
            HideLooseLobbyButtons(show);
            if (!show) return;

            bool ready = gm.HudLocalIsReady();
            if (LobbyLabel != null)
                LobbyLabel.text = ready ? "取消准备" : "准备 Ready";
            Image readyImage = LobbyButton != null ? LobbyButton.GetComponent<Image>() : null;
            if (readyImage != null)
                readyImage.color = ready
                    ? new Color(0.42f, 0.46f, 0.5f, 0.96f)
                    : new Color(0.18f, 0.55f, 0.92f, 0.96f);
            if (LobbyButton != null)
            {
                LobbyButton.interactable = true;
                LobbyButton.transform.SetAsLastSibling();
            }

            bool allReady = gm.HudLobbyAllReady;
            if (LobbyStartLabel != null)
                LobbyStartLabel.text = allReady ? "开始游戏" : "等待全员准备";
            Image startImage = LobbyStartButton != null ? LobbyStartButton.GetComponent<Image>() : null;
            if (startImage != null)
                startImage.color = allReady
                    ? new Color(0.16f, 0.72f, 0.38f, 0.96f)
                    : new Color(0.35f, 0.38f, 0.42f, 0.88f);
            if (LobbyStartButton != null)
            {
                LobbyStartButton.interactable = true;
                LobbyStartButton.transform.SetAsLastSibling();
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

            if (LobbyButton == null || LobbyLabel == null)
            {
                BindOrCreateLobbyButton(
                    "LobbyAction",
                    new Vector2(-130f, -150f),
                    new Color(0.18f, 0.55f, 0.92f, 0.96f),
                    "准备 Ready",
                    OnLobbyReadyClicked,
                    fallbackFont,
                    out LobbyButton,
                    out LobbyLabel);
            }

            if (LobbyStartButton == null || LobbyStartLabel == null)
            {
                BindOrCreateLobbyButton(
                    "LobbyStart",
                    new Vector2(130f, -150f),
                    new Color(0.16f, 0.72f, 0.38f, 0.96f),
                    "开始游戏",
                    OnLobbyStartClicked,
                    fallbackFont,
                    out LobbyStartButton,
                    out LobbyStartLabel);
            }
        }

        void BindOrCreateLobbyButton(
            string name,
            Vector2 position,
            Color color,
            string text,
            UnityEngine.Events.UnityAction onClick,
            Font font,
            out Button button,
            out Text label)
        {
            button = null;
            label = null;
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

        void BindRulesPanel(BrawlGameManager gm)
        {
            bool show = gm != null && gm.HudIsShowingRules;
            if (RulesRoot != null)
                RulesRoot.SetActive(show);
            if (!show) return;

            if (RulesRoot != null)
                RulesRoot.transform.SetAsLastSibling();

            if (RulesTitle != null)
                RulesTitle.text = gm.HudRulesTitle;
            if (RulesBody != null && !string.IsNullOrEmpty(gm.HudRulesBody))
                RulesBody.text = gm.HudRulesBody;

            int seconds = Mathf.Max(1, Mathf.CeilToInt(gm.HudRemainingSeconds));
            if (RulesCountdown != null)
            {
                RulesCountdown.text = $"{seconds} 秒后进入空气墙等待区";
                float pulse = Mathf.PingPong(Time.unscaledTime * 3.2f, 1f);
                RulesCountdown.color = Color.Lerp(new Color(1f, 0.84f, 0.28f, 1f), Color.white, pulse);
            }
        }

        void EnsureRulesPanel()
        {
            if (RulesRoot != null && RulesBody != null && RulesCountdown != null)
            {
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
            RulesBody.text =
                "抱住笔记本电脑并坚持不放，就能持续得分。\n" +
                "被拳头打中会丢掉电脑，自己也会被打飞。\n" +
                "先到 99 分，或时间结束时按分数排名。\n" +
                "掉出场地会送回出生点，不会淘汰。\n\n" +
                "WASD 移动　　空格 跳跃　　Shift 加速\n" +
                "左键 出拳　　按住右键 抱起电脑　　松开右键 放下\n" +
                "Esc 释放鼠标　　Alt 重新捕获鼠标\n\n" +
                "开局有空气墙，倒计时结束后撤墙，正式开打。";

            RulesCountdown = CreatePlainText(cardRect, "Countdown", fallbackFont, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.28f, 1f));
            SetHudRect(RulesCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(560f, 36f));
            RulesCountdown.text = "6 秒后自动开始";

            root.SetActive(false);
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

        void EnsureCursorHint()
        {
            const string controlsHint =
                "W S A D : Movement\nSpace : Jump\nLeft Click : Punch\nHold Right Click : Pick Up Laptop\nRelease Right Click : Put Down\nEsc : 退出鼠标捕获\nAlt : 重新捕获鼠标";

            if (ControlsText == null)
            {
                Transform controls = transform.Find("Controls");
                if (controls != null) ControlsText = controls.GetComponent<Text>();
            }

            if (ControlsText != null)
            {
                ControlsText.text = controlsHint;
                RectTransform controlsRect = ControlsText.rectTransform;
                controlsRect.anchoredPosition = new Vector2(24f, 140f);
                if (controlsRect.sizeDelta.y < 196f)
                    controlsRect.sizeDelta = new Vector2(Mathf.Max(460f, controlsRect.sizeDelta.x), 196f);
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
                panel.anchoredPosition = new Vector2(24f, -168f);
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
