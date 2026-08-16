using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl
{
    /// <summary>
    /// Built-in Render Pipeline 在运行时切换场景后，偶尔会继续使用旧的环境光探针。
    /// 每次场景加载完成后的下一帧刷新一次，确保天空盒环境光在 Game 视图中生效。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrawlEnvironmentLightingRefresh : MonoBehaviour
    {
        static BrawlEnvironmentLightingRefresh instance;

        Coroutine pendingRefresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (instance != null) return;

            var go = new GameObject("Brawl Environment Lighting Refresh");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BrawlEnvironmentLightingRefresh>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (pendingRefresh != null)
                StopCoroutine(pendingRefresh);

            pendingRefresh = StartCoroutine(RefreshAfterSceneSettles(scene));
        }

        IEnumerator RefreshAfterSceneSettles(Scene scene)
        {
            // 等新场景成为 Active Scene，并让 RenderSettings 完成切换。
            yield return null;

            pendingRefresh = null;
            if (!scene.IsValid() || !scene.isLoaded)
                yield break;

            DynamicGI.UpdateEnvironment();
        }
    }
}
