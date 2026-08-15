using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Brawl
{
    /// <summary>
    /// MiniGame_01 对局 HUD。只刷新场景 Canvas 下已摆好的 UGUI 节点，不在运行时创建界面。
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

        [Header("Local Health")]
        public Text HealthTitle;
        public Text HealthName;
        public Text HealthValue;
        public Image HealthFill;

        [Header("Other")]
        public GameObject RankingRoot;
        public Text RankingBody;

        [Header("Colors")]
        public Color IdleFrameColor = new Color(0.08f, 0.08f, 0.08f, 0.72f);
        public Color HoldingFrameColor = new Color(1f, 0.86f, 0.28f, 0.95f);
        public Color HealthOkColor = new Color(0.25f, 0.82f, 0.38f);
        public Color HealthLowColor = new Color(0.9f, 0.25f, 0.2f);
        public Color HealthDeadColor = new Color(0.55f, 0.16f, 0.14f);
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

        void Awake()
        {
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

            ApplyTimerWarning(gm != null && gm.HudIsPlaying, remaining);

            if (StatusText != null)
                StatusText.text = TrimStatus(gm != null ? gm.HudStatusText : "");

            int slotCount = Slots != null ? Slots.Length : 0;
            for (int i = 0; i < slotCount; i++)
                BindSlot(Slots[i], i, i < hudPlayers.Count ? hudPlayers[i] : null, scoreMax);

            BindLocalHealth();
            BindRanking(gm);
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

        void BindLocalHealth()
        {
            PlayerAttributes local = null;
            uint netId = 0;
            for (int i = 0; i < hudPlayers.Count; i++)
            {
                if (hudPlayers[i] is NetworkBehaviour nb && nb.isLocalPlayer)
                {
                    local = hudPlayers[i].Attributes;
                    netId = hudPlayers[i].NetId;
                    break;
                }
            }

            if (local == null)
            {
                if (HealthName != null) HealthName.text = "等待加入";
                if (HealthValue != null) HealthValue.text = "--/--";
                SetFill(HealthFill, 0f);
                return;
            }

            if (HealthTitle != null) HealthTitle.text = "血量";
            if (HealthName != null)
            {
                HealthName.text = BrawlHudNames.LocalLabel(netId);
                HealthName.color = new Color(0.45f, 0.95f, 0.55f);
            }

            if (HealthValue != null)
                HealthValue.text = local.IsDead ? "倒下" : $"{local.CurrentHealth}/{local.MaxHealth}";

            if (HealthFill != null)
            {
                HealthFill.color = local.IsDead
                    ? HealthDeadColor
                    : local.HealthNormalized <= 0.3f
                        ? HealthLowColor
                        : HealthOkColor;
                SetFill(HealthFill, local.HealthNormalized);
            }
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
