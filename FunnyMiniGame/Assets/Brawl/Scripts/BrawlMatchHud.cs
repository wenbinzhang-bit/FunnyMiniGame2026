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

        void Awake()
        {
            HideLegacyHealthUi();
            EnsureTurboVisuals();
            EnsureCursorHint();
            EnsureNextRoundButton();
            ApplyCjkFont();
            CacheTimerVisuals();
            EnsureBeepSource();
            if (RankingRoot != null)
                RankingRoot.SetActive(false);
        }

        void LateUpdate()
        {
            if (!NetworkClient.active && !NetworkServer.active) return;
            Refresh();
        }

        void Refresh()
        {
            BrawlGameManager gm = BrawlGameManager.Instance;
            int scoreMax = gm != null ? Mathf.Max(1, gm.HudScoreMax) : ScoreBarMaxFallback;
            CollectPlayers();

            float remaining = gm != null ? gm.HudRemainingSeconds : 0f;
            if (TimerText != null)
                TimerText.text = FormatTime(remaining);

            ApplyTimerWarning(gm != null && (gm.HudIsPlaying || gm.HudIsWaiting || gm.HudIsRoundEnd), remaining);

            if (StatusText != null)
            {
                if (gm != null && gm.HudIsWaiting)
                    StatusText.text = $"空气墙倒计时 {FormatTime(remaining)} 后消失";
                else if (gm != null && gm.HudIsRoundEnd)
                    StatusText.text = gm.HudContinueRequested
                        ? "已确认下一局，继续当前玩法"
                        : $"点击「下一局」继续，否则 {FormatTime(remaining)} 后结束";
                else
                    StatusText.text = TrimStatus(gm != null ? gm.HudStatusText : "");
            }

            int slotCount = Slots != null ? Slots.Length : 0;
            for (int i = 0; i < slotCount; i++)
                BindSlot(Slots[i], i, i < hudPlayers.Count ? hudPlayers[i] : null, scoreMax);

            BindLocalTurbo();
            BindRanking(gm);
            BindNextRoundButton(gm);
            BindCursorHint();
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
            bool show = gm != null && gm.HudIsRoundEnd;
            if (NextRoundButton != null)
                NextRoundButton.gameObject.SetActive(show);
            if (!show || NextRoundLabel == null) return;

            bool requested = gm.HudContinueRequested;
            NextRoundButton.interactable = !requested;
            NextRoundLabel.text = requested ? "已确认" : "下一局";
        }

        void OnNextRoundClicked()
        {
            if (BrawlGameManager.Instance != null)
                BrawlGameManager.Instance.RequestNextRound();
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
            buttonRect.anchoredPosition = new Vector2(0f, -150f);
            buttonRect.sizeDelta = new Vector2(220f, 56f);
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
                if (controlsRect.sizeDelta.y < 196f)
                    controlsRect.sizeDelta = new Vector2(Mathf.Max(460f, controlsRect.sizeDelta.x), 196f);
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
            bool show = gm != null && gm.HudIsRoundEnd;
            if (RankingRoot != null) RankingRoot.SetActive(show);
            if (!show || RankingBody == null) return;

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
