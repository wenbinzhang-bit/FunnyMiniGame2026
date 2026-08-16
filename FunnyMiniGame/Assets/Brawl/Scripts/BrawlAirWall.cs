using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl
{
    /// <summary>
    /// 场景里的空气墙实体。直接拖父物体或四堵墙调整范围，倒计时结束后由对局管理器关掉。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BrawlAirWall : MonoBehaviour
    {
        public static BrawlAirWall Instance { get; private set; }

        [Header("场景墙体（可直接选中拖动）")]
        public Transform WallNorth;
        public Transform WallSouth;
        public Transform WallEast;
        public Transform WallWest;
        public Transform WallCeiling;

        [Header("尺寸（改完会重排子物体）")]
        [Tooltip("内部可活动空间：X 宽、Y 高、Z 深")]
        public Vector3 InnerSize = new Vector3(12f, 8f, 16f);
        [Min(0.2f)] public float Thickness = 0.6f;
        [Tooltip("勾选后改尺寸自动重排墙；关掉后可以单独拖每堵墙")]
        public bool LockWallsToSize = true;

        public static BrawlAirWall Ensure(BrawlGameManager gm)
        {
            if (Instance == null)
                Instance = null;
            if (gm != null && gm.AirWall == null)
                gm.AirWall = null;

            Scene active = SceneManager.GetActiveScene();
            if (Instance != null && Instance.gameObject.scene == active)
                return Instance;

            Instance = null;
            if (gm != null && gm.AirWall != null && gm.AirWall.gameObject.scene == active)
            {
                Instance = gm.AirWall;
                return Instance;
            }

            Instance = FindObjectOfType<BrawlAirWall>(true);
            if (gm != null)
                gm.AirWall = Instance;
            return Instance;
        }

        public static BrawlAirWall EnsureInLevel(BrawlGameManager gm)
        {
            BrawlAirWall wall = Ensure(gm);
            if (wall != null) return wall;
            if (!BrawlLevelCatalog.IsLevel(SceneManager.GetActiveScene().name))
                return null;

            wall = CreateRuntimeInActiveScene();
            if (gm != null)
                gm.AirWall = wall;
            return wall;
        }

        public static BrawlAirWall CreateRuntimeInActiveScene()
        {
            var root = new GameObject("AirWall");
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
            Instance = wall;
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
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            return slab.transform;
        }

        public static void ClearStale()
        {
            if (Instance == null)
                Instance = null;
            cachedWalls = null;
            cachedSceneHandle = -1;
        }

        static BrawlAirWall[] cachedWalls;
        static int cachedSceneHandle = -1;

        public static void SetAllActive(bool active)
        {
            int handle = SceneManager.GetActiveScene().handle;
            if (cachedWalls == null || cachedWalls.Length == 0 || cachedWalls[0] == null || cachedSceneHandle != handle)
            {
                cachedWalls = FindObjectsOfType<BrawlAirWall>(true);
                cachedSceneHandle = handle;
            }

            if (cachedWalls == null) return;
            for (int i = 0; i < cachedWalls.Length; i++)
            {
                if (cachedWalls[i] != null)
                    cachedWalls[i].SetActiveWall(active);
            }
        }

        public void SetActiveWall(bool active)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (active && (Mathf.Abs(transform.localScale.x) < 0.99f || Mathf.Abs(transform.localScale.z) < 0.99f))
                transform.localScale = Vector3.one;

            BindChildren();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null)
                    child.gameObject.SetActive(active);
            }

            SetChildActive(WallNorth, active);
            SetChildActive(WallSouth, active);
            SetChildActive(WallEast, active);
            SetChildActive(WallWest, active);
            SetChildActive(WallCeiling, active);

            foreach (Collider col in GetComponentsInChildren<Collider>(true))
            {
                if (col != null) col.enabled = active;
            }

            foreach (Renderer rend in GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null) continue;
                rend.enabled = active;
                if (active && rend.material != null)
                {
                    Color color = rend.material.color;
                    if (color.a < 0.2f)
                    {
                        color.a = 0.35f;
                        rend.material.color = color;
                    }
                }
            }
        }

        static void SetChildActive(Transform wall, bool active)
        {
            if (wall != null)
                wall.gameObject.SetActive(active);
        }

        public bool Contains(Vector3 worldPos, float inset = 0.35f)
        {
            GetInnerBox(out Vector3 center, out Vector3 half);
            Vector3 local = worldPos - center;
            return Mathf.Abs(local.x) <= half.x - inset
                && Mathf.Abs(local.z) <= half.z - inset
                && local.y <= half.y * 2f - inset;
        }

        public Vector3 ClampInside(Vector3 worldPos, float inset = 0.6f)
        {
            GetInnerBox(out Vector3 center, out Vector3 half);
            Vector3 local = worldPos - center;
            local.x = Mathf.Clamp(local.x, -half.x + inset, half.x - inset);
            local.z = Mathf.Clamp(local.z, -half.z + inset, half.z - inset);
            local.y = Mathf.Clamp(local.y, 0.2f, half.y * 2f - inset);
            return center + local;
        }

        [ContextMenu("按尺寸重排墙体")]
        public void ApplyLayout()
        {
            BindChildren();
            float hx = Mathf.Max(0.5f, InnerSize.x) * 0.5f;
            float hy = Mathf.Max(1f, InnerSize.y) * 0.5f;
            float hz = Mathf.Max(0.5f, InnerSize.z) * 0.5f;
            float t = Mathf.Max(0.2f, Thickness);
            float height = hy * 2f;

            SetLocal(WallNorth, new Vector3(0f, hy, hz + t * 0.5f), new Vector3(hx * 2f + t, height, t));
            SetLocal(WallSouth, new Vector3(0f, hy, -hz - t * 0.5f), new Vector3(hx * 2f + t, height, t));
            SetLocal(WallEast, new Vector3(hx + t * 0.5f, hy, 0f), new Vector3(t, height, hz * 2f));
            SetLocal(WallWest, new Vector3(-hx - t * 0.5f, hy, 0f), new Vector3(t, height, hz * 2f));
            SetLocal(WallCeiling, new Vector3(0f, height + t * 0.5f, 0f), new Vector3(hx * 2f + t, t, hz * 2f + t));
        }

        void OnEnable()
        {
            Instance = this;
            BindChildren();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!LockWallsToSize) return;
            if (WallNorth == null || WallSouth == null || WallEast == null || WallWest == null) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && LockWallsToSize)
                    ApplyLayout();
            };
        }

        void OnDrawGizmos()
        {
            GetInnerBox(out Vector3 center, out Vector3 half);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.18f);
            Gizmos.DrawCube(center + Vector3.up * half.y, new Vector3(half.x * 2f, half.y * 2f, half.z * 2f));
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(center + Vector3.up * half.y, new Vector3(half.x * 2f, half.y * 2f, half.z * 2f));
        }
#endif

        void BindChildren()
        {
            if (WallNorth == null) WallNorth = transform.Find("Wall_N");
            if (WallSouth == null) WallSouth = transform.Find("Wall_S");
            if (WallEast == null) WallEast = transform.Find("Wall_E");
            if (WallWest == null) WallWest = transform.Find("Wall_W");
            if (WallCeiling == null) WallCeiling = transform.Find("Wall_Ceiling");
        }

        void GetInnerBox(out Vector3 center, out Vector3 half)
        {
            BindChildren();
            if (WallNorth != null && WallSouth != null && WallEast != null && WallWest != null)
            {
                float minX = WallWest.position.x + WallWest.lossyScale.x * 0.5f;
                float maxX = WallEast.position.x - WallEast.lossyScale.x * 0.5f;
                float minZ = WallSouth.position.z + WallSouth.lossyScale.z * 0.5f;
                float maxZ = WallNorth.position.z - WallNorth.lossyScale.z * 0.5f;
                center = new Vector3((minX + maxX) * 0.5f, transform.position.y, (minZ + maxZ) * 0.5f);
                half = new Vector3(
                    Mathf.Max(0.5f, (maxX - minX) * 0.5f),
                    Mathf.Max(1f, InnerSize.y) * 0.5f,
                    Mathf.Max(0.5f, (maxZ - minZ) * 0.5f));
                return;
            }

            center = transform.position;
            half = new Vector3(InnerSize.x * 0.5f, InnerSize.y * 0.5f, InnerSize.z * 0.5f);
        }

        static void SetLocal(Transform wall, Vector3 localPos, Vector3 scale)
        {
            if (wall == null) return;
            wall.localPosition = localPos;
            wall.localRotation = Quaternion.identity;
            wall.localScale = scale;
        }
    }
}
