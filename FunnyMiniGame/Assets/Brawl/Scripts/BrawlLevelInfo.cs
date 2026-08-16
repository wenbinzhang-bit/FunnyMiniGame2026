using UnityEngine;
using UnityEngine.SceneManagement;

namespace Brawl
{
    public enum BrawlPlayMode : byte
    {
        HoldKpi = 0,
        PassTheBuck = 1
    }

    /// <summary>
    /// 挂在各小关场景里，声明本关玩法模式，并给开局规则页提供标题和说明。
    /// </summary>
    public sealed class BrawlLevelInfo : MonoBehaviour
    {
        public const string HoldKpiTitle = "抢电脑";
        public const string PassTheBuckTitle = "甩锅";

        public const string HoldKpiRules =
            "抱住笔记本电脑并坚持不放，就能持续得分。\n" +
            "被拳头打中会丢掉电脑，自己也会被打飞。\n" +
            "先到 99 分，或时间结束时按分数排名。\n" +
            "掉出场地会送回出生点，不会淘汰。\n\n" +
            "WASD 移动　　空格 跳跃　　Shift 加速\n" +
            "左键 出拳　　按住右键 抱起电脑　　松开右键 放下\n" +
            "Esc 释放鼠标　　Alt 重新捕获鼠标\n\n" +
            "开局有空气墙，倒计时结束后撤墙，正式开打。";

        public const string PassTheBuckRules =
            "开局随机一人头上扣一口锅。\n" +
            "背锅的人用右键对准 3 米内的其他玩家，把锅甩给他。\n" +
            "锅会在随机 5~30 秒后爆炸，背着的人会被炸飞淘汰。\n" +
            "界面不显示倒计时，图标闪得越快越危险。\n" +
            "淘汰到只剩一人就结算。\n" +
            "按存活轮次计分，最后一人拿满分。\n\n" +
            "WASD 移动　　空格 跳跃　　Shift 加速\n" +
            "左键 出拳　　右键 对准玩家甩锅\n" +
            "Esc 释放鼠标　　Alt 重新捕获鼠标";

        public BrawlPlayMode PlayMode = BrawlPlayMode.HoldKpi;

        public string Title = HoldKpiTitle;

        [TextArea(4, 12)]
        public string Rules = HoldKpiRules;

        [Tooltip("甩锅模式：时间结束时，当前持有者扣除的分数")]
        [Min(0)] public int BuckPenalty = 15;

        [Tooltip("甩锅模式：被砸中后无法行动和回甩的秒数")]
        [Min(0.2f)] public float CatchStunSeconds = 1f;

        [Tooltip("甩锅模式：把电脑砸出去的速度")]
        [Min(1f)] public float ThrowSpeed = 14f;

        [Tooltip("仅甩锅关：最后这段时间停止抱分，只留终局背锅。抢电脑关不读这个值")]
        [Min(1f)] public float BuckDumpSeconds = 30f;

        public static BrawlLevelInfo FindInScene()
        {
            return FindObjectOfType<BrawlLevelInfo>();
        }

        public static BrawlLevelInfo EnsureInLevel()
        {
            BrawlLevelInfo info = FindInScene();
            if (info != null) return info;

            string sceneName = SceneManager.GetActiveScene().name;
            if (!BrawlLevelCatalog.IsLevel(sceneName))
                return null;

            var root = new GameObject("LevelInfo");
            info = root.AddComponent<BrawlLevelInfo>();
            info.PlayMode = BrawlLevelCatalog.DefaultPlayMode(sceneName);
            info.ApplyDefaultsForMode();
            return info;
        }

        public void ApplyDefaultsForMode()
        {
            if (PlayMode == BrawlPlayMode.PassTheBuck)
            {
                if (string.IsNullOrEmpty(Title) || Title == "本局规则" || Title == HoldKpiTitle)
                    Title = PassTheBuckTitle;
                if (string.IsNullOrEmpty(Rules) || Rules == HoldKpiRules || Rules.Contains("60")
                    || !(Rules.Contains("5~30") || Rules.Contains("5 到 30")))
                    Rules = PassTheBuckRules;
                return;
            }

            if (string.IsNullOrEmpty(Title) || Title == "本局规则")
                Title = HoldKpiTitle;
            if (string.IsNullOrEmpty(Rules))
                Rules = HoldKpiRules;
        }
    }
}
