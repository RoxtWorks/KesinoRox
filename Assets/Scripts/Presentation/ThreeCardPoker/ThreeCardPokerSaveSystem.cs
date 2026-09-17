using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class ThreeCardPokerSaveSystem
{
    const string FileName = "threecardpokersim_save.txt";
    const int MaxSavedRecords = 500;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    public static void Save(Bankroll bankroll, int nextRoundIndex, List<ThreeCardPokerRoundRecord> records)
    {
        try
        {
            var lines = new List<string>
            {
                string.Join(",", bankroll.Balance, bankroll.StartingBalance, bankroll.TotalFunded, nextRoundIndex)
            };
            int start = Mathf.Max(0, records.Count - MaxSavedRecords);
            for (int i = start; i < records.Count; i++)
            {
                var r = records[i];
                lines.Add(string.Join(",", r.RoundIndex, (int)r.PlayerRank, (int)r.DealerRank,
                    (int)r.Outcome, r.DealerQualified ? 1 : 0,
                    r.TotalStaked, r.TotalReturned, r.BalanceAfter));
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ThreeCardPokerSaveSystem: save failed — {e.Message}");
        }
    }

    public static bool TryLoad(out long balance, out long startingBalance, out long totalFunded,
        out int nextRoundIndex, out List<ThreeCardPokerRoundRecord> records)
    {
        balance = startingBalance = totalFunded = 0;
        nextRoundIndex = 0;
        records = new List<ThreeCardPokerRoundRecord>();

        if (!File.Exists(FilePath)) return false;

        try
        {
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length == 0) return false;

            var h = lines[0].Split(',');
            balance = long.Parse(h[0], CultureInfo.InvariantCulture);
            startingBalance = long.Parse(h[1], CultureInfo.InvariantCulture);
            totalFunded = long.Parse(h[2], CultureInfo.InvariantCulture);
            nextRoundIndex = int.Parse(h[3], CultureInfo.InvariantCulture);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = lines[i].Split(',');
                records.Add(new ThreeCardPokerRoundRecord(
                    int.Parse(f[0], CultureInfo.InvariantCulture),
                    (ThreeCardPokerRank)int.Parse(f[1], CultureInfo.InvariantCulture),
                    (ThreeCardPokerRank)int.Parse(f[2], CultureInfo.InvariantCulture),
                    (ThreeCardPokerOutcome)int.Parse(f[3], CultureInfo.InvariantCulture),
                    f[4] == "1",
                    long.Parse(f[5], CultureInfo.InvariantCulture),
                    long.Parse(f[6], CultureInfo.InvariantCulture),
                    long.Parse(f[7], CultureInfo.InvariantCulture)));
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ThreeCardPokerSaveSystem: load failed — {e.Message}");
            return false;
        }
    }
}
