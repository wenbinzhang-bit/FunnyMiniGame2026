using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl
{
    /// <summary>
    /// 办公模块地板碰撞只有 1–5mm，大厅地面也只有 10cm。
    /// 只把碰撞盒往下加厚，顶面高度不变，避免角色离散穿地。
    /// </summary>
    public static class BrawlGroundColliderFix
    {
        const float MinWorldThickness = 0.25f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyInActiveScene();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            ApplyInScene(scene);
        }

        public static void ApplyInActiveScene()
        {
            ApplyInScene(SceneManager.GetActiveScene());
        }

        public static void ApplyInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                ApplyInHierarchy(roots[i].transform);
        }

        static void ApplyInHierarchy(Transform root)
        {
            BoxCollider[] boxes = root.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxCollider box = boxes[i];
                if (box == null || box.isTrigger || !IsFloorLike(box.transform))
                    continue;
                ThickenDown(box, MinWorldThickness);
            }
        }

        static bool IsFloorLike(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string name = current.name;
                if (name.StartsWith("SM_Floor") || name == "LobbyGround" || name == "TrainingGround")
                    return true;
            }

            return false;
        }

        static void ThickenDown(BoxCollider box, float minWorldThickness)
        {
            float scaleY = Mathf.Abs(box.transform.lossyScale.y);
            if (scaleY < 0.0001f) return;

            float worldThickness = Mathf.Abs(box.size.y) * scaleY;
            if (worldThickness >= minWorldThickness - 0.001f) return;

            Vector3 size = box.size;
            Vector3 center = box.center;
            float topLocal = center.y + size.y * 0.5f;
            float newSizeY = minWorldThickness / scaleY;
            size.y = newSizeY;
            center.y = topLocal - newSizeY * 0.5f;
            box.size = size;
            box.center = center;
        }
    }
}
