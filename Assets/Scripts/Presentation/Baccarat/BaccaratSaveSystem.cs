using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Same shape and path-resolution trick as the other two games' save systems, its own
// file — baccaratsim_save.txt — so all three games' sessions stay isolated.
public static class BaccaratSaveSystem
{
    const string FileName = "baccaratsim_save.txt";
    const int MaxSavedRecords = 500;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    public static void Save(Bankroll bankroll, int nextRoundIndex, List<BaccaratRoundRecord> records)
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
                lines.Add(string.Join(",", r.RoundIndex, r.PlayerPoint, r.BankerPoint, (int)r.Outcome, r.TotalStaked, r.TotalReturned, r.BalanceAfter));
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"BaccaratSaveSystem: failed to save to '{FilePath}' — {e.Message}");
        }
    }

    public static bool TryLoad(out long balance, out long startingBalance, out long totalFunded,
        out int nextRoundIndex, out List<BaccaratRoundRecord> records)
    {
        balance = startingBalance = totalFunded = 0;
        nextRoundIndex = 0;
        records = new List<BaccaratRoundRecord>();

        if (!File.Exists(FilePath)) return false;

        try
        {
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length == 0) return false;

            var header = lines[0].Split(',');
            balance = long.Parse(header[0], CultureInfo.InvariantCulture);
            startingBalance = long.Parse(header[1], CultureInfo.InvariantCulture);
            totalFunded = long.Parse(header[2], CultureInfo.InvariantCulture);
            nextRoundIndex = int.Parse(header[3], CultureInfo.InvariantCulture);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = lines[i].Split(',');
                records.Add(new BaccaratRoundRecord(
                    int.Parse(f[0], CultureInfo.InvariantCulture),
                    int.Parse(f[1], CultureInfo.InvariantCulture),
                    int.Parse(f[2], CultureInfo.InvariantCulture),
                    (BaccaratOutcome)int.Parse(f[3], CultureInfo.InvariantCulture),
                    long.Parse(f[4], CultureInfo.InvariantCulture),
                    long.Parse(f[5], CultureInfo.InvariantCulture),
                    long.Parse(f[6], CultureInfo.InvariantCulture)));
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"BaccaratSaveSystem: failed to load '{FilePath}' — {e.Message}");
            return false;
        }
    }
}
