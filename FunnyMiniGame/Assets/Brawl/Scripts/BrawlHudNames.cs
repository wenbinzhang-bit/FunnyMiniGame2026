using System.Collections.Generic;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 对局显示名：按 netId 排序后的座位号 Player 1-4，不暴露 Mirror 的内部 netId。
    /// </summary>
    public static class BrawlHudNames
    {
        public static string Label(uint netId)
        {
            return Format(DisplayNumber(netId, CollectNetIds()));
        }

        public static string Label(uint netId, IEnumerable<IBrawlPlayer> players)
        {
            var ids = new List<uint>(4);
            if (players != null)
            {
                foreach (IBrawlPlayer player in players)
                {
                    if (player != null && player.NetId != 0u && !ids.Contains(player.NetId))
                        ids.Add(player.NetId);
                }
            }

            ids.Sort();
            return Format(DisplayNumber(netId, ids));
        }

        public static string LocalLabel(uint netId)
        {
            return "自己  " + Label(netId);
        }

        static string Format(int number)
        {
            return number > 0 ? $"Player {number}" : "Player ?";
        }

        static int DisplayNumber(uint netId, List<uint> sortedIds)
        {
            if (netId == 0u || sortedIds == null) return 0;
            int index = sortedIds.IndexOf(netId);
            return index >= 0 ? index + 1 : 0;
        }

        static List<uint> CollectNetIds()
        {
            var ids = new List<uint>(4);
            foreach (NetFAnnequinController player in Object.FindObjectsOfType<NetFAnnequinController>())
            {
                if (player != null && player.netId != 0u && !ids.Contains(player.netId))
                    ids.Add(player.netId);
            }

            foreach (NetPlayerMotor player in Object.FindObjectsOfType<NetPlayerMotor>())
            {
                if (player != null && player.netId != 0u && !ids.Contains(player.netId))
                    ids.Add(player.netId);
            }

            ids.Sort();
            return ids;
        }
    }
}
