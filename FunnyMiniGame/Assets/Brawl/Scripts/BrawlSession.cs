using FIMSpace.FProceduralAnimation;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Brawl
{
    /// <summary>
    /// 跨关卡常驻根节点：网络、对局、HUD、输入都挂在这棵树上，切场景不销毁。
    /// 空气墙、出生点、电脑等关卡物体仍留在各小关场景里，切关后自动重绑。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class BrawlSession : MonoBehaviour
    {
        public static BrawlSession Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (FindObjectOfType<BrawlSession>() != null) return;

            GameObject prefab = Resources.Load<GameObject>("BrawlSession");
            if (prefab != null)
            {
                Instantiate(prefab);
                return;
            }

            var go = new GameObject("BrawlSession");
            go.AddComponent<BrawlSession>().BuildRuntimeFallback();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            BrawlRunRecord.Ensure(transform);
            ConfigureNetworkManager();
            AdoptLooseObjects();
            BindScene();
            BindHudCanvas();
            BrawlLobbyStage.Ensure();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void LateUpdate()
        {
            AdoptRuntimeHelpers();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Instance != this) return;
            DestroyLooseDuplicates();
            BindScene();
            BindHudCanvas();
            BrawlLobbyStage.Ensure();
            BrawlMatchHud hud = GetComponentInChildren<BrawlMatchHud>(true);
            if (hud != null && !hud.gameObject.activeSelf)
                hud.gameObject.SetActive(true);
        }

        public void BuildRuntimeFallback()
        {
            if (GetComponentInChildren<BrawlNetworkManager>(true) == null)
            {
                var network = new GameObject("Network");
                network.transform.SetParent(transform, false);
                var manager = network.AddComponent<BrawlNetworkManager>();
                var kcp = network.AddComponent<kcp2k.KcpTransport>();
                kcp.Port = 7777;
                manager.transport = kcp;
                manager.networkAddress = "localhost";
                manager.autoCreatePlayer = true;
                manager.dontDestroyOnLoad = false;
                manager.offlineScene = BrawlLevelCatalog.LauncherScene;
                network.AddComponent<NetworkManagerHUD>();
                network.AddComponent<MiniGameNetworkHook>();
                BrawlServerDiscovery.Ensure(manager);
            }

            if (GetComponentInChildren<BrawlBootstrap>(true) == null)
            {
                var bootstrap = new GameObject("Bootstrap");
                bootstrap.transform.SetParent(transform, false);
                var boot = bootstrap.AddComponent<BrawlBootstrap>();
                boot.FixedTimeStep = 0.02f;
            }

            BrawlRunRecord.Ensure(transform);
            if (GetComponentInChildren<BrawlGameManager>(true) == null)
            {
                var gmObject = new GameObject("GameManager");
                gmObject.transform.SetParent(transform, false);
                gmObject.AddComponent<NetworkIdentity>();
                gmObject.AddComponent<BrawlGameManager>();
            }

            if (GetComponentInChildren<EventSystem>(true) == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            EnsureHudCanvas();
            if (GetComponentInChildren<BrawlMatchHud>(true) == null)
            {
                var hud = new GameObject("MatchHud", typeof(RectTransform));
                hud.transform.SetParent(EnsureHudCanvas(), false);
                if (hud.transform is RectTransform rect)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }
                hud.AddComponent<BrawlMatchHud>();
            }

            AdoptLooseObjects();
        }

        public static void AdoptActor(GameObject actor)
        {
            if (actor == null) return;
            AdoptTransform(actor.transform);
            AdoptRagdollRuntime(actor);
        }

        public static void AdoptAllPlayers()
        {
            foreach (NetFAnnequinController fan in FindObjectsOfType<NetFAnnequinController>())
            {
                if (fan != null)
                    AdoptActor(fan.gameObject);
            }

            foreach (NetPlayerMotor motor in FindObjectsOfType<NetPlayerMotor>())
            {
                if (motor != null)
                    AdoptActor(motor.gameObject);
            }

            foreach (RagdollAnimatorDummyReference dummy in FindObjectsOfType<RagdollAnimatorDummyReference>())
            {
                if (dummy != null)
                    AdoptTransform(dummy.transform);
            }
        }

        static void AdoptTransform(Transform target)
        {
            if (target == null) return;
            if (Instance != null)
            {
                Transform root = Instance.EnsurePersistentActors();
                if (target != root && !target.IsChildOf(root))
                    target.SetParent(root, true);
                return;
            }

            target.SetParent(null);
            DontDestroyOnLoad(target.gameObject);
        }

        static void AdoptRagdollRuntime(GameObject actor)
        {
            RagdollAnimator2 ragdoll = actor.GetComponent<RagdollAnimator2>();
            if (ragdoll == null)
                ragdoll = actor.GetComponentInChildren<RagdollAnimator2>();
            if (ragdoll == null || ragdoll.Settings == null) return;

            Transform dummy = ragdoll.Settings.Dummy_Container;
            if (dummy != null)
                AdoptTransform(dummy);

            Transform dummyParent = ragdoll.Settings.TargetParentForRagdollDummy;
            if (dummyParent != null && dummyParent.parent == null)
                AdoptTransform(dummyParent);
        }

        Transform EnsurePersistentActors()
        {
            Transform root = transform.Find("PersistentActors");
            if (root != null) return root;

            var go = new GameObject("PersistentActors");
            root = go.transform;
            root.SetParent(transform, false);
            return root;
        }

        void BindHudCanvas()
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null) return;

            if (Application.isPlaying)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 2f;
            if (canvas.transform.localScale.sqrMagnitude < 0.01f)
                canvas.transform.localScale = Vector3.one;
        }

        public void BindScene()
        {
            AdoptAllPlayers();
            BrawlGameManager gm = GetComponentInChildren<BrawlGameManager>(true);
            if (gm == null) return;

            gm.AirWall = null;
            BrawlAirWall.ClearStale();
            BrawlAirWall wall = FindObjectOfType<BrawlAirWall>(true);
            gm.AirWall = wall;
            if (wall != null)
                BrawlAirWall.Ensure(gm);
        }

        public void AdoptLooseObjects()
        {
            ParentIfLoose(FindObjectOfType<BrawlNetworkManager>());
            ParentIfLoose(FindObjectOfType<BrawlBootstrap>());
            ParentIfLoose(FindObjectOfType<BrawlGameManager>());
            ParentIfLoose(FindObjectOfType<EventSystem>());
            AdoptMatchHud();
            ConfigureNetworkManager();
        }

        void AdoptRuntimeHelpers()
        {
            ParentIfLoose(FindObjectOfType<BrawlNetworkHud>());
            ParentIfLoose(FindObjectOfType<BrawlBotLobby>());
            AdoptAllPlayers();
        }

        void AdoptMatchHud()
        {
            BrawlMatchHud hud = FindObjectOfType<BrawlMatchHud>();
            if (hud == null || hud.transform.IsChildOf(transform)) return;

            Transform canvas = EnsureHudCanvas();
            hud.transform.SetParent(canvas, false);
            if (hud.transform is RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
            }
        }

        Transform EnsureHudCanvas()
        {
            Transform existing = transform.Find("HudCanvas");
            if (existing != null) return existing;

            var canvasObject = new GameObject("HudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1366f, 768f);
            scaler.matchWidthOrHeight = 0.556f;
            return canvasObject.transform;
        }

        void ConfigureNetworkManager()
        {
            NetworkManager manager = GetComponentInChildren<NetworkManager>(true);
            if (manager == null) manager = NetworkManager.singleton;
            if (manager == null) return;
            manager.dontDestroyOnLoad = false;
            if (manager.GetComponent<MiniGameNetworkHook>() == null)
                manager.gameObject.AddComponent<MiniGameNetworkHook>();
            BrawlServerDiscovery.Ensure(manager);
        }

        void DestroyLooseDuplicates()
        {
            DestroyIfLoose(FindObjectsOfType<BrawlSession>());
            DestroyIfLoose(FindObjectsOfType<BrawlNetworkManager>());
            DestroyIfLoose(FindObjectsOfType<BrawlBootstrap>());
            DestroyIfLoose(FindObjectsOfType<BrawlGameManager>());
            DestroyIfLoose(FindObjectsOfType<BrawlMatchHud>());
            DestroyIfLoose(FindObjectsOfType<BrawlNetworkHud>());
            DestroyIfLoose(FindObjectsOfType<BrawlBotLobby>());

            EventSystem[] systems = FindObjectsOfType<EventSystem>();
            EventSystem keep = GetComponentInChildren<EventSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null || systems[i] == keep) continue;
                if (keep != null)
                    Destroy(systems[i].gameObject);
                else
                    systems[i].transform.SetParent(transform, true);
            }
        }

        void ParentIfLoose(Component component)
        {
            if (component == null || component.transform.IsChildOf(transform) || component.transform == transform)
                return;
            component.transform.SetParent(transform, true);
        }

        void DestroyIfLoose(Component[] components)
        {
            if (components == null) return;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.transform.IsChildOf(transform) || component.transform == transform)
                    continue;
                Destroy(component.gameObject);
            }
        }
    }
}
