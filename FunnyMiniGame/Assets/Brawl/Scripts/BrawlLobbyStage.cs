using Mirror;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// Launcher 大厅场地标记。地面和出生点应直接放在 Launcher 场景里编辑；
    /// 运行时只在场景缺这些物体时补一套，避免空场景掉下去。
    /// </summary>
    public sealed class BrawlLobbyStage : MonoBehaviour
    {
        public static void Ensure()
        {
            if (!BrawlLevelCatalog.IsLauncher(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                return;

            if (FindObjectOfType<BrawlLobbyStage>() == null)
            {
                var go = new GameObject("LobbyStage");
                go.AddComponent<BrawlLobbyStage>();
            }

            EnsureGround();
            EnsureSpawns();
            BrawlGroundColliderFix.ApplyInActiveScene();
        }

        static void EnsureGround()
        {
            if (GameObject.Find("LobbyGround") != null || GameObject.Find("TrainingGround") != null)
                return;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "LobbyGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(36f, 1f, 36f);
            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(0.23f, 0.28f, 0.32f, 1f);
        }

        static void EnsureSpawns()
        {
            if (FindObjectOfType<NetworkStartPosition>() != null) return;

            Vector3[] spots =
            {
                new Vector3(4f, 1.4f, 4f),
                new Vector3(-4f, 1.4f, -4f),
                new Vector3(-4f, 1.4f, 4f),
                new Vector3(4f, 1.4f, -4f)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var go = new GameObject("Spawn_" + (i + 1));
                go.transform.position = spots[i];
                go.AddComponent<NetworkStartPosition>();
            }
        }
    }
}
