using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FIMSpace.FProceduralAnimation;
using FIMSpace.RagdollAnimatorDemo;
using Mirror;
using UnityEditor;
using UnityEngine;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 把四个已换皮的 Punch Demo 角色补成 Mirror 玩家 Prefab，并写入 MiniGame_01 的角色表。
    /// 仅当角色表仍未指向这四个角色时自动执行，不会覆盖后续人工调整。
    /// </summary>
    public static class MiniGame01PlayerModelsSetup
    {
        const string SessionKey = "Brawl.MiniGame01PlayerModelsSetup.RanV8AttackTiming";
        const string HitVoicePath = "Assets/Brawl/Resources/Audio/HitVoice_Ah.mp3";
        const string LaptopAnimatorPath = "Assets/Brawl/Animations/AC_BrawlLaptop.overrideController";
        const string PuncherVictimPrefabPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Demos Assets/Prefabs/PR_PuncherVictim_Mannequin.prefab";

        static readonly string[] CharacterPrefabPaths =
        {
            "Assets/Brawl/Prefabs/Characters/FAnnequinV2_New1.prefab", // Man_77
            "Assets/Brawl/Prefabs/Characters/FAnnequinV2_New2.prefab", // Man_99
            "Assets/Brawl/Prefabs/Characters/FAnnequinV2_New3.prefab", // Man_33
            "Assets/Brawl/Prefabs/Characters/FAnnequinV2_New4.prefab"  // Girl_55
        };

        static readonly string[] PlayerModelsPaths =
        {
            "Assets/Brawl/Configs/PlayerModels.asset",
            "Assets/Brawl/Resources/PlayerModels.asset"
        };

        [InitializeOnLoadMethod]
        static void ScheduleAutoSetup()
        {
            EditorApplication.update -= AutoSetup;
            EditorApplication.update += AutoSetup;
        }

        static void AutoSetup()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            EditorApplication.update -= AutoSetup;
            SessionState.SetBool(SessionKey, true);
            if (!NeedsSetup()) return;

            ConfigureFourOnlineCharacters();
        }

        [MenuItem("Brawl/Configure MiniGame 01 Player Models")]
        public static void ConfigureFourOnlineCharacters()
        {
            try
            {
                var configuredPrefabs = CharacterPrefabPaths
                    .Select(ConfigureNetworkPlayerPrefab)
                    .ToArray();

                foreach (string modelsPath in PlayerModelsPaths)
                {
                    PlayerModels models = AssetDatabase.LoadAssetAtPath<PlayerModels>(modelsPath);
                    if (models == null)
                        throw new InvalidOperationException($"找不到 PlayerModels: {modelsPath}");

                    Undo.RecordObject(models, "Assign MiniGame 01 player models");
                    models.prefabs = configuredPrefabs;
                    EditorUtility.SetDirty(models);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateConfiguration();

                Debug.Log("BRAWL_PLAYER_MODELS: SUCCESS - P1=Man_77, P2=Man_99, P3=Man_33, P4=Girl_55");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("BRAWL_PLAYER_MODELS: FAILED - 可从菜单 Brawl/Configure MiniGame 01 Player Models 重试。");
            }
        }

        static GameObject ConfigureNetworkPlayerPrefab(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
                throw new InvalidOperationException($"无法打开角色 Prefab: {prefabPath}");

            try
            {
                FBasic_RigidbodyMover mover = root.GetComponent<FBasic_RigidbodyMover>();
                Demo_Ragd_Hero1 hero = root.GetComponent<Demo_Ragd_Hero1>();
                Rigidbody body = root.GetComponent<Rigidbody>();
                CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
                Animator visualAnimator = hero != null ? hero.Mecanim : null;
                RuntimeAnimatorController laptopAnimator =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(LaptopAnimatorPath);

                if (mover == null || hero == null || body == null || capsule == null || visualAnimator == null
                    || laptopAnimator == null)
                    throw new InvalidOperationException($"角色缺少移动、攻击、刚体、胶囊体或外观 Animator: {prefabPath}");
                if (visualAnimator.transform == root.transform)
                    throw new InvalidOperationException($"角色仍在使用根节点默认 Animator，没有绑定换皮模型: {prefabPath}");

                mover.Mecanim = visualAnimator;
                visualAnimator.runtimeAnimatorController = laptopAnimator;
                visualAnimator.enabled = true;
                visualAnimator.applyRootMotion = false;
                mover.UpdateInput = false;
                mover.DisableRootMotion = true;
                hero.Mover = mover;
                hero.Mecanim = visualAnimator;
                hero.ProcessInput = false;

                RagdollAnimator2 ragdoll = ConfigurePuncherVictimRagdoll(root, visualAnimator);
                hero.Ragdoll = ragdoll;

                body.mass = 20f;
                body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                capsule.radius = Mathf.Max(0.38f, capsule.radius);
                capsule.enabled = true;
                capsule.isTrigger = false;

                NetworkIdentity identity = GetOrAdd<NetworkIdentity>(root);
                identity.serverOnly = false;

                NetworkTransformReliable networkTransform = GetOrAdd<NetworkTransformReliable>(root);
                networkTransform.target = root.transform;
                networkTransform.syncDirection = SyncDirection.ServerToClient;

                NetworkAnimator networkAnimator = GetOrAdd<NetworkAnimator>(root);
                networkAnimator.animator = visualAnimator;
                networkAnimator.clientAuthority = false;
                networkAnimator.syncDirection = SyncDirection.ServerToClient;

                NetFAnnequinController controller = GetOrAdd<NetFAnnequinController>(root);
                controller.Mover = mover;
                controller.Hero = hero;
                controller.Mecanim = visualAnimator;
                controller.Ragdoll = ragdoll;
                controller.PunchAnimationLockSeconds = 1.25f;
                controller.PunchMovementLockSeconds = 1.23f;
                controller.UppercutAnimationLockSeconds = 0.8f;
                controller.PunchHitRange = 2.4f;
                controller.PunchHitAngle = 55f;
                controller.PunchPointBlankRange = 0.55f;
                controller.HitVoiceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(HitVoicePath);
                controller.HitVoiceVolume = 0.9f;
                controller.HitVoicePitchRange = new Vector2(0.96f, 1.04f);
                controller.ComputerPickupRange = 2.3f;
                controller.ComputerPickupAngle = 120f;
                controller.ComputerPickupPointBlank = 1.75f;
                controller.ComputerPickupAnimationSeconds = 0.45f;
                controller.ComputerHoldOffset = new Vector3(0f, 0.06f, 0.08f);
                controller.ComputerHoldEuler = new Vector3(8f, 0f, 0f);
                controller.ComputerDropForward = 0.8f;
                controller.TurboDurationSeconds = 5f;
                controller.TurboRechargeSeconds = 5f;
                controller.KnockdownGroundSeconds = 1.55f;
                controller.GetUpFaceSeconds = 1.35f;
                controller.GetUpBackSeconds = 1.65f;
                controller.KnockdownSlideSpeed = 5.3f;
                controller.KnockdownLiftSpeed = 3f;
                controller.KnockbackControlSeconds = 1f;
                controller.KnockbackDeceleration = 5.3f;
                controller.KnockbackSpinSpeed = 1.25f;
                controller.KnockbackSpinDeceleration = 1.5f;
                controller.RagdollHorizontalForceScale = 0.31f;
                controller.VisualFallAngle = 82f;
                controller.VisualTumbleDegrees = 180f;
                controller.VisualFallDelay = 0.1f;
                controller.VisualFallSeconds = 0.5f;
                controller.VisualFallLift = 0.02f;
                controller.GetUpAlignmentBlendSeconds = 0.5f;

                RagdollNetworkSync poseSync = GetOrAdd<RagdollNetworkSync>(root);
                poseSync.enabled = true;
                poseSync.SendsPerSecond = 25f;
                poseSync.BufferTimeMultiplier = 2f;

                GetOrAdd<LocalCameraRig>(root);
                GetOrAdd<PlayerAttributes>(root);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (saved == null)
                    throw new InvalidOperationException($"保存联网角色失败: {prefabPath}");

                // LoadPrefabContents 中的对象会被 Mirror 当作临时场景对象并把 AssetId 清零。
                // 首次保存为真正的 Prefab 资产后再写一次，才能持久化到磁盘并用于玩家生成。
                NetworkIdentity savedIdentity = saved.GetComponent<NetworkIdentity>();
                PersistNetworkAssetId(savedIdentity, prefabPath);
                PrefabUtility.SavePrefabAsset(saved);

                return saved;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static T GetOrAdd<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            return component != null ? component : root.AddComponent<T>();
        }

        static RagdollAnimator2 ConfigurePuncherVictimRagdoll(GameObject root, Animator visualAnimator)
        {
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PuncherVictimPrefabPath);
            RagdollAnimator2 source = sourcePrefab != null
                ? sourcePrefab.GetComponent<RagdollAnimator2>()
                : null;
            if (source == null)
                throw new InvalidOperationException($"找不到 Punch Demo Bot 的 RagdollAnimator2: {PuncherVictimPrefabPath}");

            RagdollAnimator2 target = GetOrAdd<RagdollAnimator2>(root);
            List<RagdollAnimatorFeatureHelper> copiedFeatures = CloneBotFeatures(source.Settings.ExtraFeatures);

            // 复制 Bot 的质量、弹簧、阻尼、速度限制等公共参数；骨骼链必须按当前皮肤重新生成。
            source.Settings.ApplyAllPropertiesToOtherRagdoll(target.Settings);
            target.Settings.ExtraFeatures = copiedFeatures;
            target.Settings.BaseTransform = root.transform;
            target.Settings.HelperOwnerTransform = root.transform;
            target.Settings.Mecanim = visualAnimator;
            target.Settings.TargetParentForRagdollDummy = null;
            target.Settings.TryFindBones(false);
            foreach (RagdollBonesChain chain in target.Settings.Chains)
            {
                chain.AutoAdjustColliders(target.Settings.IsHumanoid);
                chain.AutoAdjustPhysics();
            }
            target.Settings.StoreReferenceTPose();
            target.UpdateAllAfterManualChanges();
            target.enabled = true;
            EditorUtility.SetDirty(target);
            return target;
        }

        static List<RagdollAnimatorFeatureHelper> CloneBotFeatures(
            List<RagdollAnimatorFeatureHelper> sourceFeatures)
        {
            var result = new List<RagdollAnimatorFeatureHelper>();
            foreach (RagdollAnimatorFeatureHelper source in sourceFeatures)
            {
                if (source == null || source.FeatureReference == null) continue;

                var copy = new RagdollAnimatorFeatureHelper
                {
                    FeatureReference = source.FeatureReference,
                    CustomName = source.CustomName,
                    customStringList = source.customStringList != null
                        ? new List<string>(source.customStringList)
                        : new List<string>(),
                    // Bot 事件引用的是 Bot 自己的 Mover/Collider，玩家 Prefab 不能照搬；
                    // 保留同样数量的空事件，玩法脚本负责锁定和恢复控制。
                    customEventsList = source.customEventsList != null
                        ? source.customEventsList.Select(_ => new UnityEngine.Events.UnityEvent()).ToList()
                        : new List<UnityEngine.Events.UnityEvent>(),
                    customObjectList = new List<UnityEngine.Object>()
                };
                copy.Enabled = source.Enabled;
                copy.CopySettingsFrom(source);

                // 玩家使用 Hero Puncher Controller，其中对应状态名是 Fall，不是 Victim 的 Fall Pose。
                if (copy.HasVariable("Fall Animation:"))
                    copy.RequestVariable("Fall Animation:", "Fall").SetValue("Fall");

                result.Add(copy);
            }
            return result;
        }

        static void PersistNetworkAssetId(NetworkIdentity identity, string prefabPath)
        {
            string assetGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(assetGuid))
                throw new InvalidOperationException($"角色 Prefab 没有有效 GUID: {prefabPath}");

            uint assetId = NetworkIdentity.AssetGuidToUint(new Guid(assetGuid));
            if (assetId == 0)
                throw new InvalidOperationException($"角色 NetworkIdentity 生成了无效 AssetId: {prefabPath}");

            var serializedIdentity = new SerializedObject(identity);
            SerializedProperty assetIdProperty = serializedIdentity.FindProperty("_assetId");
            assetIdProperty.longValue = assetId;
            serializedIdentity.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(identity);
        }

        static bool HasPersistentNetworkAssetId(NetworkIdentity identity)
        {
            if (identity == null) return false;
            var serializedIdentity = new SerializedObject(identity);
            if (serializedIdentity.FindProperty("_assetId").longValue == 0) return false;

            string prefabPath = AssetDatabase.GetAssetPath(identity.gameObject);
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath)) return false;
            string serializedPrefab = File.ReadAllText(prefabPath);
            return !serializedPrefab.Contains("  _assetId: 0");
        }

        static bool NeedsSetup()
        {
            RuntimeAnimatorController expectedAnimator =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(LaptopAnimatorPath);
            GameObject[] expected = CharacterPrefabPaths
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .ToArray();

            if (expected.Any(prefab => prefab == null)) return false;
            if (expectedAnimator == null) return true;
            if (expected.Any(prefab => !HasPersistentNetworkAssetId(prefab.GetComponent<NetworkIdentity>())
                || prefab.GetComponent<NetFAnnequinController>() == null
                || prefab.GetComponent<RagdollAnimator2>() == null
                || prefab.GetComponent<RagdollNetworkSync>() == null
                || prefab.GetComponent<Demo_Ragd_Hero1>()?.Ragdoll == null
                || prefab.GetComponent<Demo_Ragd_Hero1>()?.Mecanim == null
                || !prefab.GetComponent<Demo_Ragd_Hero1>().Mecanim.enabled
                || prefab.GetComponent<Demo_Ragd_Hero1>().Mecanim.runtimeAnimatorController != expectedAnimator)) return true;

            foreach (string modelsPath in PlayerModelsPaths)
            {
                PlayerModels models = AssetDatabase.LoadAssetAtPath<PlayerModels>(modelsPath);
                if (models == null || models.prefabs == null || !models.prefabs.SequenceEqual(expected))
                    return true;
            }

            return false;
        }

        [MenuItem("Brawl/Validate MiniGame 01 Player Models")]
        public static void ValidateConfiguration()
        {
            RuntimeAnimatorController expectedAnimator =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(LaptopAnimatorPath);
            GameObject[] expected = CharacterPrefabPaths
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .ToArray();

            for (int i = 0; i < expected.Length; i++)
            {
                GameObject prefab = expected[i];
                NetworkIdentity identity = prefab != null ? prefab.GetComponent<NetworkIdentity>() : null;
                NetFAnnequinController controller = prefab != null ? prefab.GetComponent<NetFAnnequinController>() : null;
                RagdollAnimator2 ragdoll = prefab != null ? prefab.GetComponent<RagdollAnimator2>() : null;

                bool valid = prefab != null
                    && identity != null
                    && HasPersistentNetworkAssetId(identity)
                    && identity.assetId != 0
                    && controller != null
                    && controller.Mover != null
                    && controller.Hero != null
                    && controller.Mecanim != null
                    && controller.Ragdoll == ragdoll
                    && ragdoll != null
                    && ragdoll.Settings.ExtraFeatures != null
                    && ragdoll.Settings.ExtraFeatures.Count >= 7
                    && controller.Hero.Ragdoll == ragdoll
                    && controller.Mecanim.transform != prefab.transform
                    && controller.Mecanim.enabled
                    && controller.Mecanim.runtimeAnimatorController == expectedAnimator
                    && prefab.GetComponent<NetworkTransformReliable>() != null
                    && prefab.GetComponent<NetworkAnimator>() != null
                    && prefab.GetComponent<RagdollNetworkSync>() != null
                    && prefab.GetComponent<LocalCameraRig>() != null
                    && prefab.GetComponent<PlayerAttributes>() != null;

                if (!valid)
                    throw new InvalidOperationException($"联网角色校验失败: {CharacterPrefabPaths[i]}");
            }

            foreach (string modelsPath in PlayerModelsPaths)
            {
                PlayerModels models = AssetDatabase.LoadAssetAtPath<PlayerModels>(modelsPath);
                if (models == null || models.prefabs == null || !models.prefabs.SequenceEqual(expected))
                    throw new InvalidOperationException($"PlayerModels 列表不正确: {modelsPath}");
            }

            Debug.Log("BRAWL_PLAYER_MODELS_VALIDATE: SUCCESS - four custom network player prefabs are ready.");
        }
    }
}
