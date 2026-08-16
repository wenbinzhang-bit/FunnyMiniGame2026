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
            [Tooltip("HUD 和结算页显示的角色名，空着则回退 Player 1-4")]
            public string displayName;
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

        public Sprite ResolveAvatar(IBrawlPlayer player)
        {
            return ResolveAvatarByIndex(ResolveCharacterIndex(player));
        }

        public string ResolveName(IBrawlPlayer player)
        {
            return ResolveNameByIndex(ResolveCharacterIndex(player));
        }

        public string ResolveNameByIndex(int index)
        {
            EnsureMigrated();
            if (index < 0 || characters == null || index >= characters.Length || characters[index] == null)
                return "";
            return characters[index].displayName != null ? characters[index].displayName.Trim() : "";
        }

        int ResolveCharacterIndex(IBrawlPlayer player)
        {
            EnsureMigrated();
            int index = player != null ? player.CharacterIndex : -1;
            if (index < 0)
                index = ResolveIndex(player != null ? player.Transform : null, -1);
            return index;
        }

        public Sprite ResolveAvatar(Transform actor, int fallbackIndex)
        {
            EnsureMigrated();
            if (actor != null)
            {
                var fan = actor.GetComponent<NetFAnnequinController>();
                if (fan != null && fan.CharacterIndex >= 0)
                    return ResolveAvatarByIndex(fan.CharacterIndex);
            }

            int index = ResolveIndex(actor, fallbackIndex);
            return ResolveAvatarByIndex(index);
        }

        public int IndexOfPrefab(GameObject prefab)
        {
            EnsureMigrated();
            if (prefab == null || characters == null)
                return -1;

            for (int i = 0; i < characters.Length; i++)
            {
                GameObject entry = characters[i] != null ? characters[i].prefab : null;
                if (entry == prefab || (entry != null && entry.name == prefab.name))
                    return i;
            }

            return -1;
        }

        public int ResolveIndex(Transform actor, int fallbackIndex)
        {
            EnsureMigrated();
            if (characters == null || characters.Length == 0)
                return -1;

            int best = -1;
            int bestLength = -1;
            string actorName = actor != null ? actor.name : string.Empty;
            if (!string.IsNullOrEmpty(actorName))
            {
                for (int i = 0; i < characters.Length; i++)
                {
                    GameObject prefab = characters[i] != null ? characters[i].prefab : null;
                    if (prefab == null || string.IsNullOrEmpty(prefab.name))
                        continue;
                    if (actorName.IndexOf(prefab.name, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (prefab.name.Length <= bestLength)
                        continue;
                    bestLength = prefab.name.Length;
                    best = i;
                }
            }

            if (best >= 0)
                return best;
            if (fallbackIndex >= 0 && fallbackIndex < characters.Length)
                return fallbackIndex;
            return -1;
        }

        Sprite ResolveAvatarByIndex(int index)
        {
            if (index < 0 || characters == null || index >= characters.Length || characters[index] == null)
                return null;
            return characters[index].avatar;
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
        void OnValidate()
        {
            if (UnityEditor.AssetDatabase.GetAssetPath(this) != EditorAssetPath)
                return;
            UnityEditor.EditorApplication.delayCall -= EditorSyncToResourcesCatalog;
            UnityEditor.EditorApplication.delayCall += EditorSyncToResourcesCatalog;
        }

        static void EditorSyncToResourcesCatalog()
        {
            BrawlCharacterCatalog source = UnityEditor.AssetDatabase.LoadAssetAtPath<BrawlCharacterCatalog>(EditorAssetPath);
            BrawlCharacterCatalog dest = UnityEditor.AssetDatabase.LoadAssetAtPath<BrawlCharacterCatalog>(ResourcesAssetPath);
            if (source == null || dest == null || source == dest)
                return;
            dest.CopyEntriesFrom(source);
            UnityEditor.EditorUtility.SetDirty(dest);
        }

        public void CopyEntriesFrom(BrawlCharacterCatalog source)
        {
            if (source == null || source == this)
                return;

            IReadOnlyList<CharacterEntry> sourceEntries = source.Characters;
            characters = new CharacterEntry[sourceEntries.Count];
            characterPrefabs = new GameObject[sourceEntries.Count];
            for (int i = 0; i < sourceEntries.Count; i++)
            {
                CharacterEntry entry = sourceEntries[i];
                characters[i] = new CharacterEntry
                {
                    prefab = entry != null ? entry.prefab : null,
                    displayName = entry != null ? entry.displayName : "",
                    avatar = entry != null ? entry.avatar : null
                };
                characterPrefabs[i] = characters[i].prefab;
            }
        }

        public void EditorSetPrefabs(GameObject[] prefabs)
        {
            prefabs = prefabs ?? Array.Empty<GameObject>();
            var next = new CharacterEntry[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                next[i] = new CharacterEntry
                {
                    prefab = prefabs[i],
                    displayName = FindExistingName(prefabs[i], i),
                    avatar = FindExistingAvatar(prefabs[i], i)
                };
            }

            characters = next;
            characterPrefabs = prefabs;
        }

        string FindExistingName(GameObject prefab, int index)
        {
            if (characters == null)
                return "";

            if (prefab != null)
            {
                for (int i = 0; i < characters.Length; i++)
                {
                    CharacterEntry entry = characters[i];
                    if (entry == null || entry.prefab == null || string.IsNullOrEmpty(entry.displayName))
                        continue;
                    if (entry.prefab == prefab || entry.prefab.name == prefab.name)
                        return entry.displayName;
                }
            }

            if (index >= 0 && index < characters.Length && characters[index] != null)
                return characters[index].displayName ?? "";
            return "";
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
