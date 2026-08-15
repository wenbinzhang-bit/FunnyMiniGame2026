using System;
using FIMSpace.RagdollAnimatorDemo;
using Mirror;
using UnityEditor;
using UnityEngine;

namespace Brawl.EditorTools
{
    /// <summary>Configures the Resources/computer prefab as a throwable KPI objective.</summary>
    public static class BrawlComputerPrefabSetup
    {
        const string ComputerPrefabPath = "Assets/Resources/computer.prefab";

        [InitializeOnLoadMethod]
        static void ScheduleAutoSetup()
        {
            EditorApplication.update -= AutoSetup;
            EditorApplication.update += AutoSetup;
        }

        static void AutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= AutoSetup;
            SetupComputerPrefab();
        }

        [MenuItem("Brawl/Setup KPI Computer Prefab")]
        public static void SetupComputerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ComputerPrefabPath);
            if (root == null)
                throw new InvalidOperationException($"找不到电脑 Prefab: {ComputerPrefabPath}");

            try
            {
                BoxCollider collider = root.GetComponent<BoxCollider>();
                if (collider == null) collider = root.AddComponent<BoxCollider>();
                collider.isTrigger = false;
                collider.enabled = true;

                Rigidbody body = root.GetComponent<Rigidbody>();
                if (body == null) body = root.AddComponent<Rigidbody>();
                body.mass = 2f;
                body.drag = 0.08f;
                body.angularDrag = 0.8f;
                body.useGravity = true;
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.constraints = RigidbodyConstraints.None;
                body.maxAngularVelocity = 25f;

                NetworkIdentity identity = root.GetComponent<NetworkIdentity>();
                if (identity == null) identity = root.AddComponent<NetworkIdentity>();
                identity.serverOnly = false;

                NetworkTransformReliable networkTransform = root.GetComponent<NetworkTransformReliable>();
                if (networkTransform == null) networkTransform = root.AddComponent<NetworkTransformReliable>();
                networkTransform.target = root.transform;
                networkTransform.syncDirection = SyncDirection.ServerToClient;

                ClayCatchable catchable = root.GetComponent<ClayCatchable>();
                if (catchable == null) catchable = root.AddComponent<ClayCatchable>();
                catchable.Moving = true;

                KpiComputerObjective objective = root.GetComponent<KpiComputerObjective>();
                if (objective == null) objective = root.AddComponent<KpiComputerObjective>();
                objective.WinningKpi = 99f;
                objective.PointsPerHoldTick = 1;
                objective.KpiPerHeldSecond = 1f;
                objective.Body = body;

                EditorUtility.SetDirty(collider);
                EditorUtility.SetDirty(body);
                EditorUtility.SetDirty(identity);
                EditorUtility.SetDirty(networkTransform);
                EditorUtility.SetDirty(catchable);
                EditorUtility.SetDirty(objective);
                PrefabUtility.SaveAsPrefabAsset(root, ComputerPrefabPath);
                AssetDatabase.SaveAssets();

                Debug.Log("BRAWL_COMPUTER_SETUP: SUCCESS - networked Rigidbody, pickup target and KPI=99 configured.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
