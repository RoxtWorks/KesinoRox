using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Lines: header, then one per round (last MaxSavedRounds only):
// index, dealerPoint, dealerRoyal, staked, returned, balance, then per box: stake, outcome, halfPay.
public static class ThreePicturesSaveSystem
{
    const string FileName = "threepicturessim_save.txt";
    public const int MaxSavedRounds = 30;
    const int RoundFields = 6 + 3 * ThreePicturesRound.MaxHands;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    // chipsOnTable is counted into the saved balance so a crash with chips on the felt refunds them instead of losing them.
    public static void Save(Bankroll bankroll, int nextRoundIndex, List<ThreePicturesRoundRecord> records, long chipsOnTable = 0)
    {
        try
        {
            var lines = new List<string>
            {
                string.Join(",", bankroll.Balance + chipsOnTable, bankroll.StartingBalance, bankroll.TotalFunded, nextRoundIndex)
            };
            int start = Mathf.Max(0, records.Count - MaxSavedRounds);
            for (int i = start; i < records.Count; i++)
            {
                var r = records[i];
                var fields = new List<string>
                {
                    r.RoundIndex.ToString(CultureInfo.InvariantCulture), r.DealerPoint.ToString(CultureInfo.InvariantCulture),
                    r.DealerRoyal ? "1" : "0", r.TotalStaked.ToString(CultureInfo.InvariantCulture),
                    r.TotalReturned.ToString(CultureInfo.InvariantCulture), r.BalanceAfter.ToString(CultureInfo.InvariantCulture)
                };
                foreach (var b in r.Boxes)
                {
                    fields.Add(b.Stake.ToString(CultureInfo.InvariantCulture));
                    fields.Add(((int)b.Outcome).ToString(CultureInfo.InvariantCulture));
                    fields.Add(b.HalfPay ? "1" : "0");
                }
                lines.Add(string.Join(",", fields));
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
                var f = lines[i].Split(',');
                if (f.Length != RoundFields) continue; // rounds from the old one-hand version are skipped; the balance still loads
                var boxes = new ThreePicturesRoundRecord.BoxEntry[ThreePicturesRound.MaxHands];
                for (int b = 0; b < boxes.Length; b++)
                {
                    int o = 6 + b * 3;
                    boxes[b] = new ThreePicturesRoundRecord.BoxEntry
                    {
                        Stake = long.Parse(f[o], CultureInfo.InvariantCulture),
                        Outcome = (ThreePicturesOutcome)int.Parse(f[o + 1], CultureInfo.InvariantCulture),
                        HalfPay = f[o + 2] == "1"
                    };
                }
                records.Add(new ThreePicturesRoundRecord(
                    int.Parse(f[0], CultureInfo.InvariantCulture),
                    int.Parse(f[1], CultureInfo.InvariantCulture),
                    f[2] == "1", boxes,
                    long.Parse(f[3], CultureInfo.InvariantCulture),
                    long.Parse(f[4], CultureInfo.InvariantCulture),
                    long.Parse(f[5], CultureInfo.InvariantCulture)));
            }
            if (records.Count > MaxSavedRounds) records.RemoveRange(0, records.Count - MaxSavedRounds);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ThreePicturesSaveSystem: load failed — {e.Message}");
            return false;
        }
    }
}
