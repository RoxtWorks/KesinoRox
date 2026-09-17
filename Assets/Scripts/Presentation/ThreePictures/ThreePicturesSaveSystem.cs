using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class ThreePicturesSaveSystem
{
    const string FileName = "threepicturessim_save.txt";
    const int MaxSavedRecords = 500;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    public static void Save(Bankroll bankroll, int nextRoundIndex, List<ThreePicturesRoundRecord> records)
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
                lines.Add(string.Join(",", r.RoundIndex, r.PlayerPoint, r.DealerPoint,
                    r.PlayerRoyal ? 1 : 0, r.DealerRoyal ? 1 : 0,
                    (int)r.Outcome, r.TotalStaked, r.TotalReturned, r.BalanceAfter));
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ThreePicturesSaveSystem: save failed — {e.Message}");
        }
    }

    public static bool TryLoad(out long balance, out long startingBalance, out long totalFunded,
        out int nextRoundIndex, out List<ThreePicturesRoundRecord> records)
    {
        balance = startingBalance = totalFunded = 0;
        nextRoundIndex = 0;
        records = new List<ThreePicturesRoundRecord>();

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
                records.Add(new ThreePicturesRoundRecord(
                    int.Parse(f[0], CultureInfo.InvariantCulture),
                    int.Parse(f[1], CultureInfo.InvariantCulture),
                    int.Parse(f[2], CultureInfo.InvariantCulture),
                    f[3] == "1", f[4] == "1",
                    (ThreePicturesOutcome)int.Parse(f[5], CultureInfo.InvariantCulture),
                    long.Parse(f[6], CultureInfo.InvariantCulture),
                    long.Parse(f[7], CultureInfo.InvariantCulture),
                    long.Parse(f[8], CultureInfo.InvariantCulture)));
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ThreePicturesSaveSystem: load failed — {e.Message}");
            return false;
        }
    }
}
