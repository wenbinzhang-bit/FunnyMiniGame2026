using Brawl;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 把 Launcher 大厅换成办公走廊地板 + 训练假人 + 箱子。
    /// </summary>
    public static class BrawlLobbyTrainingSetup
    {
        const string FloorPath = "Assets/LeartesStudios/OfficeCorridor/Art/Prefabs/SM_Floor01.prefab";
        const string WallPath = "Assets/LeartesStudios/OfficeCorridor/Art/Prefabs/SM_Office_wall01c.prefab";
        const string SofaPath = "Assets/LeartesStudios/OfficeCorridor/Art/Prefabs/SM_Sofa01.prefab";
        const string DummyPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Demos Assets/Prefabs/Demo Training Dummy.prefab";
        const string PunchVictimPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Demos Assets/Prefabs/PR_PuncherVictim_Mannequin.prefab";
        const string BoxPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Demos Assets/Prefabs/Demo Box.prefab";

        const float TileSize = 6f;
        const int TileCount = 6;

        [InitializeOnLoadMethod]
        static void AutoApply()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (SceneManager.GetActiveScene().name != BrawlLevelCatalog.LauncherScene) return;
                if (GameObject.Find("TrainingGround") != null) return;
                PlaceInLauncher();
            };
        }

        [MenuItem("Brawl/Setup Lobby Training Ground")]
        public static void SetupFromMenu()
        {
            PlaceInLauncher();
        }

        public static void PlaceInLauncher()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != BrawlLevelCatalog.LauncherScene)
            {
                Debug.LogWarning("BRAWL_LAUNCH: 请先打开 Launcher 场景再布置训练场。");
                return;
            }

            GameObject floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FloorPath);
            GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
            GameObject sofaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SofaPath);
            GameObject dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPath);
            GameObject boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);
            if (floorPrefab == null || dummyPrefab == null || boxPrefab == null)
            {
                Debug.LogError("BRAWL_LAUNCH: 训练场预制体缺失，无法布置大厅。");
                return;
            }

            Transform stage = EnsureStage();
            HideOldGroundCube();
            EnsureInvisibleFloor(stage);
            EnsureSpawns(stage);

            Transform training = stage.Find("TrainingGround");
            if (training != null)
                Undo.DestroyObjectImmediate(training.gameObject);

            var root = new GameObject("TrainingGround");
            Undo.RegisterCreatedObjectUndo(root, "Create TrainingGround");
            root.transform.SetParent(stage, false);

            PlaceFloors(root.transform, floorPrefab);
            if (wallPrefab != null)
                PlaceWalls(root.transform, wallPrefab);
            if (sofaPrefab != null)
                PlaceSofas(root.transform, sofaPrefab);
            PlaceDummies(root.transform, dummyPrefab);
            GameObject victimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PunchVictimPath);
            if (victimPrefab != null)
                PlacePunchVictims(root.transform, victimPrefab);
            PlaceBoxes(root.transform, boxPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("BRAWL_LAUNCH: 大厅已换成办公走廊训练场，可直接在 Hierarchy 的 TrainingGround 下改。");
        }

        static Transform EnsureStage()
        {
            BrawlLobbyStage stage = Object.FindObjectOfType<BrawlLobbyStage>();
            if (stage != null)
                return stage.transform;

            var go = new GameObject("LobbyStage");
            Undo.RegisterCreatedObjectUndo(go, "Create LobbyStage");
            go.AddComponent<BrawlLobbyStage>();
            return go.transform;
        }

        static void HideOldGroundCube()
        {
            GameObject ground = GameObject.Find("LobbyGround");
            if (ground == null) return;

            MeshRenderer renderer = ground.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;
            MeshFilter filter = ground.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
        }

        static void EnsureInvisibleFloor(Transform stage)
        {
            GameObject ground = GameObject.Find("LobbyGround");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(ground, "Create LobbyGround");
                ground.name = "LobbyGround";
                ground.transform.SetParent(stage, false);
            }

            ground.transform.SetParent(stage, true);
            ground.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(36f, 0.5f, 36f);
            MeshRenderer renderer = ground.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;
        }

        static void EnsureSpawns(Transform stage)
        {
            if (Object.FindObjectOfType<NetworkStartPosition>() != null)
                return;

            Vector3[] spots =
            {
                new Vector3(4f, 1.4f, 4f),
                new Vector3(-4f, 1.4f, -4f),
                new Vector3(-4f, 1.4f, 4f),
                new Vector3(4f, 1.4f, -4f)
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var spawn = new GameObject("Spawn_" + (i + 1));
                Undo.RegisterCreatedObjectUndo(spawn, "Create Lobby Spawn");
                spawn.transform.SetParent(stage, false);
                spawn.transform.localPosition = spots[i];
                spawn.AddComponent<NetworkStartPosition>();
            }
        }

        static void PlaceFloors(Transform parent, GameObject prefab)
        {
            float origin = -TileSize * TileCount * 0.5f;
            int index = 0;
            for (int x = 0; x < TileCount; x++)
            {
                for (int z = 0; z < TileCount; z++)
                {
                    Transform tile = InstantiateUnder(prefab, parent, "SM_Floor01_" + index);
                    tile.localPosition = new Vector3(origin + x * TileSize, 0f, origin + z * TileSize);
                    tile.localScale = Vector3.one * 2f;
                    index++;
                }
            }
        }

        static void PlaceWalls(Transform parent, GameObject prefab)
        {
            float half = TileSize * TileCount * 0.5f;
            float[] along = { -12f, 0f, 12f };
            for (int i = 0; i < along.Length; i++)
            {
                Transform north = InstantiateUnder(prefab, parent, "Wall_N_" + i);
                north.localPosition = new Vector3(along[i] - 3f, 0f, half);
                north.localRotation = Quaternion.Euler(0f, 90f, 0f);
                north.localScale = Vector3.one * 2f;

                Transform south = InstantiateUnder(prefab, parent, "Wall_S_" + i);
                south.localPosition = new Vector3(along[i] + 3f, 0f, -half);
                south.localRotation = Quaternion.Euler(0f, -90f, 0f);
                south.localScale = Vector3.one * 2f;

                Transform east = InstantiateUnder(prefab, parent, "Wall_E_" + i);
                east.localPosition = new Vector3(half, 0f, along[i] + 3f);
                east.localRotation = Quaternion.Euler(0f, 180f, 0f);
                east.localScale = Vector3.one * 2f;

                Transform west = InstantiateUnder(prefab, parent, "Wall_W_" + i);
                west.localPosition = new Vector3(-half, 0f, along[i] - 3f);
                west.localRotation = Quaternion.identity;
                west.localScale = Vector3.one * 2f;
            }
        }

        static void PlaceSofas(Transform parent, GameObject prefab)
        {
            Transform a = InstantiateUnder(prefab, parent, "Sofa_A");
            a.localPosition = new Vector3(-14f, 0f, 12f);
            a.localRotation = Quaternion.Euler(0f, 90f, 0f);

            Transform b = InstantiateUnder(prefab, parent, "Sofa_B");
            b.localPosition = new Vector3(14f, 0f, -12f);
            b.localRotation = Quaternion.Euler(0f, -90f, 0f);
        }

        static void PlaceDummies(Transform parent, GameObject prefab)
        {
            Vector3[] spots =
            {
                new Vector3(0f, 0f, 9f),
                new Vector3(0f, 0f, -9f),
                new Vector3(9f, 0f, 0f),
                new Vector3(-9f, 0f, 0f)
            };
            for (int i = 0; i < spots.Length; i++)
            {
                Transform dummy = InstantiateUnder(prefab, parent, "TrainingDummy_" + (i + 1));
                dummy.localPosition = spots[i];
                dummy.localRotation = Quaternion.LookRotation(-spots[i].normalized, Vector3.up);
                dummy.gameObject.SetActive(true);
            }
        }

        static void PlacePunchVictims(Transform parent, GameObject prefab)
        {
            Transform group = parent.Find("Characters");
            if (group == null)
            {
                var go = new GameObject("Characters");
                Undo.RegisterCreatedObjectUndo(go, "Create Lobby Characters");
                group = go.transform;
                group.SetParent(parent, false);
            }

            Vector3[] spots =
            {
                new Vector3(0f, 0f, 6f),
                new Vector3(2.8f, 0f, 6f),
                new Vector3(-2.8f, 0f, 6f)
            };
            for (int i = 0; i < spots.Length; i++)
            {
                string name = i == 0 ? "PR_PuncherVictim_Mannequin" : "PR_PuncherVictim_Mannequin (" + i + ")";
                if (group.Find(name) != null) continue;
                Transform victim = InstantiateUnder(prefab, group, name);
                victim.localPosition = spots[i];
                victim.localRotation = Quaternion.Euler(0f, 180f, 0f);
                victim.gameObject.SetActive(true);
            }
        }

        static void PlaceBoxes(Transform parent, GameObject prefab)
        {
            Vector3[] spots =
            {
                new Vector3(12f, 0.2f, 12f),
                new Vector3(-12f, 0.2f, 12f),
                new Vector3(12f, 0.2f, -12f),
                new Vector3(-12f, 0.2f, -6f),
                new Vector3(11f, 0.2f, 3f)
            };
            for (int i = 0; i < spots.Length; i++)
            {
                Transform box = InstantiateUnder(prefab, parent, "DemoBox_" + (i + 1));
                box.localPosition = spots[i];
                box.localRotation = Quaternion.Euler(0f, 20f * i, 0f);
            }
        }

        static Transform InstantiateUnder(GameObject prefab, Transform parent, string name)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Place " + name);
            instance.name = name;
            return instance.transform;
        }
    }
}
