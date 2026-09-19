using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Lines: header, then one per shooter-turn summary, then "R,"-prefixed per-roll History rows.
public static class NormalCrapsSaveSystem
{
    const string FileName = "normalcrapssim_save.txt";
    const int MaxSavedRecords = 500;
    public const int MaxSavedRolls = 30;

    static string FilePath => SavePaths.For(FileName);

    // chipsOnTable is counted into the saved balance so a crash mid-turn refunds the felt instead of losing it.
    public static void Save(Bankroll bankroll, int nextRoundIndex, List<NormalCrapsRoundRecord> records,
        List<NormalCrapsRoundRecord> rollRecords, long chipsOnTable = 0)
    {
        try
        {
            var lines = new List<string>
            {
                string.Join(",", bankroll.Balance + chipsOnTable, bankroll.StartingBalance, bankroll.TotalFunded, nextRoundIndex)
            };
            int start = Mathf.Max(0, records.Count - MaxSavedRecords);
            for (int i = start; i < records.Count; i++)
                lines.Add(Line(records[i]));
            int rollStart = Mathf.Max(0, rollRecords.Count - MaxSavedRolls);
            for (int i = rollStart; i < rollRecords.Count; i++)
                lines.Add("R," + Line(rollRecords[i]));
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"NormalCrapsSaveSystem: save failed — {e.Message}");
        }
    }

    static string Line(NormalCrapsRoundRecord r) =>
        string.Join(",", r.RoundIndex, r.FinalPoint, r.RollCount, r.TotalStaked, r.TotalReturned, r.BalanceAfter, r.RollTotal);

    public static bool TryLoad(out long balance, out long startingBalance, out long totalFunded,
        out int nextRoundIndex, out List<NormalCrapsRoundRecord> records, out List<NormalCrapsRoundRecord> rollRecords)
    {
        balance = startingBalance = totalFunded = 0;
        nextRoundIndex = 0;
        records = new List<NormalCrapsRoundRecord>();
        rollRecords = new List<NormalCrapsRoundRecord>();

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
                bool isRoll = f[0] == "R";
                int o = isRoll ? 1 : 0;
                var record = new NormalCrapsRoundRecord(
                    int.Parse(f[o], CultureInfo.InvariantCulture),
                    int.Parse(f[o + 1], CultureInfo.InvariantCulture),
                    int.Parse(f[o + 2], CultureInfo.InvariantCulture),
                    long.Parse(f[o + 3], CultureInfo.InvariantCulture),
                    long.Parse(f[o + 4], CultureInfo.InvariantCulture),
                    long.Parse(f[o + 5], CultureInfo.InvariantCulture),
                    // Older save files (before the Roll column existed) won't have a 7th field
                    f.Length > o + 6 ? int.Parse(f[o + 6], CultureInfo.InvariantCulture) : 0);
                (isRoll ? rollRecords : records).Add(record);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"NormalCrapsSaveSystem: load failed — {e.Message}");
            SavePaths.KeepBadCopy(FilePath); // keep the unreadable file rather than let the next save overwrite it
            return false;
        }
    }
}
