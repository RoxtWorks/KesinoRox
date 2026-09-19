using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Lines: header, then one per hand (last MaxSavedHands only):
// index, playerRank, playerHigh, dealerRank, dealerHigh, outcome, dealerQualified, folded, staked, returned, balance.
public static class ThreeCardPokerSaveSystem
{
    const string FileName = "threecardpokersim_save.txt";
    public const int MaxSavedHands = 30;
    const int HandFields = 11;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    // chipsOnTable is counted into the saved balance so a crash with chips on the felt refunds them instead of losing them.
    public static void Save(Bankroll bankroll, int nextRoundIndex, List<ThreeCardPokerRoundRecord> records, long chipsOnTable = 0)
    {
        try
        {
            var lines = new List<string>
            {
                string.Join(",", bankroll.Balance + chipsOnTable, bankroll.StartingBalance, bankroll.TotalFunded, nextRoundIndex)
            };
            int start = Mathf.Max(0, records.Count - MaxSavedHands);
            for (int i = start; i < records.Count; i++)
            {
                var r = records[i];
                lines.Add(string.Join(",", r.RoundIndex, (int)r.PlayerRank, r.PlayerHigh, (int)r.DealerRank, r.DealerHigh,
                    (int)r.Outcome, r.DealerQualified ? 1 : 0, r.Folded ? 1 : 0, r.TotalStaked, r.TotalReturned, r.BalanceAfter));
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
                var f = lines[i].Split(',');
                if (f.Length != HandFields) continue; // hands saved by the old version are skipped; the balance still loads
                records.Add(new ThreeCardPokerRoundRecord(
                    int.Parse(f[0], CultureInfo.InvariantCulture),
                    (ThreeCardPokerRank)int.Parse(f[1], CultureInfo.InvariantCulture),
                    int.Parse(f[2], CultureInfo.InvariantCulture),
                    (ThreeCardPokerRank)int.Parse(f[3], CultureInfo.InvariantCulture),
                    int.Parse(f[4], CultureInfo.InvariantCulture),
                    (ThreeCardPokerOutcome)int.Parse(f[5], CultureInfo.InvariantCulture),
                    f[6] == "1", f[7] == "1",
                    long.Parse(f[8], CultureInfo.InvariantCulture),
                    long.Parse(f[9], CultureInfo.InvariantCulture),
                    long.Parse(f[10], CultureInfo.InvariantCulture)));
            }
            if (records.Count > MaxSavedHands) records.RemoveRange(0, records.Count - MaxSavedHands);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ThreeCardPokerSaveSystem: load failed — {e.Message}");
            return false;
        }
    }
}
