using Brawl;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 把散落在场景里的网络/对局节点收进 BrawlSession Prefab，切关不销毁。
    /// </summary>
    public static class BrawlSessionSetup
    {
        public const string PrefabPath = "Assets/Brawl/Prefabs/BrawlSession.prefab";
        const string ResourcesPrefabPath = "Assets/Brawl/Resources/BrawlSession.prefab";
        const string NetworkPrefabPath = "Assets/Brawl/Prefabs/Network.prefab";
        const string MatchHudPrefabPath = "Assets/Brawl/Prefabs/MatchHud.prefab";

        [InitializeOnLoadMethod]
        static void AutoPackWhenMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                    BuildPrefab();
                if (SceneManager.GetActiveScene().name != BrawlLevelCatalog.LauncherScene) return;
                if (Object.FindObjectOfType<BrawlSession>() != null) return;
                PlaceSessionInActiveScene();
            };
        }

        [MenuItem("Brawl/Pack Persistent Session Prefab")]
        public static void PackFromMenu()
        {
            PackActiveScene();
        }

        public static GameObject PackActiveScene()
        {
            GameObject prefab = BuildPrefab();
            if (SceneManager.GetActiveScene().name == BrawlLevelCatalog.LauncherScene)
                PlaceSessionInActiveScene();
            Debug.Log("BRAWL_SESSION: 已更新常驻 Session Prefab。关卡场景不再放置这份节点。");
            return prefab;
        }

        public static void PlaceSessionInActiveScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                prefab = BuildPrefab();
            if (Object.FindObjectOfType<BrawlSession>() != null) return;

            Scene scene = SceneManager.GetActiveScene();
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static GameObject BuildPrefab()
        {
            BrawlGameManager existingGm = Object.FindObjectOfType<BrawlGameManager>();
            BrawlBootstrap existingBoot = Object.FindObjectOfType<BrawlBootstrap>();

            var root = new GameObject("BrawlSession");
            root.AddComponent<BrawlSession>();
            root.AddComponent<BrawlRunRecord>();

            GameObject networkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPrefabPath);
            if (networkPrefab == null)
                throw new System.Exception("BRAWL_SESSION: 缺少 " + NetworkPrefabPath);

            var network = (GameObject)PrefabUtility.InstantiatePrefab(networkPrefab, root.transform);
            network.name = "Network";
            NetworkManager manager = network.GetComponent<NetworkManager>();
            if (manager != null)
                manager.dontDestroyOnLoad = false;
            if (network.GetComponent<MiniGameNetworkHook>() == null)
                network.AddComponent<MiniGameNetworkHook>();

            var bootstrap = new GameObject("Bootstrap");
            bootstrap.transform.SetParent(root.transform, false);
            var boot = bootstrap.AddComponent<BrawlBootstrap>();
            boot.FixedTimeStep = existingBoot != null ? existingBoot.FixedTimeStep : 0.02f;
            boot.GravityY = existingBoot != null ? existingBoot.GravityY : -9.81f;

            var gmObject = new GameObject("GameManager");
            gmObject.transform.SetParent(root.transform, false);
            gmObject.AddComponent<NetworkIdentity>();
            var gm = gmObject.AddComponent<BrawlGameManager>();
            CopyGameManager(existingGm, gm);

            CreateHudCanvas(root.transform);
            CreateEventSystem(root.transform);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (AssetDatabase.IsValidFolder("Assets/Brawl/Resources"))
                PrefabUtility.SaveAsPrefabAsset(root, ResourcesPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return saved;
        }

        static void ReplaceSceneObjects(Scene scene, GameObject prefab)
        {
            DestroySceneObject(Object.FindObjectOfType<BrawlSession>());
            DestroySceneObject(Object.FindObjectOfType<BrawlNetworkManager>());
            DestroySceneObject(Object.FindObjectOfType<BrawlBootstrap>());
            DestroySceneObject(Object.FindObjectOfType<BrawlGameManager>());
            DestroySceneObject(Object.FindObjectOfType<BrawlMatchHud>());

            EventSystem sceneEvent = Object.FindObjectOfType<EventSystem>();
            if (sceneEvent != null)
                Object.DestroyImmediate(sceneEvent.gameObject);

            PrefabUtility.InstantiatePrefab(prefab, scene);
        }

        static void CopyGameManager(BrawlGameManager from, BrawlGameManager to)
        {
            if (from == null || to == null) return;
            to.KillY = from.KillY;
            to.SpectatorIsland = from.SpectatorIsland;
            to.RoundRestartDelay = from.RoundRestartDelay;
            to.ContinueDecisionSeconds = from.ContinueDecisionSeconds;
            to.RoundDurationSeconds = from.RoundDurationSeconds;
            to.HoldScoreInterval = from.HoldScoreInterval;
            to.HoldScorePoints = from.HoldScorePoints;
            to.MinPlayersToStart = from.MinPlayersToStart;
            to.HudScoreMax = from.HudScoreMax;
            to.WaitingDurationSeconds = from.WaitingDurationSeconds;
            to.RulesDurationSeconds = from.RulesDurationSeconds;
            to.BuckPenalty = from.BuckPenalty;
            to.CatchStunSeconds = from.CatchStunSeconds;
            to.ThrowSpeed = from.ThrowSpeed;
            to.AirWall = null;
        }

        static void CreateHudCanvas(Transform parent)
        {
            var canvasObject = new GameObject("HudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(parent, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1366f, 768f);
            scaler.matchWidthOrHeight = 0.556f;

            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MatchHudPrefabPath);
            if (hudPrefab == null)
            {
                BrawlHudSetup.BuildUnderCanvas(canvas);
                return;
            }

            var hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, canvasObject.transform);
            hud.name = "MatchHud";
            if (hud.transform is RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
            }
        }

        static void CreateEventSystem(Transform parent)
        {
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        static void DestroySceneObject(Component component)
        {
            if (component == null) return;
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(component.gameObject);
            Object.DestroyImmediate(root != null ? root : component.gameObject);
        }
    }
}
