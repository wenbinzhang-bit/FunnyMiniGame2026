using System.Collections.Generic;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 对局显示名：优先用 BrawlCharacterCatalog 配置的角色名，否则回退 Player 1-4。
    /// </summary>
    public static class BrawlHudNames
    {
        public static string Label(uint netId)
        {
            return Label(netId, CollectPlayers());
        }

        static List<IBrawlPlayer> CollectPlayers()
        {
            var players = new List<IBrawlPlayer>(4);
            foreach (NetFAnnequinController player in Object.FindObjectsOfType<NetFAnnequinController>())
            {
                if (player != null && !players.Contains(player))
                    players.Add(player);
            }

            foreach (NetPlayerMotor player in Object.FindObjectsOfType<NetPlayerMotor>())
            {
                if (player != null && !players.Contains(player))
                    players.Add(player);
            }

            return players;
        }

        public static string Label(uint netId, IEnumerable<IBrawlPlayer> players)
        {
            IBrawlPlayer match = FindPlayer(netId, players);
            string configured = BrawlCharacterCatalog.Load()?.ResolveName(match);
            if (!string.IsNullOrEmpty(configured))
                return configured;

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

        static IBrawlPlayer FindPlayer(uint netId, IEnumerable<IBrawlPlayer> players)
        {
            if (netId == 0u || players == null)
                return null;
            foreach (IBrawlPlayer player in players)
            {
                if (player != null && player.NetId == netId)
                    return player;
            }

            return null;
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
    }
}
