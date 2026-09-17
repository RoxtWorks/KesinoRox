using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Same shape and path-resolution trick as the other games' save systems, its
// own file — crapssim_save.txt — so every game's session stays isolated.
// Lines: header, then one per shooter-turn summary, then "R,"-prefixed per-roll History rows.
public static class CrapsSaveSystem
{
    const string FileName = "crapssim_save.txt";
    const int MaxSavedRecords = 500;
    public const int MaxSavedRolls = 30;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    // chipsOnTable is counted into the saved balance so a crash mid-turn refunds the felt instead of losing it.
    public static void Save(Bankroll bankroll, int nextRoundIndex, List<CrapsRoundRecord> records,
        List<CrapsRoundRecord> rollRecords, long chipsOnTable = 0)
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
            Debug.LogWarning($"CrapsSaveSystem: failed to save to '{FilePath}' — {e.Message}");
        }
    }

    static string Line(CrapsRoundRecord r) =>
        string.Join(",", r.RoundIndex, r.FinalPoint, r.RollCount, r.TotalStaked, r.TotalReturned, r.BalanceAfter, r.RollTotal);

    public static bool TryLoad(out long balance, out long startingBalance, out long totalFunded,
        out int nextRoundIndex, out List<CrapsRoundRecord> records, out List<CrapsRoundRecord> rollRecords)
    {
        balance = startingBalance = totalFunded = 0;
        nextRoundIndex = 0;
        records = new List<CrapsRoundRecord>();
        rollRecords = new List<CrapsRoundRecord>();

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
                bool isRoll = f[0] == "R";
                int o = isRoll ? 1 : 0;
                var record = new CrapsRoundRecord(
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
            Debug.LogWarning($"CrapsSaveSystem: failed to load '{FilePath}' — {e.Message}");
            return false;
        }
    }
}
