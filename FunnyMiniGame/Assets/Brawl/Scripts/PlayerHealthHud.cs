using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 窗口左侧血量面板:自己排最前,其余玩家按 netId 排列。
    /// </summary>
    public class PlayerHealthHud : MonoBehaviour
    {
        const float PanelX = 16f;
        const float PanelY = 270f;
        const float PanelWidth = 268f;
        const float RowHeight = 52f;
        const float BarHeight = 16f;

        readonly List<PlayerAttributes> cached = new List<PlayerAttributes>();
        float nextRefresh;
        Texture2D pixel;

        void Awake()
        {
            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        void OnDestroy()
        {
            if (pixel != null) Destroy(pixel);
        }

        void RefreshIfNeeded()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            cached.Clear();
            cached.AddRange(FindObjectsOfType<PlayerAttributes>());
            cached.Sort((a, b) =>
            {
                if (a == null || b == null) return 0;
                if (a.isLocalPlayer != b.isLocalPlayer)
                    return a.isLocalPlayer ? -1 : 1;
                return a.netId.CompareTo(b.netId);
            });
        }

        void OnGUI()
        {
            // 已替换为 BrawlMatchHud 的 UGUI，不再使用 OnGUI。
        }

        void DisabledOnGUI()
        {
            RefreshIfNeeded();
            cached.RemoveAll(p => p == null);
            if (cached.Count == 0) return;

            float height = 36f + cached.Count * RowHeight + 10f;
            DrawRect(new Rect(PanelX, PanelY, PanelWidth, height), new Color(0f, 0f, 0f, 0.55f));

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            title.normal.textColor = Color.white;
            GUI.Label(new Rect(PanelX + 12f, PanelY + 6f, PanelWidth - 24f, 24f), "血量", title);

            var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            var hpStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleRight
            };

            for (int i = 0; i < cached.Count; i++)
            {
                PlayerAttributes attr = cached[i];
                float rowY = PanelY + 32f + i * RowHeight;
                bool mine = attr.isLocalPlayer;
                bool dead = attr.IsDead;
                float fill = Mathf.Clamp01(attr.HealthNormalized);

                nameStyle.normal.textColor = mine ? new Color(0.45f, 0.95f, 0.55f) : Color.white;
                string name = mine ? BrawlHudNames.LocalLabel(attr.netId) : BrawlHudNames.Label(attr.netId);
                GUI.Label(new Rect(PanelX + 12f, rowY, 150f, 20f), name, nameStyle);

                hpStyle.normal.textColor = dead ? new Color(1f, 0.4f, 0.35f) : new Color(1f, 1f, 1f, 0.9f);
                string hpText = dead ? "倒下" : $"{attr.CurrentHealth}/{attr.MaxHealth}";
                GUI.Label(new Rect(PanelX + 150f, rowY, PanelWidth - 166f, 20f), hpText, hpStyle);

                Rect bar = new Rect(PanelX + 12f, rowY + 24f, PanelWidth - 24f, BarHeight);
                DrawRect(bar, new Color(0.12f, 0.12f, 0.12f, 0.9f));
                if (fill > 0f)
                {
                    Color barColor = dead
                        ? new Color(0.45f, 0.12f, 0.1f)
                        : fill <= 0.3f
                            ? new Color(0.9f, 0.25f, 0.2f)
                            : mine
                                ? new Color(0.25f, 0.85f, 0.4f)
                                : new Color(0.3f, 0.7f, 0.95f);
                    DrawRect(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * fill, bar.height - 2f), barColor);
                }
            }
        }

        void DrawRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = old;
        }
    }
}
