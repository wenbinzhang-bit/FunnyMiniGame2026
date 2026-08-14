using System;
using System.Collections;
using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 全局物理设置,替代 Demo 的 ClayBeastsExampleSettings:
    /// 所有端(主机/客户端)执行完全一致的配置,保证 Mirror 时序与物理层一致。
    /// 另支持命令行自动联机(用于自动化测试):-brawlHost / -brawlClient [地址]。
    /// </summary>
    public class BrawlBootstrap : MonoBehaviour
    {
        public float FixedTimeStep = 0.01f;
        public float GravityY = -9.81f;

        void Awake()
        {
            Time.fixedDeltaTime = FixedTimeStep;
            Physics.gravity = new Vector3(0f, GravityY, 0f);

            // 与 Clay Beasts Demo 一致的层碰撞忽略(Water vs UI / TransparentFX vs Water)
            Physics.IgnoreLayerCollision(4, 5, true);
            Physics.IgnoreLayerCollision(1, 4, true);
        }

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool host = Array.IndexOf(args, "-brawlHost") >= 0;
            int clientIdx = Array.IndexOf(args, "-brawlClient");

            int quitIdx = Array.IndexOf(args, "-brawlQuitAfter");
            if (quitIdx >= 0 && quitIdx + 1 < args.Length && float.TryParse(args[quitIdx + 1], out float quitAfter))
                StartCoroutine(QuitAfter(quitAfter));

            if (!host && clientIdx < 0) yield break;

            // 等待 NetworkManager 就绪
            while (NetworkManager.singleton == null) yield return null;
            yield return null;

            if (host)
            {
                Debug.Log("BRAWL_SMOKE: AUTO_START_HOST");
                NetworkManager.singleton.StartHost();
            }
            else
            {
                if (clientIdx + 1 < args.Length && !args[clientIdx + 1].StartsWith("-"))
                    NetworkManager.singleton.networkAddress = args[clientIdx + 1];
                Debug.Log($"BRAWL_SMOKE: AUTO_START_CLIENT -> {NetworkManager.singleton.networkAddress}");
                NetworkManager.singleton.StartClient();
            }
        }

        IEnumerator QuitAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.Log("BRAWL_SMOKE: QUIT_AFTER_TIMEOUT");
            Application.Quit();
        }
    }
}
