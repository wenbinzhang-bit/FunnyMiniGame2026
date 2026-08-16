using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl
{
    /// <summary>
    /// 关卡表：Launcher 是大厅。
    /// 整场只打 2 关：第1关 MiniGame_00，第2关 MiniGame_01，结束后汇总 KPI。
    /// </summary>
    public static class BrawlLevelCatalog
    {
        public const string LauncherScene = "Launcher";
        public const string LevelNamePrefix = "MiniGame_";
        public const int MaxLevelCount = 2;
        static readonly Regex LevelName = new Regex(@"^MiniGame_(\d+)$", RegexOptions.CultureInvariant);

        public static string FormatLevelName(int index)
        {
            return $"{LevelNamePrefix}{Mathf.Max(0, index):D2}";
        }

        public static string NormalizeName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return "";
            return System.IO.Path.GetFileNameWithoutExtension(sceneName);
        }

        public static string ActiveSceneName()
        {
            return NormalizeName(SceneManager.GetActiveScene().name);
        }

        public static bool IsLauncher(string sceneName)
        {
            return NormalizeName(sceneName) == LauncherScene;
        }

        public static bool IsLevel(string sceneName)
        {
            return GetLevelIndex(sceneName) >= 0;
        }

        public static bool ActiveSceneIsLauncher()
        {
            return IsLauncher(ActiveSceneName());
        }

        public static bool ActiveSceneIsLevel()
        {
            return IsLevel(ActiveSceneName());
        }

        public static int GetLevelIndex(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return -1;
            Match match = LevelName.Match(NormalizeName(sceneName));
            if (!match.Success) return -1;
            return int.TryParse(match.Groups[1].Value, out int index) ? index : -1;
        }

        public static string GetLevelTitle(string sceneName)
        {
            int index = GetLevelIndex(sceneName);
            if (index < 0) return NormalizeName(sceneName);
            return $"第{index + 1}关";
        }

        public static string GetFirstLevel()
        {
            string first = FormatLevelName(0);
            return LevelExistsInBuild(first) ? first : "";
        }

        public static string GetNextLevel(string currentSceneName)
        {
            string current = NormalizeName(currentSceneName);
            if (IsLauncher(current))
                return GetFirstLevel();

            int index = GetLevelIndex(current);
            if (index < 0) return "";

            int nextIndex = index + 1;
            if (nextIndex >= MaxLevelCount) return "";

            string next = FormatLevelName(nextIndex);
            return LevelExistsInBuild(next) ? next : "";
        }

        public static bool HasNextLevel(string currentSceneName)
        {
            return !string.IsNullOrEmpty(GetNextLevel(currentSceneName));
        }

        public static bool LevelExistsInBuild(string sceneName)
        {
            return GetBuildIndex(sceneName) >= 0;
        }

        public static int GetBuildIndex(string sceneName)
        {
            string want = NormalizeName(sceneName);
            if (string.IsNullOrEmpty(want)) return -1;
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (NormalizeName(path) == want)
                    return i;
            }

            return -1;
        }

        public static List<string> GetLevelScenes()
        {
            var result = new List<(int index, string name)>();
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                string name = NormalizeName(path);
                int index = GetLevelIndex(name);
                if (index >= 0 && index < MaxLevelCount)
                    result.Add((index, name));
            }

            result.Sort((a, b) => a.index.CompareTo(b.index));
            var names = new List<string>(result.Count);
            for (int i = 0; i < result.Count; i++)
                names.Add(result[i].name);
            return names;
        }
    }
}
