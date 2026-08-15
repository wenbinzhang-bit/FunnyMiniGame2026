using UnityEngine;

namespace Brawl
{
    [CreateAssetMenu(fileName = "PlayerModels", menuName = "Brawl/Player Models")]
    public class PlayerModels : ScriptableObject
    {
        [Tooltip("第 1 个玩家用 Element 0,第 2 个用 Element 1。某格没拖预制体则用前一个。")]
        public GameObject[] prefabs;

        public GameObject GetPrefab(int playerIndex)
        {
            if (prefabs == null || prefabs.Length == 0 || playerIndex < 0)
                return null;

            int start = Mathf.Min(playerIndex, prefabs.Length - 1);
            for (int i = start; i >= 0; i--)
            {
                if (prefabs[i] != null)
                    return prefabs[i];
            }

            return null;
        }
    }
}
