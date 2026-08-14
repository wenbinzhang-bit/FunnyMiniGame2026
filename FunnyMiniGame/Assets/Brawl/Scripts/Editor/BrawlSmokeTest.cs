using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 编辑器内主机冒烟测试(可在 batchmode 运行):
    /// 打开 Arena 场景 → 进入播放模式 → StartHost → 断言玩家生成、Ragdoll 初始化、姿态流发送。
    /// 通过打印 BRAWL_SMOKE: HOST_SMOKE_OK 并以退出码 0 结束;失败退出码 1。
    /// </summary>
    public static class BrawlSmokeTest
    {
        const string RunningFlag = "BRAWL_SMOKE_RUNNING";

        public static void Run()
        {
            SessionState.SetBool(RunningFlag, true);
            EditorSceneManager.OpenScene("Assets/Brawl/Scenes/Arena.unity");
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void HookAfterDomainReload()
        {
            if (!SessionState.GetBool(RunningFlag, false)) return;

            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingPlayMode)
                    SessionState.SetBool(RunningFlag, false);
            };

            // 域重载后若已处于播放模式,挂载执行器
            EditorApplication.delayCall += () =>
            {
                if (!Application.isPlaying) return;
                if (Object.FindObjectOfType<SmokeRunner>() != null) return;
                new GameObject("~SmokeRunner").AddComponent<SmokeRunner>();
            };
        }

        class SmokeRunner : MonoBehaviour
        {
            const float Timeout = 120f;
            float startTime;
            bool hostStarted;
            bool stopping;
            float stopTime;

            void Awake()
            {
                DontDestroyOnLoad(gameObject);
                startTime = Time.realtimeSinceStartup;
            }

            void Update()
            {
                float elapsed = Time.realtimeSinceStartup - startTime;

                if (stopping)
                {
                    // 已验证成功,触发 StopHost 走一遍销毁路径以暴露清理期异常,再退出
                    if (Time.realtimeSinceStartup >= stopTime) Succeed();
                    return;
                }

                if (!hostStarted)
                {
                    if (NetworkManager.singleton != null)
                    {
                        hostStarted = true;
                        Debug.Log("BRAWL_SMOKE: RUNNER_START_HOST");
                        NetworkManager.singleton.StartHost();
                    }
                    else if (elapsed > 15f)
                    {
                        Fail("NetworkManager singleton not found");
                    }
                    return;
                }

                // 成功条件:本地玩家已生成 + 服务端 Ragdoll 初始化完成 + 姿态流已在发送
                if (NetworkServer.active && NetworkClient.localPlayer != null)
                {
                    var motor = NetworkClient.localPlayer.GetComponent<NetPlayerMotor>();
                    if (motor != null && motor.ServerInitialized && RagdollNetworkSync.PoseSendCount > 20)
                    {
                        Debug.Log($"BRAWL_SMOKE: HOST_SMOKE_OK poseSends={RagdollNetworkSync.PoseSendCount}");
                        stopping = true;
                        stopTime = Time.realtimeSinceStartup + 3f;
                        NetworkManager.singleton.StopHost();
                        return;
                    }
                }

                if (elapsed > Timeout)
                    Fail($"timeout. serverActive={NetworkServer.active} localPlayer={NetworkClient.localPlayer} poseSends={RagdollNetworkSync.PoseSendCount}");
            }

            void Succeed()
            {
                SessionState.SetBool(RunningFlag, false);
                if (Application.isBatchMode) EditorApplication.Exit(0);
                else EditorApplication.ExitPlaymode();
                enabled = false;
            }

            void Fail(string reason)
            {
                Debug.LogError($"BRAWL_SMOKE: HOST_SMOKE_FAIL {reason}");
                SessionState.SetBool(RunningFlag, false);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else EditorApplication.ExitPlaymode();
                enabled = false;
            }
        }
    }
}
