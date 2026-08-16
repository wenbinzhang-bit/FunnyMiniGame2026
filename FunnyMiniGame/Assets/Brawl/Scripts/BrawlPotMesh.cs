using System.Collections.Generic;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 甩锅关用的铁锅网格。项目里没有现成锅模型，运行时生成一只浅口铁锅。
    /// </summary>
    public static class BrawlPotMesh
    {
        static Mesh sharedMesh;
        static Material sharedMaterial;

        public static Mesh SharedMesh => sharedMesh != null ? sharedMesh : (sharedMesh = Build());

        public static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                Shader shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                sharedMaterial = shader != null
                    ? new Material(shader)
                    : new Material(Shader.Find("Diffuse"));
                sharedMaterial.name = "BrawlIronPot";
                sharedMaterial.color = new Color(0.16f, 0.14f, 0.13f, 1f);
                if (sharedMaterial.HasProperty("_Metallic"))
                    sharedMaterial.SetFloat("_Metallic", 0.82f);
                if (sharedMaterial.HasProperty("_Glossiness"))
                    sharedMaterial.SetFloat("_Glossiness", 0.38f);
                if (sharedMaterial.HasProperty("_Smoothness"))
                    sharedMaterial.SetFloat("_Smoothness", 0.38f);
                return sharedMaterial;
            }
        }

        public static void ApplyTo(KpiComputerObjective objective)
        {
            if (objective == null || !BrawlGameManager.PassTheBuckActive) return;

            Transform visual = objective.transform.Find("PotVisual");
            if (visual == null)
            {
                var go = new GameObject("PotVisual");
                visual = go.transform;
                visual.SetParent(objective.transform, false);
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
                visual.localScale = Vector3.one;
                go.AddComponent<MeshFilter>().sharedMesh = SharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = SharedMaterial;
            }

            Renderer[] laptop = objective.GetComponentsInChildren<Renderer>(true);
            var potRenderers = new List<Renderer>();
            for (int i = 0; i < laptop.Length; i++)
            {
                Renderer renderer = laptop[i];
                if (renderer == null) continue;
                bool isPot = renderer.transform == visual || renderer.transform.IsChildOf(visual);
                renderer.enabled = isPot;
                if (isPot) potRenderers.Add(renderer);
            }

            if (potRenderers.Count > 0)
                objective.PickupRenderers = potRenderers.ToArray();
        }

        static Mesh Build()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // 浅口铁锅剖面：半径、高度。从锅底到锅沿。
            Vector2[] profile =
            {
                new Vector2(0.00f, 0.000f),
                new Vector2(0.05f, 0.006f),
                new Vector2(0.11f, 0.024f),
                new Vector2(0.17f, 0.058f),
                new Vector2(0.21f, 0.100f),
                new Vector2(0.235f, 0.142f),
                new Vector2(0.248f, 0.168f),
                new Vector2(0.252f, 0.182f),
                new Vector2(0.236f, 0.188f)
            };

            const int segments = 28;
            Lathe(profile, segments, vertices, triangles);
            AddHandle(new Vector3(0.236f, 0.155f, 0f), Vector3.right, vertices, triangles);
            AddHandle(new Vector3(-0.236f, 0.155f, 0f), Vector3.left, vertices, triangles);

            var mesh = new Mesh { name = "BrawlIronPot" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Lathe(
            Vector2[] profile,
            int segments,
            List<Vector3> vertices,
            List<int> triangles)
        {
            int rings = profile.Length;
            int start = vertices.Count;
            for (int i = 0; i < rings; i++)
            {
                Vector2 cur = profile[i];
                for (int s = 0; s < segments; s++)
                {
                    float angle = s * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(cur.x * Mathf.Cos(angle), cur.y, cur.x * Mathf.Sin(angle)));
                }
            }

            for (int i = 0; i < rings - 1; i++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = start + i * segments + s;
                    int b = start + i * segments + (s + 1) % segments;
                    int c = start + (i + 1) * segments + s;
                    int d = start + (i + 1) * segments + (s + 1) % segments;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }
        }

        static void AddHandle(
            Vector3 root,
            Vector3 outward,
            List<Vector3> vertices,
            List<int> triangles)
        {
            const int tubeSegments = 8;
            const int pathCount = 10;
            const float radius = 0.018f;
            Vector3 side = Vector3.Cross(outward, Vector3.up).normalized;
            var path = new Vector3[pathCount];
            for (int i = 0; i < pathCount; i++)
            {
                float t = i / (pathCount - 1f);
                float swing = Mathf.Sin(t * Mathf.PI);
                path[i] = root
                    + outward * (0.03f + swing * 0.055f)
                    + side * Mathf.Lerp(-0.055f, 0.055f, t)
                    + Vector3.up * (swing * 0.012f);
            }

            int start = vertices.Count;
            for (int i = 0; i < pathCount; i++)
            {
                Vector3 tangent = i < pathCount - 1 ? (path[i + 1] - path[i]).normalized : (path[i] - path[i - 1]).normalized;
                Vector3 n1 = Vector3.Cross(tangent, Vector3.up);
                if (n1.sqrMagnitude < 0.001f) n1 = Vector3.Cross(tangent, side);
                n1.Normalize();
                Vector3 n2 = Vector3.Cross(tangent, n1).normalized;
                for (int s = 0; s < tubeSegments; s++)
                {
                    float angle = s * Mathf.PI * 2f / tubeSegments;
                    Vector3 n = (n1 * Mathf.Cos(angle) + n2 * Mathf.Sin(angle)).normalized;
                    vertices.Add(path[i] + n * radius);
                }
            }

            for (int i = 0; i < pathCount - 1; i++)
            {
                for (int s = 0; s < tubeSegments; s++)
                {
                    int a = start + i * tubeSegments + s;
                    int b = start + i * tubeSegments + (s + 1) % tubeSegments;
                    int c = start + (i + 1) * tubeSegments + s;
                    int d = start + (i + 1) * tubeSegments + (s + 1) % tubeSegments;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }
        }
    }
}
