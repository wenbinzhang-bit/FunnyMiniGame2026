using Brawl;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 给各小关放一份 BrawlLevelInfo，用玩法模式区分抢电脑和甩锅。
    /// </summary>
    public static class BrawlLevelInfoSetup
    {
        const string RootName = "LevelInfo";

        [MenuItem("Brawl/Setup Level Play Modes")]
        public static void SetupFromMenu()
        {
            PlaceInActiveLevelIfMissing();
            Debug.Log("BRAWL_LEVEL_INFO: 已按关卡写入玩法模式。");
        }

        public static void PlaceInActiveLevelIfMissing()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!BrawlLevelCatalog.IsLevel(scene.name)) return;

            if (Object.FindObjectOfType<BrawlLevelInfo>() != null) return;

            BrawlPlayMode want = BrawlLevelCatalog.DefaultPlayMode(scene.name);
            var root = new GameObject(RootName);
            BrawlLevelInfo info = root.AddComponent<BrawlLevelInfo>();
            info.PlayMode = want;
            info.ApplyDefaultsForMode();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"BRAWL_LEVEL_INFO: 已在 {scene.name} 创建 {want}");
        }
    }
}
