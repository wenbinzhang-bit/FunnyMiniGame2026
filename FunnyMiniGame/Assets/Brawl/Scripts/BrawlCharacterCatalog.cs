using System;
using System.Collections.Generic;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 角色表：Prefab 和 Slot 头像都在这里配。
    /// HUD 按玩家物体名是否包含 Prefab 名来取头像，不再写死 FAnnequinV2_New1 这类对应。
    /// </summary>
    [CreateAssetMenu(menuName = "Brawl/Character Catalog", fileName = "BrawlCharacterCatalog")]
    public sealed class BrawlCharacterCatalog : ScriptableObject
    {
        public const string EditorAssetPath = "Assets/Brawl/Prefabs/Characters/BrawlCharacterCatalog.asset";
        public const string ResourcesAssetPath = "Assets/Brawl/Resources/BrawlCharacterCatalog.asset";

        [Serializable]
        public sealed class CharacterEntry
        {
            [Tooltip("角色 Prefab，进房生成和 HUD 匹配都用它")]
            public GameObject prefab;
            [Tooltip("Slot 头像，空着则该角色不显示专用头像")]
            public Sprite avatar;
        }

        [SerializeField] CharacterEntry[] characters = Array.Empty<CharacterEntry>();
        [SerializeField, HideInInspector] GameObject[] characterPrefabs = Array.Empty<GameObject>();

        static BrawlCharacterCatalog cached;

        public IReadOnlyList<CharacterEntry> Characters => characters ?? Array.Empty<CharacterEntry>();

        public IReadOnlyList<GameObject> CharacterPrefabs
        {
            get
            {
                CharacterEntry[] entries = characters;
                if (entries == null || entries.Length == 0)
                    return characterPrefabs ?? Array.Empty<GameObject>();

                var prefabs = new GameObject[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                    prefabs[i] = entries[i] != null ? entries[i].prefab : null;
                return prefabs;
            }
        }

        public static BrawlCharacterCatalog Load()
        {
            if (cached != null)
                return cached;

#if UNITY_EDITOR
            cached = UnityEditor.AssetDatabase.LoadAssetAtPath<BrawlCharacterCatalog>(EditorAssetPath);
            if (cached != null)
                return cached;
#endif
            cached = Resources.Load<BrawlCharacterCatalog>("BrawlCharacterCatalog");
            return cached;
        }

        public GameObject[] GetRandomOrder(int seed)
        {
            GameObject[] result = CopyPrefabs();
            var random = new System.Random(seed);
            for (int i = result.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (result[i], result[other]) = (result[other], result[i]);
            }

            return result;
        }

        public Sprite ResolveAvatar(Transform actor, int fallbackIndex)
        {
            EnsureMigrated();
            int index = ResolveIndex(actor, fallbackIndex);
            if (index < 0 || characters == null || index >= characters.Length || characters[index] == null)
                return null;
            return characters[index].avatar;
        }

        public int ResolveIndex(Transform actor, int fallbackIndex)
        {
            EnsureMigrated();
            if (characters == null || characters.Length == 0)
                return -1;

            string actorName = actor != null ? actor.name : string.Empty;
            if (!string.IsNullOrEmpty(actorName))
            {
                for (int i = 0; i < characters.Length; i++)
                {
                    GameObject prefab = characters[i] != null ? characters[i].prefab : null;
                    if (prefab != null
                        && !string.IsNullOrEmpty(prefab.name)
                        && actorName.IndexOf(prefab.name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
            }

            if (fallbackIndex >= 0 && fallbackIndex < characters.Length)
                return fallbackIndex;
            return -1;
        }

        void OnEnable()
        {
            EnsureMigrated();
        }

        void EnsureMigrated()
        {
            if (characters != null && characters.Length > 0)
                return;
            if (characterPrefabs == null || characterPrefabs.Length == 0)
                return;

            characters = new CharacterEntry[characterPrefabs.Length];
            for (int i = 0; i < characterPrefabs.Length; i++)
                characters[i] = new CharacterEntry { prefab = characterPrefabs[i] };
        }

        GameObject[] CopyPrefabs()
        {
            IReadOnlyList<GameObject> source = CharacterPrefabs;
            var result = new GameObject[source.Count];
            for (int i = 0; i < source.Count; i++)
                result[i] = source[i];
            return result;
        }

#if UNITY_EDITOR
        public void EditorSetPrefabs(GameObject[] prefabs)
        {
            prefabs = prefabs ?? Array.Empty<GameObject>();
            var next = new CharacterEntry[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                next[i] = new CharacterEntry
                {
                    prefab = prefabs[i],
                    avatar = FindExistingAvatar(prefabs[i], i)
                };
            }

            characters = next;
            characterPrefabs = prefabs;
        }

        Sprite FindExistingAvatar(GameObject prefab, int index)
        {
            if (characters == null)
                return null;

            if (prefab != null)
            {
                for (int i = 0; i < characters.Length; i++)
                {
                    CharacterEntry entry = characters[i];
                    if (entry == null || entry.avatar == null || entry.prefab == null)
                        continue;
                    if (entry.prefab == prefab || entry.prefab.name == prefab.name)
                        return entry.avatar;
                }
            }

            if (index >= 0 && index < characters.Length && characters[index] != null)
                return characters[index].avatar;
            return null;
        }
#endif
    }
}
