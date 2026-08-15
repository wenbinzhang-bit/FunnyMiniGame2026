using System;
using System.Collections.Generic;
using System.Linq;
using FIMSpace.RagdollAnimatorDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Brawl.EditorTools
{
    /// <summary>
    /// 从 TestScene 中已验证过的 Man_77 攻击玩家复制出四套换皮 Prefab。
    /// 保留移动、跳跃、攻击、抓取与音效设置，只替换 Humanoid 外观和骨骼引用（构建版 2）。
    /// </summary>
    public static class BrawlCharacterPrefabBuilder
    {
        const string TestScenePath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/TestScene.unity";
        const string ControllerPath = "Assets/FImpossible Creations/Plugins - Animating/Ragdoll Animator 2/Ragdoll Animator 2 - Demo/Demos Assets/Additional Resources/AC_RagdollAnimator_Hero Puncher.controller";
        const string OutputFolder = "Assets/Brawl/Prefabs/Characters";
        const string CatalogPath = OutputFolder + "/BrawlCharacterCatalog.asset";
        const string PunchSwingHitPath = "Assets/Brawl/Audio/PunchSwing_Hit.mp3";

        static readonly CharacterDefinition[] Characters =
        {
            new CharacterDefinition("FAnnequinV2_New1", "Assets/Creator Of Path/Models/man/Man_77.FBX"),
            new CharacterDefinition("FAnnequinV2_New2", "Assets/Creator Of Path/Models/man/Man_99.FBX"),
            new CharacterDefinition("FAnnequinV2_New3", "Assets/Creator Of Path/Models/man/Man_33.FBX"),
            new CharacterDefinition("FAnnequinV2_New4", "Assets/Creator Of Path/Models/girl/Girl_55.FBX")
        };

        [InitializeOnLoadMethod]
        static void AutoBuildWhenMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                if (Characters.All(character => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(character)) != null)
                    && AssetDatabase.LoadAssetAtPath<BrawlCharacterCatalog>(CatalogPath) != null)
                {
                    AssignPunchVoiceAudioToPrefabs();
                    AssignPunchVoiceAudioToLoadedSceneObjects();
                    ValidateBuiltPrefabs();
                    return;
                }

                BuildFourCharacterPrefabs();
            };
        }

        [MenuItem("Brawl/Build Four Demo Character Prefabs")]
        public static void BuildFourCharacterPrefabs()
        {
            try
            {
                EnsureOutputFolder();
                EnsureHumanoidImports();

                RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                if (controller == null)
                    throw new InvalidOperationException($"攻击 Animator Controller 不存在: {ControllerPath}");

                bool openedTemplateScene = false;
                Scene templateScene = SceneManager.GetSceneByPath(TestScenePath);
                if (!templateScene.IsValid() || !templateScene.isLoaded)
                {
                    templateScene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Additive);
                    openedTemplateScene = true;
                }

                Demo_Ragd_Hero1 templateHero = FindConfiguredTemplate(templateScene);
                if (templateHero == null)
                    throw new InvalidOperationException("TestScene 中没有找到 Mecanim 指向 Man_77 的 Demo_Ragd_Hero1 模板。请先保留当前已调通的玩家对象。 ");

                Scene previousActiveScene = SceneManager.GetActiveScene();
                Scene buildScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var savedPrefabs = new List<GameObject>(Characters.Length);

                try
                {
                    SceneManager.SetActiveScene(buildScene);

                    foreach (CharacterDefinition character in Characters)
                    {
                        GameObject saved = BuildOne(templateHero.gameObject, character, controller);
                        savedPrefabs.Add(saved);
                    }
                }
                finally
                {
                    if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                        SceneManager.SetActiveScene(previousActiveScene);
                    EditorSceneManager.CloseScene(buildScene, true);
                }

                BuildCatalog(savedPrefabs.ToArray());
                ValidateBuiltPrefabs();

                if (openedTemplateScene)
                    EditorSceneManager.CloseScene(templateScene, true);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("BRAWL_CHARACTER_BUILD: SUCCESS - New1=Man_77, New2=Man_99, New3=Man_33, New4=Girl_55");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("BRAWL_CHARACTER_BUILD: FAILED - 可从菜单 Brawl/Build Four Demo Character Prefabs 重新执行。");
            }
        }

        static GameObject BuildOne(GameObject template, CharacterDefinition character, RuntimeAnimatorController controller)
        {
            GameObject clone = Object.Instantiate(template);
            clone.name = character.PrefabName;
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.transform.localScale = Vector3.one;

            try
            {
                Demo_Ragd_Hero1 hero = clone.GetComponent<Demo_Ragd_Hero1>();
                FBasic_RigidbodyMover mover = clone.GetComponent<FBasic_RigidbodyMover>();
                if (hero == null || mover == null)
                    throw new InvalidOperationException($"模板 {template.name} 缺少 Demo_Ragd_Hero1 或 FBasic_RigidbodyMover。");

                // 记录并关闭 FAnnequin 原始外观；它仍保留碰撞体和移动根节点职责。
                foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;

                Animator oldVisual = clone.GetComponentsInChildren<Animator>(true)
                    .FirstOrDefault(animator => animator != clone.GetComponent<Animator>() && animator.name == "Man_77");
                if (oldVisual != null)
                    Object.DestroyImmediate(oldVisual.gameObject);

                Animator rootAnimator = clone.GetComponent<Animator>();
                if (rootAnimator != null)
                {
                    rootAnimator.enabled = false;
                    rootAnimator.runtimeAnimatorController = null;
                    rootAnimator.applyRootMotion = false;
                }

                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(character.ModelPath);
                if (modelAsset == null)
                    throw new InvalidOperationException($"角色模型不存在: {character.ModelPath}");

                GameObject visual = PrefabUtility.InstantiatePrefab(modelAsset, clone.transform) as GameObject;
                if (visual == null)
                    throw new InvalidOperationException($"无法实例化角色模型: {character.ModelPath}");

                visual.name = character.ModelName;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                Animator animator = visual.GetComponent<Animator>() ?? visual.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                    throw new InvalidOperationException($"{character.ModelName} 的 Humanoid Avatar 无效，无法重绑攻击动作。");

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;

                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = true;

                PunchAnimationEventRelay relay = visual.GetComponent<PunchAnimationEventRelay>();
                if (relay == null) relay = visual.AddComponent<PunchAnimationEventRelay>();
                var relayObject = new SerializedObject(relay);
                relayObject.FindProperty("target").objectReferenceValue = hero;
                relayObject.ApplyModifiedPropertiesWithoutUndo();
                ConfigurePunchVoice(relay, LoadPunchVoiceClips());

                mover.Mecanim = animator;
                // 移动由 Rigidbody Mover 完成，禁用不同 Animator 上的内置 Root Motion 调用。
                mover.DisableRootMotion = true;
                hero.Mover = mover;
                hero.Mecanim = animator;
                hero.Ragdoll = null;
                hero.UpperArm = FindBone(animator, HumanBodyBones.RightUpperArm, "Bip01 R UpperArm");
                hero.Hand = FindBone(animator, HumanBodyBones.RightHand, "Bip01 R Hand");

                Transform leftHand = FindBone(animator, HumanBodyBones.LeftHand, "Bip01 L Hand");
                Demo_Ragd_SetParent catchParent = clone.GetComponentsInChildren<Demo_Ragd_SetParent>(true)
                    .FirstOrDefault(component => hero.CatchMagnet != null && component.gameObject == hero.CatchMagnet.gameObject)
                    ?? clone.GetComponentInChildren<Demo_Ragd_SetParent>(true);
                if (catchParent != null) catchParent.TargetParent = leftHand;

                if (hero.UpperArm == null || hero.Hand == null || leftHand == null)
                    throw new InvalidOperationException($"{character.ModelName} 缺少攻击/抓取所需的手臂骨骼。");

                EditorUtility.SetDirty(hero);
                EditorUtility.SetDirty(mover);
                EditorUtility.SetDirty(catchParent);

                string prefabPath = PrefabPath(character);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                if (saved == null)
                    throw new InvalidOperationException($"保存 Prefab 失败: {prefabPath}");

                Debug.Log($"BRAWL_CHARACTER_BUILD: saved {prefabPath} <- {character.ModelName}");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        static Demo_Ragd_Hero1 FindConfiguredTemplate(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Demo_Ragd_Hero1 hero in root.GetComponentsInChildren<Demo_Ragd_Hero1>(true))
                {
                    if (hero.Mecanim != null && hero.Mecanim.name == "Man_77" && hero.Mover != null)
                        return hero;
                }
            }

            return null;
        }

        static Transform FindBone(Animator animator, HumanBodyBones humanBone, string fallbackName)
        {
            Transform bone = null;
            try { bone = animator.GetBoneTransform(humanBone); }
            catch (InvalidOperationException) { }

            if (bone != null) return bone;
            return animator.GetComponentsInChildren<Transform>(true).FirstOrDefault(transform => transform.name == fallbackName);
        }

        static void EnsureHumanoidImports()
        {
            foreach (CharacterDefinition character in Characters)
            {
                ModelImporter importer = AssetImporter.GetAtPath(character.ModelPath) as ModelImporter;
                if (importer == null)
                    throw new InvalidOperationException($"找不到 ModelImporter: {character.ModelPath}");

                if (importer.animationType == ModelImporterAnimationType.Human
                    && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel)
                    continue;

                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
                Debug.Log($"BRAWL_CHARACTER_BUILD: converted {character.ModelName} to Humanoid");
            }
        }

        static void BuildCatalog(GameObject[] prefabs)
        {
            BrawlCharacterCatalog catalog = AssetDatabase.LoadAssetAtPath<BrawlCharacterCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BrawlCharacterCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.EditorSetPrefabs(prefabs);
            EditorUtility.SetDirty(catalog);
        }

        [MenuItem("Brawl/Validate Four Demo Character Prefabs")]
        public static void ValidateBuiltPrefabs()
        {
            RuntimeAnimatorController expectedController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

            foreach (CharacterDefinition character in Characters)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(character));
                if (prefab == null) throw new InvalidOperationException($"缺少角色 Prefab: {PrefabPath(character)}");

                Demo_Ragd_Hero1 hero = prefab.GetComponent<Demo_Ragd_Hero1>();
                FBasic_RigidbodyMover mover = prefab.GetComponent<FBasic_RigidbodyMover>();
                Animator animator = hero != null ? hero.Mecanim : null;
                PunchAnimationEventRelay relay = prefab.GetComponentInChildren<PunchAnimationEventRelay>(true);
                Demo_Ragd_SetParent catchParent = prefab.GetComponentInChildren<Demo_Ragd_SetParent>(true);

                bool valid = hero != null
                    && mover != null
                    && animator != null
                    && animator.avatar != null
                    && animator.avatar.isHuman
                    && animator.avatar.isValid
                    && animator.runtimeAnimatorController == expectedController
                    && mover.Mecanim == animator
                    && mover.DisableRootMotion
                    && hero.UpperArm != null
                    && hero.Hand != null
                    && relay != null
                    && relay.Target == hero
                    && relay.PunchVoiceSource != null
                    && relay.PunchVoiceClips != null
                    && relay.PunchVoiceClips.Length == 1
                    && relay.PunchVoiceClips.All(clip => clip != null)
                    && catchParent != null
                    && catchParent.TargetParent != null
                    && animator.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled);

                if (!valid)
                    throw new InvalidOperationException($"角色 Prefab 引用校验失败: {PrefabPath(character)}");
            }

            BrawlCharacterCatalog catalog = AssetDatabase.LoadAssetAtPath<BrawlCharacterCatalog>(CatalogPath);
            if (catalog == null || catalog.CharacterPrefabs.Count != Characters.Length || catalog.CharacterPrefabs.Any(prefab => prefab == null))
                throw new InvalidOperationException("BrawlCharacterCatalog 必须包含四个有效角色 Prefab。");

            Debug.Log("BRAWL_CHARACTER_VALIDATE: SUCCESS - 4 prefabs, humanoid avatars, animator, punch relay and catch bones are valid.");
        }

        [MenuItem("Brawl/Assign Punch Voice Audio To Four Prefabs")]
        public static void AssignPunchVoiceAudioToPrefabs()
        {
            AudioClip[] clips = LoadPunchVoiceClips();

            foreach (CharacterDefinition character in Characters)
            {
                string prefabPath = PrefabPath(character);
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

                try
                {
                    PunchAnimationEventRelay relay = root.GetComponentInChildren<PunchAnimationEventRelay>(true);
                    if (relay == null)
                        throw new InvalidOperationException($"角色缺少 PunchAnimationEventRelay: {prefabPath}");

                    bool alreadyConfigured = relay.PunchVoiceSource != null
                        && relay.PunchVoiceClips != null
                        && relay.PunchVoiceClips.SequenceEqual(clips);

                    if (alreadyConfigured) continue;

                    ConfigurePunchVoice(relay, clips);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"BRAWL_PUNCH_VOICE: assigned to {prefabPath}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
        }

        static AudioClip[] LoadPunchVoiceClips()
        {
            AudioClip punchSwing = AssetDatabase.LoadAssetAtPath<AudioClip>(PunchSwingHitPath);
            if (punchSwing == null)
                throw new InvalidOperationException("挥拳音效尚未被 Unity 导入，请执行 Assets/Refresh 后重试。");

            return new[] { punchSwing };
        }

        static void ConfigurePunchVoice(PunchAnimationEventRelay relay, AudioClip[] clips)
        {
            AudioSource source = relay.GetComponent<AudioSource>();
            if (source == null) source = relay.gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.volume = 0.9f;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = 1f;
            source.maxDistance = 14f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            relay.ConfigurePunchVoice(source, clips);
            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(relay);
        }

        static void AssignPunchVoiceAudioToLoadedSceneObjects()
        {
            AudioClip[] clips = LoadPunchVoiceClips();

            foreach (PunchAnimationEventRelay relay in Resources.FindObjectsOfTypeAll<PunchAnimationEventRelay>())
            {
                if (relay == null || EditorUtility.IsPersistent(relay) || !relay.gameObject.scene.IsValid())
                    continue;

                bool alreadyConfigured = relay.PunchVoiceSource != null
                    && relay.PunchVoiceClips != null
                    && relay.PunchVoiceClips.SequenceEqual(clips);
                if (alreadyConfigured) continue;

                ConfigurePunchVoice(relay, clips);
                EditorSceneManager.MarkSceneDirty(relay.gameObject.scene);
                Debug.Log($"BRAWL_PUNCH_VOICE: assigned to loaded scene object {relay.gameObject.name}");
            }
        }

        static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Brawl/Prefabs"))
                AssetDatabase.CreateFolder("Assets/Brawl", "Prefabs");
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/Brawl/Prefabs", "Characters");
        }

        static string PrefabPath(CharacterDefinition character) => $"{OutputFolder}/{character.PrefabName}.prefab";

        readonly struct CharacterDefinition
        {
            public readonly string PrefabName;
            public readonly string ModelPath;
            public string ModelName => System.IO.Path.GetFileNameWithoutExtension(ModelPath);

            public CharacterDefinition(string prefabName, string modelPath)
            {
                PrefabName = prefabName;
                ModelPath = modelPath;
            }
        }
    }
}
