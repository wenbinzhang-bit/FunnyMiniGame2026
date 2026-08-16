using Brawl;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 把 Launcher 设为游戏入口，并保证 MiniGame 关卡都在 Build Settings 里。
    /// </summary>
    public static class BrawlLaunchSetup
    {
        const string LauncherPath = "Assets/Brawl/Scenes/Launcher.unity";
        const string LevelSceneFolder = "Assets/Brawl/Scenes";

        [InitializeOnLoadMethod]
        static void AutoApply()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                ApplyBuildSettings();
                StripSessionFromLevelScene();
                BrawlLevelInfoSetup.PlaceInActiveLevelIfMissing();
                PlaceLobbyStageIfMissing();
                if (SceneManager.GetActiveScene().name == BrawlLevelCatalog.LauncherScene
                    && GameObject.Find("TrainingGround") == null)
                    BrawlLobbyTrainingSetup.PlaceInLauncher();
            };
        }

        [MenuItem("Brawl/Setup Launcher Entry")]
        public static void SetupFromMenu()
        {
            ApplyBuildSettings();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BrawlSessionSetup.PrefabPath) == null)
                BrawlSessionSetup.BuildPrefab();
            Debug.Log("BRAWL_LAUNCH: Launcher 已设为入口，Build Settings 已包含大厅和关卡。");
        }

        public static void ApplyBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(LauncherPath, true)
            };
            foreach (string path in FindLevelScenePaths())
                scenes.Add(new EditorBuildSettingsScene(path, true));

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing == null || string.IsNullOrEmpty(existing.path)) continue;
                if (scenes.Exists(s => s.path == existing.path)) continue;
                scenes.Add(existing);
            }

            EditorBuildSettings.scenes = scenes.ToArray();

            SceneAsset launcher = AssetDatabase.LoadAssetAtPath<SceneAsset>(LauncherPath);
            if (launcher != null)
                EditorSceneManager.playModeStartScene = launcher;
        }

        static System.Collections.Generic.List<string> FindLevelScenePaths()
        {
            var found = new System.Collections.Generic.List<(int index, string path)>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { LevelSceneFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                int index = BrawlLevelCatalog.GetLevelIndex(System.IO.Path.GetFileNameWithoutExtension(path));
                if (index >= 0 && index < BrawlLevelCatalog.MaxLevelCount)
                    found.Add((index, path));
            }

            found.Sort((a, b) => a.index.CompareTo(b.index));
            var paths = new System.Collections.Generic.List<string>(found.Count);
            for (int i = 0; i < found.Count; i++)
                paths.Add(found[i].path);
            return paths;
        }

        static void StripSessionFromLevelScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!BrawlLevelCatalog.IsLevel(scene.name)) return;

            bool dirty = false;
            dirty |= DestroyAll<BrawlSession>();
            dirty |= DestroyAll<BrawlNetworkManager>();
            dirty |= DestroyAll<BrawlGameManager>();
            dirty |= DestroyAll<BrawlBootstrap>();
            dirty |= DestroyAll<BrawlMatchHud>();
            if (!dirty) return;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("BRAWL_LAUNCH: 已从关卡场景移除常驻网络节点，改由 Launcher / BrawlSession 提供。");
        }

        static void PlaceLobbyStageIfMissing()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != BrawlLevelCatalog.LauncherScene) return;
            if (GameObject.Find("LobbyGround") != null && Object.FindObjectOfType<NetworkStartPosition>() != null)
                return;

            Material groundMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Brawl/Materials/LobbyGround.mat");
            Transform root = null;
            BrawlLobbyStage stage = Object.FindObjectOfType<BrawlLobbyStage>();
            if (stage != null)
                root = stage.transform;
            else
            {
                var go = new GameObject("LobbyStage");
                Undo.RegisterCreatedObjectUndo(go, "Create LobbyStage");
                go.AddComponent<BrawlLobbyStage>();
                root = go.transform;
            }

            if (GameObject.Find("LobbyGround") == null)
            {
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(ground, "Create LobbyGround");
                ground.name = "LobbyGround";
                ground.transform.SetParent(root, false);
                ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(36f, 1f, 36f);
                var renderer = ground.GetComponent<Renderer>();
                if (renderer != null && groundMat != null)
                    renderer.sharedMaterial = groundMat;
            }

            if (Object.FindObjectOfType<NetworkStartPosition>() == null)
            {
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
                    spawn.transform.SetParent(root, false);
                    spawn.transform.localPosition = spots[i];
                    spawn.AddComponent<NetworkStartPosition>();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("BRAWL_LAUNCH: 已把大厅地面和出生点放进 Launcher 场景，可直接在 Hierarchy 里改。");
        }

        static bool DestroyAll<T>() where T : Component
        {
            T[] found = Object.FindObjectsOfType<T>();
            bool dirty = false;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null) continue;
                GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(found[i].gameObject);
                Object.DestroyImmediate(root != null ? root : found[i].gameObject);
                dirty = true;
            }

            return dirty;
        }
    }
}
