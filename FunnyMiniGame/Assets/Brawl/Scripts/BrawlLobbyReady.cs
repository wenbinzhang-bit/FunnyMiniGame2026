namespace Brawl
{
    /// <summary>
    /// 只处理 Launcher 大厅准备。关内空气墙等待不要调用这里。
    /// </summary>
    public static class BrawlLobbyReady
    {
        public static void ApplyForLobby(NetFAnnequinController fan, bool isBot)
        {
            if (fan == null) return;
            fan.LobbyReady = isBot;
        }

        public static void KeepBotReady(NetFAnnequinController fan, bool isBot)
        {
            if (isBot && fan != null)
                fan.LobbyReady = true;
        }

        public static void Clear(NetFAnnequinController fan)
        {
            if (fan != null)
                fan.LobbyReady = false;
        }

        public struct Tally
        {
            public int Ready;
            public int Total;
            public int Humans;

            public void Add(bool valid, bool isBot, bool ready)
            {
                if (!valid) return;
                Total++;
                if (!isBot)
                    Humans++;
                if (isBot || ready)
                    Ready++;
            }

            public bool CanEnterFirstLevel(int minHumans)
            {
                return Humans >= minHumans && Total > 0 && Ready >= Total;
            }

            public string Line => $"已准备 {Ready}/{Total}";
        }
    }
}
