using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Lines: header, then one per roll (last MaxSavedRolls only).
public static class SicBoSaveSystem
{
    const string FileName = "sicbosim_save.txt";
    public const int MaxSavedRolls = 30;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    // chipsOnTable is counted into the saved balance so a crash with chips on the felt refunds them instead of losing them.
    public static void Save(Bankroll bankroll, int nextRoundIndex, List<SicBoRoundRecord> records, long chipsOnTable = 0)
    {
        try
        {
            var lines = new List<string>
            {
                string.Join(",", bankroll.Balance + chipsOnTable, bankroll.StartingBalance, bankroll.TotalFunded, nextRoundIndex)
            };
            int start = Mathf.Max(0, records.Count - MaxSavedRolls);
            for (int i = start; i < records.Count; i++)
            {
                var r = records[i];
                lines.Add(string.Join(",", r.RoundIndex, r.Die1, r.Die2, r.Die3, r.TotalStaked, r.TotalReturned, r.BalanceAfter));
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SicBoSaveSystem: save failed — {e.Message}");
        }
    }

    public static bool TryLoad(out long balance, out long startingBalance, out long totalFunded,
        out int nextRoundIndex, out List<SicBoRoundRecord> records)
    {
        balance = startingBalance = totalFunded = 0;
        nextRoundIndex = 0;
        records = new List<SicBoRoundRecord>();

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
                records.Add(new SicBoRoundRecord(
                    int.Parse(f[0], CultureInfo.InvariantCulture),
                    int.Parse(f[1], CultureInfo.InvariantCulture),
                    int.Parse(f[2], CultureInfo.InvariantCulture),
                    int.Parse(f[3], CultureInfo.InvariantCulture),
                    long.Parse(f[4], CultureInfo.InvariantCulture),
                    long.Parse(f[5], CultureInfo.InvariantCulture),
                    long.Parse(f[6], CultureInfo.InvariantCulture)));
            }
            // Older saves kept up to 500 rolls
            if (records.Count > MaxSavedRolls) records.RemoveRange(0, records.Count - MaxSavedRolls);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SicBoSaveSystem: load failed — {e.Message}");
            return false;
        }
    }
}
