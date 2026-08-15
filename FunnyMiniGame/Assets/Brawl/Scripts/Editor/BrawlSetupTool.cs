using System.Collections.Generic;
using Brawl;
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
        const string FannequinPrefabPath = "Assets/Brawl/Resources/NetFAnnequin.prefab";
        const string CapsulePrefabPath = PrefabDir + "/SmokeCapsule.prefab";
        const string NetworkPrefabPath = PrefabDir + "/Network.prefab";
        const string ArenaScenePath = SceneDir + "/Arena.unity";
        const string MiniGame01ScenePath = SceneDir + "/MiniGame_01.unity";
        const string PunchMinigameScenePath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Ragdoll Animator 2 Demo - Punch Minigame.unity";
        const string FannequinFbxPath = "Assets/FImpossible Creations/Plugins - Shared/FBasic Assets/Models/Fannequin/FAnnequinV2.fbx";
        const string HeroAnimatorPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Demos Assets/Additional Resources/AC_RagdollAnimator_Hero Puncher.controller";
        const string HeroPhysMatPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Core/Ragdoll Resources/PM_NoFriction.physicMaterial";

        [InitializeOnLoadMethod]
        static void AutoBuildFAnnequinPrefabWhenMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(FannequinPrefabPath) != null) return;
                try
                {
                    EnsureFolders();
                    BuildNetFAnnequinPrefab();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("BRAWL_SETUP: auto FAnnequin prefab skipped: " + e.Message);
                }
            };
        }

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
            if (!AssetDatabase.IsValidFolder("Assets/Brawl/Resources")) AssetDatabase.CreateFolder("Assets/Brawl", "Resources");
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
            if (inst.GetComponent<PlayerAttributes>() == null)
                inst.AddComponent<PlayerAttributes>();

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
            manager.playerModels = AssetDatabase.LoadAssetAtPath<PlayerModels>("Assets/Brawl/Configs/PlayerModels.asset");
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
            EnsureKillerZone();

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
            gm.AddComponent<PlayerHealthHud>();

            // 全局物理设置
            new GameObject("Bootstrap").AddComponent<BrawlBootstrap>();

            // 网络管理器实例
            PrefabUtility.InstantiatePrefab(networkPrefab);

            EditorSceneManager.SaveScene(scene, ArenaScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ArenaScenePath, true) };
            Debug.Log($"BRAWL_SETUP: saved {ArenaScenePath} and updated Build Settings");
        }

        [MenuItem("Brawl/Clone Punch Minigame to MiniGame_01")]
        public static void ClonePunchMinigameToMiniGame01()
        {
            EnsureFolders();

            Scene punch = EditorSceneManager.OpenScene(PunchMinigameScenePath, OpenSceneMode.Single);
            if (!punch.IsValid())
                throw new System.Exception($"BRAWL_SETUP: Punch Minigame scene not found at {PunchMinigameScenePath}");

            if (!EditorSceneManager.SaveScene(punch, MiniGame01ScenePath, true))
                throw new System.Exception($"BRAWL_SETUP: failed to save clone to {MiniGame01ScenePath}");

            Scene scene = EditorSceneManager.OpenScene(MiniGame01ScenePath, OpenSceneMode.Single);

            DisableLocalPunchHero(scene);
            PrepareMainCameraForNetwork(scene);
            EnsureNetworkObjects(scene);

            AssignFAnnequinPlayerPrefab(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EnsureSceneInBuildSettings(MiniGame01ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"BRAWL_SETUP: cloned Punch Minigame into {MiniGame01ScenePath} with multiplayer objects");
        }

        [MenuItem("Brawl/Setup MiniGame_01 FAnnequin Player")]
        public static void SetupMiniGame01FAnnequinPlayer()
        {
            EnsureFolders();
            BuildNetFAnnequinPrefab();

            if (!System.IO.File.Exists(MiniGame01ScenePath))
            {
                Debug.LogWarning($"BRAWL_SETUP: {MiniGame01ScenePath} not found, skip scene assign");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(MiniGame01ScenePath, OpenSceneMode.Single);
            DisableLocalPunchHero(scene);
            PrepareMainCameraForNetwork(scene);
            EnsureNetworkObjects(scene);
            AssignFAnnequinPlayerPrefab(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("BRAWL_SETUP: MiniGame_01 now spawns NetFAnnequin instead of NetClayman");
        }

        [MenuItem("Brawl/Apply FAnnequin RA2 To Player Prefabs")]
        public static void ApplyFAnnequinRa2ToPlayerPrefabs()
        {
            Scene scene = default;
            bool opened = false;
            if (EditorSceneManager.GetActiveScene().path != MiniGame01ScenePath)
            {
                scene = EditorSceneManager.OpenScene(MiniGame01ScenePath, OpenSceneMode.Additive);
                opened = true;
            }
            else
            {
                scene = EditorSceneManager.GetActiveScene();
            }

            RagdollAnimator2 source = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && source == null; i++)
            {
                RagdollAnimator2[] found = roots[i].GetComponentsInChildren<RagdollAnimator2>(true);
                for (int j = 0; j < found.Length; j++)
                {
                    if (found[j] != null && found[j].name.Contains("FAnnequin"))
                    {
                        source = found[j];
                        break;
                    }
                }
            }

            if (source == null)
            {
                Debug.LogError("BRAWL_SETUP: MiniGame_01 里没有带 RagdollAnimator2 的 FAnnequin");
                if (opened) EditorSceneManager.CloseScene(scene, true);
                return;
            }

            string[] paths =
            {
                FannequinPrefabPath,
                PrefabDir + "/NetFAnnequin.prefab",
                PrefabDir + "/NetFAnnequin_0.prefab",
                PrefabDir + "/NetFAnnequin_1.prefab",
                PrefabDir + "/NetFAnnequin_2.prefab",
                PrefabDir + "/NetFAnnequin_3.prefab"
            };

            int applied = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                if (CopyRa2OntoPrefab(source, paths[i]))
                    applied++;
            }

            if (opened) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"BRAWL_SETUP: 已把 FAnnequin 布娃娃拷到 {applied} 个玩家预制体");
        }

        static bool CopyRa2OntoPrefab(RagdollAnimator2 source, string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return false;

            string tempPath = "Assets/Brawl/Prefabs/_Ra2CopyTemp.prefab";
            GameObject instance = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                RagdollAnimator2 dest = instance.GetComponent<RagdollAnimator2>();
                if (dest == null) dest = instance.AddComponent<RagdollAnimator2>();
                EditorUtility.CopySerialized(source, dest);
                RemapSerializedTransforms(source, dest, source.transform, instance.transform);

                var hero = instance.GetComponent<Demo_Ragd_Hero1>();
                if (hero != null) hero.Ragdoll = dest;

                if (instance.GetComponent<RagdollNetworkSync>() == null)
                    instance.AddComponent<RagdollNetworkSync>();

                var net = instance.GetComponent<NetFAnnequinController>();
                if (net != null)
                {
                    if (net.Hero == null) net.Hero = hero;
                    if (net.Mecanim == null) net.Mecanim = instance.GetComponent<Animator>();
                    if (net.Mover == null) net.Mover = instance.GetComponent<FBasic_RigidbodyMover>();
                }

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                Debug.Log("BRAWL_SETUP: RA2 -> " + prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(tempPath) != null)
                    AssetDatabase.DeleteAsset(tempPath);
            }
        }

        static void RemapSerializedTransforms(Component sourceComp, Component destComp, Transform sourceRoot, Transform destRoot)
        {
            var destByName = new Dictionary<string, Transform>();
            Transform[] destBones = destRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < destBones.Length; i++)
            {
                if (!destByName.ContainsKey(destBones[i].name))
                    destByName.Add(destBones[i].name, destBones[i]);
            }

            Animator destAnim = destRoot.GetComponent<Animator>();
            SerializedObject so = new SerializedObject(destComp);
            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;
            while (it.Next(enterChildren))
            {
                enterChildren = true;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;

                Object value = it.objectReferenceValue;
                if (value == null) continue;

                if (value is Transform srcT)
                {
                    Transform mapped = MapBone(srcT, sourceRoot, destRoot, destByName);
                    if (mapped != null) it.objectReferenceValue = mapped;
                }
                else if (value is Animator && destAnim != null)
                {
                    it.objectReferenceValue = destAnim;
                }
                else if (value is GameObject go && go.transform == sourceRoot)
                {
                    it.objectReferenceValue = destRoot.gameObject;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform MapBone(Transform sourceBone, Transform sourceRoot, Transform destRoot, Dictionary<string, Transform> destByName)
        {
            if (sourceBone == null) return null;
            if (sourceBone == sourceRoot) return destRoot;
            if (!sourceBone.IsChildOf(sourceRoot) && sourceBone != sourceRoot) return sourceBone;
            Transform mapped;
            if (destByName.TryGetValue(sourceBone.name, out mapped))
                return mapped;
            return destRoot;
        }

        static void DisableLocalPunchHero(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "FAnnequinV2") continue;

                var hero = root.GetComponent<Demo_Ragd_Hero1>();
                var mover = root.GetComponent<FBasic_RigidbodyMover>();
                if (hero == null && mover == null) continue;

                // 本地 Demo 英雄会抢 WASD,联机后改由 NetClayman 控制
                root.SetActive(false);
                Debug.Log("BRAWL_SETUP: disabled local Punch hero FAnnequinV2");
                return;
            }
        }

        static void PrepareMainCameraForNetwork(Scene scene)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.CompareTag("MainCamera"))
                    {
                        cam = root.GetComponent<Camera>();
                        break;
                    }
                }
            }

            if (cam == null) return;

            MonoBehaviour[] behaviours = cam.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null) continue;
                if (behaviour is FBasic_TPPCameraBehaviour) continue;
                // 关掉 Demo 跟随相机,避免和 LocalCameraRig 抢主相机
                if (behaviour.GetType().Name.Contains("Camera") || behaviour.GetType().Name.Contains("Follow"))
                    behaviour.enabled = false;
            }

            var tpp = cam.GetComponent<FBasic_TPPCameraBehaviour>();
            if (tpp == null) tpp = cam.gameObject.AddComponent<FBasic_TPPCameraBehaviour>();
            tpp.SightLayerMask = 1;
            tpp.enabled = false;
        }

        static void EnsureNetworkObjects(Scene scene)
        {
            GameObject networkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPrefabPath);
            if (networkPrefab == null)
                throw new System.Exception($"BRAWL_SETUP: Network prefab missing at {NetworkPrefabPath}");

            if (Object.FindObjectOfType<BrawlNetworkManager>() == null)
                PrefabUtility.InstantiatePrefab(networkPrefab, scene);

            if (Object.FindObjectOfType<BrawlBootstrap>() == null)
            {
                GameObject bootstrap = new GameObject("Bootstrap");
                SceneManager.MoveGameObjectToScene(bootstrap, scene);
                var boot = bootstrap.AddComponent<BrawlBootstrap>();
                boot.FixedTimeStep = 0.02f;
            }

            if (Object.FindObjectOfType<BrawlGameManager>() == null)
            {
                GameObject gm = new GameObject("GameManager");
                SceneManager.MoveGameObjectToScene(gm, scene);
                gm.AddComponent<NetworkIdentity>();
                var brawlGm = gm.AddComponent<BrawlGameManager>();
                brawlGm.SpectatorIsland = new Vector3(60f, 3f, 60f);
                if (gm.GetComponent<PlayerHealthHud>() == null)
                    gm.AddComponent<PlayerHealthHud>();
            }

            if (Object.FindObjectOfType<NetworkStartPosition>() == null)
            {
                CreateSpawnPoint("Spawn_1", new Vector3(3f, 1.6f, 3f));
                CreateSpawnPoint("Spawn_2", new Vector3(-3f, 1.6f, -3f));
                CreateSpawnPoint("Spawn_3", new Vector3(-3f, 1.6f, 3f));
                CreateSpawnPoint("Spawn_4", new Vector3(3f, 1.6f, -3f));
            }

            if (GameObject.Find("SpectatorIsland") == null)
                CreateBlock("SpectatorIsland", new Vector3(60f, -0.5f, 60f), new Vector3(12f, 1f, 12f), isStatic: true);

            EnsureKillerZone();
        }

        static GameObject BuildNetFAnnequinPrefab()
        {
            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(FannequinFbxPath);
            if (src == null) throw new System.Exception($"BRAWL_SETUP: FAnnequin FBX not found at {FannequinFbxPath}");

            GameObject inst = Object.Instantiate(src);
            inst.name = "NetFAnnequin";
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;

            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HeroAnimatorPath);
            anim.applyRootMotion = false;

            var col = inst.GetComponent<CapsuleCollider>();
            if (col == null) col = inst.AddComponent<CapsuleCollider>();
            col.radius = 0.25f;
            col.height = 1.8f;
            col.center = new Vector3(0f, 0.9f, 0f);
            col.direction = 1;
            var physMat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(HeroPhysMatPath);
            if (physMat != null) col.sharedMaterial = physMat;

            var rb = inst.GetComponent<Rigidbody>();
            if (rb == null) rb = inst.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var mover = inst.GetComponent<FBasic_RigidbodyMover>();
            if (mover == null) mover = inst.AddComponent<FBasic_RigidbodyMover>();
            mover.Rigb = rb;
            mover.Mecanim = anim;
            mover.MovementSpeed = 4.5f;
            mover.RotateToSpeed = 0.8f;
            mover.FixedRotation = true;
            mover.DirectMovement = 1f;
            mover.Interia = 1f;
            mover.GroundMask = 3;
            mover.ExtraRaycastDistance = 0.01f;
            mover.UpdateInput = false;
            mover.JumpPower = 5f;
            mover.HoldShiftForSpeed = 6.5f;
            mover.HoldCtrlForSpeed = 2f;
            mover.DisableRootMotion = true;

            var hero = inst.GetComponent<Demo_Ragd_Hero1>();
            if (hero == null) hero = inst.AddComponent<Demo_Ragd_Hero1>();
            hero.Mover = mover;
            hero.Mecanim = anim;
            hero.ProcessInput = false;
            hero.PunchPower = 20f;
            hero.UppercutPower = 15f;
            hero.PunchKey = KeyCode.Z;
            hero.PunchUppercutKey = KeyCode.X;
            hero.CatchKey = KeyCode.C;
            hero.HittableLayermask = (LayerMask)35;
            if (anim.isHuman)
            {
                hero.Hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                hero.UpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }

            var audio = inst.GetComponent<AudioSource>();
            if (audio == null) audio = inst.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.volume = 0.4f;
            audio.pitch = 0.6f;
            hero.HitAudio = audio;

            if (inst.GetComponent<NetworkIdentity>() == null)
                inst.AddComponent<NetworkIdentity>();

            var ntr = inst.GetComponent<NetworkTransformReliable>();
            if (ntr == null) ntr = inst.AddComponent<NetworkTransformReliable>();
            ntr.target = inst.transform;
            ntr.syncDirection = SyncDirection.ServerToClient;

            if (inst.GetComponent<NetworkAnimator>() == null)
            {
                var netAnim = inst.AddComponent<NetworkAnimator>();
                netAnim.animator = anim;
            }

            var net = inst.GetComponent<NetFAnnequinController>();
            if (net == null) net = inst.AddComponent<NetFAnnequinController>();
            net.Mover = mover;
            net.Hero = hero;
            net.Mecanim = anim;

            if (inst.GetComponent<PlayerAttributes>() == null)
                inst.AddComponent<PlayerAttributes>();
            if (inst.GetComponent<FAnnequinMouseActions>() == null)
                inst.AddComponent<FAnnequinMouseActions>();
            if (inst.GetComponent<FAnnequinLocomotionFix>() == null)
                inst.AddComponent<FAnnequinLocomotionFix>();
            if (inst.GetComponent<FAnnequinGrabHelper>() == null)
                inst.AddComponent<FAnnequinGrabHelper>();

            if (inst.GetComponent<LocalCameraRig>() == null)
                inst.AddComponent<LocalCameraRig>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, FannequinPrefabPath);
            Object.DestroyImmediate(inst);
            Debug.Log($"BRAWL_SETUP: saved {FannequinPrefabPath}");
            return saved;
        }

        static void AssignFAnnequinPlayerPrefab(Scene scene)
        {
            GameObject fannequin = AssetDatabase.LoadAssetAtPath<GameObject>(FannequinPrefabPath);
            if (fannequin == null) fannequin = BuildNetFAnnequinPrefab();

            var manager = Object.FindObjectOfType<NetworkManager>();
            if (manager == null)
            {
                Debug.LogWarning("BRAWL_SETUP: NetworkManager not found in MiniGame_01");
                return;
            }

            manager.playerPrefab = fannequin;
            if (manager.GetComponent<MiniGameNetworkHook>() == null)
                manager.gameObject.AddComponent<MiniGameNetworkHook>();
            EditorUtility.SetDirty(manager);
            PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
            Debug.Log("BRAWL_SETUP: MiniGame_01 playerPrefab -> NetFAnnequin");
        }

        static void EnsureSceneInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath))
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    if (scenes[i].path == scenePath)
                        scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                }
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
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

        static void EnsureKillerZone()
        {
            if (GameObject.Find("KillerZone") != null) return;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "KillerZone";
            go.transform.position = new Vector3(0f, 1.5f, 10f);
            go.transform.localScale = new Vector3(6f, 3f, 5f);
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            if (go.GetComponent<KillerZone>() == null)
                go.AddComponent<KillerZone>();
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
