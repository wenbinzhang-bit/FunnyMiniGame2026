using Brawl;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 在 MiniGame_01 里放置可编辑的空气墙立方体。
    /// </summary>
    public static class BrawlAirWallSetup
    {
        const string RootName = "AirWall";
        const string MaterialPath = "Assets/Brawl/Materials/AirWall.mat";

        [MenuItem("Brawl/Place Air Wall In Scene")]
        public static void PlaceFromMenu()
        {
            BrawlAirWall wall = PlaceInActiveScene();
            Selection.activeGameObject = wall.gameObject;
            EditorSceneManager.MarkSceneDirty(wall.gameObject.scene);
            EditorSceneManager.SaveScene(wall.gameObject.scene);
            Debug.Log("BRAWL_SETUP: 已在场景放置 AirWall，可直接拖父物体或四堵墙调整");
        }

        public static BrawlAirWall PlaceInActiveScene()
        {
            BrawlAirWall existing = Object.FindObjectOfType<BrawlAirWall>(true);
            if (existing != null)
            {
                BindToGameManager(existing);
                existing.ApplyLayout();
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Place AirWall");
            root.transform.position = new Vector3(6.3f, 0f, -2f);

            var wall = root.AddComponent<BrawlAirWall>();
            wall.InnerSize = new Vector3(12f, 8f, 16f);
            wall.Thickness = 0.6f;
            wall.LockWallsToSize = true;
            wall.WallNorth = CreateSlab(root.transform, "Wall_N");
            wall.WallSouth = CreateSlab(root.transform, "Wall_S");
            wall.WallEast = CreateSlab(root.transform, "Wall_E");
            wall.WallWest = CreateSlab(root.transform, "Wall_W");
            wall.WallCeiling = CreateSlab(root.transform, "Wall_Ceiling");
            wall.ApplyLayout();

            BindToGameManager(wall);
            return wall;
        }

        static Transform CreateSlab(Transform parent, string name)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            var rend = slab.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (mat != null) rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            var col = slab.GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = false;
                col.enabled = true;
            }

            return slab.transform;
        }

        static void BindToGameManager(BrawlAirWall wall)
        {
            BrawlGameManager gm = Object.FindObjectOfType<BrawlGameManager>(true);
            if (gm == null) return;
            if (gm.AirWall == wall) return;
            Undo.RecordObject(gm, "Bind AirWall");
            gm.AirWall = wall;
            EditorUtility.SetDirty(gm);
        }
    }
}
