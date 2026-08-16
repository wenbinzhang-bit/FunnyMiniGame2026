namespace Brawl
{
    /// <summary>
    /// 单关绩效只按本关排名分配；综合 KPI 仅用于简历展示，不影响本关盖章与评语。
    /// </summary>
    public static class BrawlRoundResultRules
    {
        public enum Grade
        {
            S,
            AMinus,
            BPlus,
            C
        }

        public static Grade ResolveGrade(int orderedIndex, int participantCount)
        {
            participantCount = participantCount < 1 ? 1 : participantCount;
            orderedIndex = orderedIndex < 0 ? 0 : orderedIndex;

            if (participantCount == 1 || orderedIndex == 0)
                return Grade.S;
            if (orderedIndex >= participantCount - 1)
                return Grade.C;
            if (orderedIndex == 1)
                return Grade.AMinus;
            return Grade.BPlus;
        }

        public static string Comment(Grade grade)
        {
            switch (grade)
            {
                case Grade.S:
                    return "该员工值得重点培养";
                case Grade.AMinus:
                    return "该员工值得继续培养";
                case Grade.BPlus:
                    return "该员工比较勤奋";
                default:
                    return "建议启动PIP";
            }
        }

        public static string StampResource(Grade grade)
        {
            switch (grade)
            {
                case Grade.S:
                    return "UI/Results/StampS";
                case Grade.AMinus:
                    return "UI/Results/StampAMinus";
                case Grade.BPlus:
                    return "UI/Results/StampBPlus";
                default:
                    return "UI/Results/StampC";
            }
        }
    }
}
