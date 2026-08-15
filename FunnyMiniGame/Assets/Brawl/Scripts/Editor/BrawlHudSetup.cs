using Brawl;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 在当前场景 Canvas 下创建可编辑的 MatchHud 节点，并绑到 BrawlMatchHud。
    /// </summary>
    public static class BrawlHudSetup
    {
        const string HudRootName = "MatchHud";

        static readonly Color[] SlotColors =
        {
            new Color(0.36f, 0.68f, 0.90f),
            new Color(0.42f, 0.76f, 0.38f),
            new Color(0.93f, 0.55f, 0.22f),
            new Color(0.62f, 0.45f, 0.82f)
        };

        [InitializeOnLoadMethod]
        static void AutoCreateWhenMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (SceneManager.GetActiveScene().name != "MiniGame_01") return;
                if (Object.FindObjectOfType<BrawlMatchHud>() != null) return;
                Canvas canvas = Object.FindObjectOfType<Canvas>();
                if (canvas == null) return;
                BuildUnderCanvas(canvas);
            };
        }

        [MenuItem("Brawl/Setup Match HUD Under Canvas")]
        public static void SetupFromMenu()
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
                throw new System.Exception("当前场景没有 Canvas，请先打开 MiniGame_01。");

            BrawlMatchHud existing = canvas.GetComponentInChildren<BrawlMatchHud>(true);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            BuildUnderCanvas(canvas);
        }

        public static BrawlMatchHud BuildUnderCanvas(Canvas canvas)
        {
            Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "微软雅黑", "Arial" }, 18);

            var root = new GameObject(HudRootName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Create MatchHud");
            root.layer = 5;
            root.transform.SetParent(canvas.transform, false);
            Stretch(root.GetComponent<RectTransform>());

            var hud = root.AddComponent<BrawlMatchHud>();
            hud.Slots = new BrawlMatchHud.PlayerSlot[4];

            RectTransform top = CreateRect(root.transform, "TopBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(1600f, 120f));
            hud.Slots[1] = CreateSlot(top, "Slot_Player2", new Vector2(-620f, -8f), SlotColors[1], uiSprite, knob, font);
            hud.Slots[0] = CreateSlot(top, "Slot_Player1", new Vector2(-330f, -8f), SlotColors[0], uiSprite, knob, font);
            CreateTimer(top, hud, knob, font);
            hud.Slots[2] = CreateSlot(top, "Slot_Player3", new Vector2(330f, -8f), SlotColors[2], uiSprite, knob, font);
            hud.Slots[3] = CreateSlot(top, "Slot_Player4", new Vector2(620f, -8f), SlotColors[3], uiSprite, knob, font);

            hud.StatusText = CreateText(top, "Status", 20, TextAnchor.MiddleCenter, Color.white, font);
            SetRect(hud.StatusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(900f, 28f));
            hud.StatusText.text = "等待开局";

            CreateHealth(root.transform, hud, uiSprite, font);
            CreateControls(root.transform, font);
            CreateRanking(root.transform, hud, uiSprite, font);

            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(canvas.gameObject);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            EditorSceneManager.SaveScene(canvas.gameObject.scene);
            Selection.activeGameObject = root;
            Debug.Log("BRAWL_HUD: 已在 Canvas/MatchHud 下创建可编辑 UI 节点。");
            return hud;
        }

        static void CreateTimer(Transform parent, BrawlMatchHud hud, Sprite knob, Font font)
        {
            RectTransform timer = CreateRect(parent, "Timer", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 8f), new Vector2(118f, 118f));
            CreateImage(timer, "Ring", knob, new Color(0.82f, 0.84f, 0.86f, 0.95f), Vector2.zero, new Vector2(118f, 118f));
            CreateImage(timer, "Fill", knob, new Color(0.18f, 0.19f, 0.21f, 0.96f), Vector2.zero, new Vector2(102f, 102f));
            hud.TimerText = CreateText(timer, "Time", 34, TextAnchor.MiddleCenter, Color.white, font);
            hud.TimerText.fontStyle = FontStyle.Bold;
            hud.TimerText.text = "01:00";
            SetRect(hud.TimerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 50f));
        }

        static BrawlMatchHud.PlayerSlot CreateSlot(Transform parent, string name, Vector2 pos, Color barColor, Sprite uiSprite, Sprite knob, Font font)
        {
            var slot = new BrawlMatchHud.PlayerSlot();
            RectTransform root = CreateRect(parent, name, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, new Vector2(268f, 74f));
            slot.Root = root.gameObject;

            slot.Frame = CreateImage(root, "Frame", uiSprite, new Color(0.08f, 0.08f, 0.08f, 0.72f), Vector2.zero, new Vector2(268f, 74f));
            slot.Frame.type = Image.Type.Sliced;
            SetRect(slot.Frame.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            CreateImage(root, "Avatar", knob, new Color(0.62f, 0.64f, 0.67f, 1f), new Vector2(-98f, 0f), new Vector2(52f, 52f));

            slot.Name = CreateText(root, "Name", 18, TextAnchor.MiddleLeft, Color.white, font);
            slot.Name.fontStyle = FontStyle.Bold;
            slot.Name.text = name.Replace("Slot_", "").Replace("_", " ");
            SetRect(slot.Name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-66f, 12f), new Vector2(148f, 24f));

            Image barBack = CreateImage(root, "BarBack", uiSprite, new Color(0.16f, 0.16f, 0.16f, 0.95f), Vector2.zero, Vector2.zero);
            barBack.type = Image.Type.Sliced;
            SetRect(barBack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-66f, -12f), new Vector2(118f, 12f));

            slot.BarFill = CreateImage(barBack.transform, "BarFill", uiSprite, barColor, Vector2.zero, Vector2.zero);
            slot.BarFill.type = Image.Type.Sliced;
            RectTransform fill = slot.BarFill.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.pivot = new Vector2(0f, 0.5f);

            slot.Score = CreateText(root, "Score", 16, TextAnchor.MiddleLeft, Color.white, font);
            slot.Score.text = "0/99";
            SetRect(slot.Score.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(58f, -12f), new Vector2(60f, 22f));
            return slot;
        }

        static void CreateHealth(Transform parent, BrawlMatchHud hud, Sprite uiSprite, Font font)
        {
            RectTransform panel = CreateRect(parent, "Health", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -168f), new Vector2(268f, 108f));
            Image bg = CreateImage(panel, "Back", uiSprite, new Color(0f, 0f, 0f, 0.58f), Vector2.zero, Vector2.zero);
            bg.type = Image.Type.Sliced;
            Stretch(bg.rectTransform);

            hud.HealthTitle = CreateText(panel, "Title", 18, TextAnchor.MiddleLeft, Color.white, font);
            hud.HealthTitle.fontStyle = FontStyle.Bold;
            hud.HealthTitle.text = "血量";
            SetRect(hud.HealthTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(-28f, 24f));

            hud.HealthName = CreateText(panel, "Name", 16, TextAnchor.MiddleLeft, new Color(0.45f, 0.95f, 0.55f), font);
            hud.HealthName.fontStyle = FontStyle.Bold;
            hud.HealthName.text = "自己  P1";
            SetRect(hud.HealthName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(14f, -32f), new Vector2(-28f, 22f));

            Image barBack = CreateImage(panel, "BarBack", uiSprite, new Color(0.12f, 0.12f, 0.12f, 0.95f), Vector2.zero, Vector2.zero);
            barBack.type = Image.Type.Sliced;
            SetRect(barBack.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-28f, 18f));
            barBack.rectTransform.offsetMin = new Vector2(14f, 16f);
            barBack.rectTransform.offsetMax = new Vector2(-14f, 34f);

            hud.HealthFill = CreateImage(barBack.transform, "Fill", uiSprite, new Color(0.25f, 0.82f, 0.38f), Vector2.zero, Vector2.zero);
            RectTransform fill = hud.HealthFill.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.pivot = new Vector2(0f, 0.5f);

            hud.HealthValue = CreateText(barBack.transform, "Value", 14, TextAnchor.MiddleCenter, Color.white, font);
            hud.HealthValue.text = "100/100";
            Stretch(hud.HealthValue.rectTransform);
        }

        static void CreateControls(Transform parent, Font font)
        {
            Text hint = CreateText(parent, "Controls", 18, TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.88f), font);
            hint.text = "W S A D : Movement\nSpace : Jump\nLeft Click : Punch\nHold Right Click : Pick Up Laptop\nRelease Right Click : Put Down\nEsc : Release Mouse";
            SetRect(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 220f), new Vector2(460f, 170f));
        }

        static void CreateRanking(Transform parent, BrawlMatchHud hud, Sprite uiSprite, Font font)
        {
            RectTransform root = CreateRect(parent, "Ranking", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(440f, 280f));
            hud.RankingRoot = root.gameObject;
            Image bg = CreateImage(root, "Back", uiSprite, new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.zero);
            bg.type = Image.Type.Sliced;
            Stretch(bg.rectTransform);

            Text title = CreateText(root, "Title", 28, TextAnchor.MiddleCenter, Color.white, font);
            title.fontStyle = FontStyle.Bold;
            title.text = "本局排名";
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(400f, 40f));

            hud.RankingBody = CreateText(root, "Body", 22, TextAnchor.UpperCenter, Color.white, font);
            SetRect(hud.RankingBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(400f, 190f));
            root.gameObject.SetActive(false);
        }

        static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            SetRect(rt, anchorMin, anchorMax, pivot, pos, size);
            return rt;
        }

        static Image CreateImage(Transform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            RectTransform rt = CreateRect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text CreateText(Transform parent, string name, int size, TextAnchor align, Color color, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
