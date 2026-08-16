using System.Collections.Generic;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// 整场对局的成绩实例。挂在 BrawlSession 上，切关不销毁。
    /// 当局分数和各关 KPI 都记在这里，不写在关卡场景脚本里。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrawlRunRecord : MonoBehaviour
    {
        public static BrawlRunRecord Current { get; private set; }

        public sealed class Seat
        {
            public int connectionId = -1;
            public int botIndex = -1;
            public string label = "";
            public int currentRoundScore;
            public readonly List<int> levelScores = new List<int>();
            public readonly List<string> levelNames = new List<string>();

            public int Total
            {
                get
                {
                    int sum = 0;
                    for (int i = 0; i < levelScores.Count; i++)
                        sum += levelScores[i];
                    return sum;
                }
            }
        }

        readonly List<Seat> seats = new List<Seat>();
        readonly List<string> playedLevels = new List<string>();

        public IReadOnlyList<Seat> Seats => seats;
        public IReadOnlyList<string> PlayedLevels => playedLevels;

        public static BrawlRunRecord Ensure(Transform host)
        {
            if (Current != null) return Current;
            if (host == null)
            {
                var go = new GameObject("RunRecord");
                DontDestroyOnLoad(go);
                Current = go.AddComponent<BrawlRunRecord>();
                return Current;
            }

            Current = host.GetComponent<BrawlRunRecord>();
            if (Current == null)
                Current = host.gameObject.AddComponent<BrawlRunRecord>();
            return Current;
        }

        void Awake()
        {
            if (Current != null && Current != this)
            {
                Destroy(this);
                return;
            }

            Current = this;
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        public void BeginNewRun()
        {
            seats.Clear();
            playedLevels.Clear();
        }

        public Seat EnsureSeat(int connectionId, int botIndex, string label)
        {
            Seat seat = FindSeat(connectionId, botIndex);
            if (seat == null)
            {
                seat = new Seat { connectionId = connectionId, botIndex = botIndex };
                seats.Add(seat);
            }

            if (!string.IsNullOrEmpty(label))
                seat.label = label;
            return seat;
        }

        public Seat FindSeat(int connectionId, int botIndex)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                Seat seat = seats[i];
                if (connectionId >= 0 && seat.connectionId == connectionId)
                    return seat;
                if (connectionId < 0 && botIndex >= 0 && seat.botIndex == botIndex)
                    return seat;
            }

            return null;
        }

        public void SetCurrentRoundScore(int connectionId, int botIndex, string label, int score)
        {
            EnsureSeat(connectionId, botIndex, label).currentRoundScore = Mathf.Max(0, score);
        }

        public void ResetCurrentRoundScores()
        {
            for (int i = 0; i < seats.Count; i++)
                seats[i].currentRoundScore = 0;
        }

        public void CommitLevel(string levelName)
        {
            string name = BrawlLevelCatalog.NormalizeName(levelName);
            if (string.IsNullOrEmpty(name))
                name = BrawlLevelCatalog.ActiveSceneName();
            playedLevels.Add(name);

            for (int i = 0; i < seats.Count; i++)
            {
                Seat seat = seats[i];
                seat.levelScores.Add(seat.currentRoundScore);
                seat.levelNames.Add(name);
            }
        }

        public string FormatBoard()
        {
            if (seats.Count == 0) return "还没有成绩";

            var ordered = new List<Seat>(seats);
            ordered.Sort((a, b) =>
            {
                int cmp = b.Total.CompareTo(a.Total);
                return cmp != 0 ? cmp : a.label.CompareTo(b.label);
            });

            var lines = new List<string>();
            int rank = 1;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0 && ordered[i].Total < ordered[i - 1].Total)
                    rank = i + 1;
                Seat seat = ordered[i];
                var parts = new List<string>();
                for (int s = 0; s < seat.levelScores.Count; s++)
                {
                    string levelName = s < seat.levelNames.Count
                        ? BrawlLevelCatalog.GetLevelTitle(seat.levelNames[s])
                        : $"第{s + 1}关";
                    parts.Add($"{levelName} {seat.levelScores[s]}分");
                }

                string detail = parts.Count == 0 ? "暂无" : string.Join("   ", parts);
                lines.Add($"第{rank}名    {seat.label}    {detail}    总分 {seat.Total}");
            }

            return string.Join("\n", lines);
        }
    }
}
