using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 挂在各小关场景里，给开局规则页提供本关标题和玩法说明。
    /// </summary>
    public sealed class BrawlLevelInfo : MonoBehaviour
    {
        public string Title = "本局规则";

        [TextArea(4, 12)]
        public string Rules =
            "抱住笔记本电脑并坚持不放，就能持续得分。\n" +
            "被拳头打中会丢掉电脑，自己也会被打飞。\n" +
            "先到 99 分，或时间结束时按分数排名。\n" +
            "掉出场地会送回出生点，不会淘汰。\n\n" +
            "WASD 移动　　空格 跳跃　　Shift 加速\n" +
            "左键 出拳　　按住右键 抱起电脑　　松开右键 放下";

        public static BrawlLevelInfo FindInScene()
        {
            return FindObjectOfType<BrawlLevelInfo>();
        }
    }
}
