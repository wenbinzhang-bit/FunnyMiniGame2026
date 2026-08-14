using System.Collections.Generic;
using FIMSpace.Basics;
using FIMSpace.FProceduralAnimation;
using FIMSpace.RagdollAnimatorDemo;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 一键组装联机资产:
    /// 1. 由 R_ClaymanV1 生成联机玩家 prefab(去掉本地 Demo 控制器,接上网络组件)
    /// 2. 生成冒烟测试胶囊 prefab
    /// 3. 生成 Network prefab(BrawlNetworkManager + KCP + HUD)
    /// 4. 生成 Arena 场景并加入 Build Settings
    /// </summary>
    public static class BrawlSetupTool
    {
        const string ClaymanPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Clay Beasts Example/Prefabs/R_ClaymanV1.prefab";
        const string PrefabDir = "Assets/Brawl/Prefabs";
        const string SceneDir = "Assets/Brawl/Scenes";
        const string PlayerPrefabPath = PrefabDir + "/NetClayman.prefab";
        const string CapsulePrefabPath = PrefabDir + "/SmokeCapsule.prefab";
        const string NetworkPrefabPath = PrefabDir + "/Network.prefab";
        const string ArenaScenePath = SceneDir + "/Arena.unity";

        [MenuItem("Brawl/Setup All (一键组装)")]
        public static void SetupAll()
        {
            EnsureFolders();

            GameObject playerPrefab = BuildNetClaymanPrefab();
            GameObject capsulePrefab = BuildCapsulePrefab();
            GameObject networkPrefab = BuildNetworkPrefab(playerPrefab, capsulePrefab);
            BuildArenaScene(networkPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("BRAWL_SETUP: SetupAll completed OK");
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Brawl")) AssetDatabase.CreateFolder("Assets", "Brawl");
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/Brawl", "Prefabs");
            if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets/Brawl", "Scenes");
        }

        static GameObject BuildNetClaymanPrefab()
        {
            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(ClaymanPath);
            if (src == null) throw new System.Exception($"BRAWL_SETUP: source prefab not found at {ClaymanPath}");

            // 完全克隆(与源 prefab 断开链接),避免改动 Demo 资产
            GameObject inst = Object.Instantiate(src);
            inst.name = "NetClayman";
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;

            var clay = inst.GetComponent<ClaymanController>();
            if (clay == null) throw new System.Exception("BRAWL_SETUP: ClaymanController missing on source prefab");

            var ragdoll = inst.GetComponent<RagdollAnimator2>();
            Animator mecanim = clay.Mecanim;

            var identity = inst.AddComponent<NetworkIdentity>();

            var motor = inst.AddComponent<NetPlayerMotor>();
            motor.RagdollAnimator = ragdoll;
            motor.Mecanim = mecanim;
            motor.TargetMovementSpeed = clay.TargetMovementSpeed;
            motor.JumpPower = clay.JumpPower;
            motor.AnchorRotationPower = clay.AnchorRotationPower;
            motor.PowerOnDeflection = clay.PowerOnDeflection;
            motor.AnchorRotationPowerOnFall = clay.AnchorRotationPowerOnFall;
            motor.GroundMask = clay.GroundMask;
            motor.ExtraRaycastDistance = clay.ExtraRaycastDistance;
            motor.SpherecastRadius = clay.SpherecastRadius;

            var grab = inst.AddComponent<NetPlayerGrab>();
            grab.RagdollAnimator = ragdoll;
            grab.Mecanim = mecanim;
            grab.TouchRadius = clay.TouchRadius;
            grab.TouchOffset = clay.TouchOffset;

            inst.AddComponent<RagdollNetworkSync>();
            inst.AddComponent<NetPlayerInput>();
            inst.AddComponent<LocalCameraRig>();

            Object.DestroyImmediate(clay);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, PlayerPrefabPath);
            Object.DestroyImmediate(inst);
            Debug.Log($"BRAWL_SETUP: saved {PlayerPrefabPath}");
            return saved;
        }

        static GameObject BuildCapsulePrefab()
        {
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "SmokeCapsule";

            capsule.AddComponent<NetworkIdentity>();

            var ntr = capsule.AddComponent<NetworkTransformReliable>();
            ntr.target = capsule.transform;
            ntr.syncDirection = SyncDirection.ClientToServer;

            capsule.AddComponent<SmokeCapsuleMove>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(capsule, CapsulePrefabPath);
            Object.DestroyImmediate(capsule);
            Debug.Log($"BRAWL_SETUP: saved {CapsulePrefabPath}");
            return saved;
        }

        static GameObject BuildNetworkPrefab(GameObject playerPrefab, GameObject capsulePrefab)
        {
            GameObject go = new GameObject("Network");

            var manager = go.AddComponent<BrawlNetworkManager>();
            var kcp = go.AddComponent<kcp2k.KcpTransport>();
            kcp.Port = 7777;

            manager.transport = kcp;
            manager.playerPrefab = playerPrefab;
            manager.spawnPrefabs = new List<GameObject> { capsulePrefab };
            manager.autoCreatePlayer = true;
            manager.sendRate = 60;
            manager.networkAddress = "localhost";
            manager.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;

            go.AddComponent<NetworkManagerHUD>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, NetworkPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"BRAWL_SETUP: saved {NetworkPrefabPath}");
            return saved;
        }

        static void BuildArenaScene(GameObject networkPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 主相机:挂第三人称相机脚本但禁用,由 LocalCameraRig 在本地玩家生成后启用
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 8f, -14f);
                cam.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
                var tpp = cam.gameObject.AddComponent<FBasic_TPPCameraBehaviour>();
                tpp.SightLayerMask = 1; // Default 层,避免相机被角色骨骼遮挡
                tpp.enabled = false;
            }

            // 主竞技场
            CreateBlock("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), isStatic: true);
            CreateBlock("Platform_Center", new Vector3(0f, 0.5f, 0f), new Vector3(6f, 1f, 6f), isStatic: true);
            CreateBlock("Ramp_N", new Vector3(0f, 0.15f, 8f), new Vector3(4f, 0.3f, 8f), isStatic: true);
            CreateBlock("Ramp_S", new Vector3(0f, 0.15f, -8f), new Vector3(4f, 0.3f, 8f), isStatic: true);

            // 观战岛(与 BrawlGameManager.SpectatorIsland 对应)
            CreateBlock("SpectatorIsland", new Vector3(60f, -0.5f, 60f), new Vector3(12f, 1f, 12f), isStatic: true);

            // 可抓取的物理箱子(服务端权威同步)
            CreateCrate("Crate_A", new Vector3(4f, 1.5f, 4f));
            CreateCrate("Crate_B", new Vector3(-4f, 1.5f, 4f));
            CreateCrate("Crate_C", new Vector3(4f, 1.5f, -4f));
            CreateCrate("Crate_D", new Vector3(-4f, 1.5f, -4f));

            // 出生点
            CreateSpawnPoint("Spawn_1", new Vector3(6f, 1.5f, 6f));
            CreateSpawnPoint("Spawn_2", new Vector3(-6f, 1.5f, -6f));
            CreateSpawnPoint("Spawn_3", new Vector3(-6f, 1.5f, 6f));
            CreateSpawnPoint("Spawn_4", new Vector3(6f, 1.5f, -6f));

            // 对局管理器(场景网络对象)
            GameObject gm = new GameObject("GameManager");
            gm.AddComponent<NetworkIdentity>();
            var brawlGm = gm.AddComponent<BrawlGameManager>();
            brawlGm.SpectatorIsland = new Vector3(60f, 3f, 60f);

            // 全局物理设置
            new GameObject("Bootstrap").AddComponent<BrawlBootstrap>();

            // 网络管理器实例
            PrefabUtility.InstantiatePrefab(networkPrefab);

            EditorSceneManager.SaveScene(scene, ArenaScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ArenaScenePath, true) };
            Debug.Log($"BRAWL_SETUP: saved {ArenaScenePath} and updated Build Settings");
        }

        /// <summary>批处理构建 Windows 播放器,用于双实例联机自动化测试。</summary>
        public static void BuildWindowsPlayer()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ArenaScenePath },
                locationPathName = "Build/Brawl.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"BRAWL_SETUP: build result {report.summary.result} errors={report.summary.totalErrors}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        static GameObject CreateBlock(string name, Vector3 pos, Vector3 scale, bool isStatic)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.isStatic = isStatic;
            return go;
        }

        static void CreateCrate(string name, Vector3 pos)
        {
            GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = name;
            crate.transform.position = pos;
            crate.transform.localScale = Vector3.one * 0.8f;

            var rb = crate.AddComponent<Rigidbody>();
            rb.mass = 2f;

            crate.AddComponent<ClayCatchable>().Moving = true;
            crate.AddComponent<NetworkIdentity>();

            var sync = crate.AddComponent<NetworkRigidbodyReliable>();
            sync.target = crate.transform;
            // 默认 ServerToClient:服务端权威,客户端自动 kinematic
        }

        static void CreateSpawnPoint(string name, Vector3 pos)
        {
            GameObject sp = new GameObject(name);
            sp.transform.position = pos;
            sp.AddComponent<NetworkStartPosition>();
        }
    }
}
