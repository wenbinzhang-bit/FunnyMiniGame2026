using System;
using System.Collections.Generic;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 可用主角 Prefab 的统一入口。GetRandomOrder 会返回不重复的随机顺序，
    /// 服务端或本地玩家出生器可以按玩家序号依次取用。
    /// </summary>
    [CreateAssetMenu(menuName = "Brawl/Character Catalog", fileName = "BrawlCharacterCatalog")]
    public sealed class BrawlCharacterCatalog : ScriptableObject
    {
        [SerializeField] GameObject[] characterPrefabs = Array.Empty<GameObject>();

        public IReadOnlyList<GameObject> CharacterPrefabs => characterPrefabs;

        public GameObject[] GetRandomOrder(int seed)
        {
            var result = (GameObject[])characterPrefabs.Clone();
            var random = new System.Random(seed);

            for (int i = result.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (result[i], result[other]) = (result[other], result[i]);
            }

            return result;
        }

#if UNITY_EDITOR
        public void EditorSetPrefabs(GameObject[] prefabs)
        {
            characterPrefabs = prefabs ?? Array.Empty<GameObject>();
        }
#endif
    }
}
