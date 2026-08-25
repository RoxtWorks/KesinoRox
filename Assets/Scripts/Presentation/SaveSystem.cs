using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Plain-text session save — bankroll plus every spin this session — so relaunching
// the app picks up exactly where it left off. Written next to the running .exe;
// Application.dataPath's parent resolves to the right folder either way: for a
// build that's "<Build>/RouletteSim_Data" -> parent is the folder holding the exe,
// and in the Editor it's "<Project>/Assets" -> parent is the project root.
// Reload replays every record through the same AddRecord/AddSpin paths a live spin
// uses, so History/P-L/Hot-Cold/Recent-Spins come back too, not just the balance.
public static class SaveSystem
{
    const string FileName = "roulettesim_save.txt";
    // Caps the save file the same way the live trackers already cap themselves
    // (PastSpinsStripUI/HotColdTrackerUI keep at most 500) — no point saving more
    // history than anything on screen will ever use.
    const int MaxSavedRecords = 500;

    static string FilePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName);

    public static void Save(Bankroll bankroll, int nextSpinIndex, List<SpinRecord> records)
    {
        try
        {
            var lines = new List<string>
            {
                string.Join(",", bankroll.Balance, bankroll.StartingBalance, bankroll.TotalFunded, nextSpinIndex)
            };
            int start = Mathf.Max(0, records.Count - MaxSavedRecords);
            for (int i = start; i < records.Count; i++)
            {
                var r = records[i];
                lines.Add(string.Join(",", r.SpinIndex, r.WinningNumber, r.TotalStaked, r.TotalReturned, r.BalanceAfter));
            }
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: failed to save to '{FilePath}' — {e.Message}");
        }
    }

    public static bool TryLoad(out long balance, out long startingBalance, out long totalFunded,
        out int nextSpinIndex, out List<SpinRecord> records)
    {
        balance = startingBalance = totalFunded = 0;
        nextSpinIndex = 0;
        records = new List<SpinRecord>();

        if (!File.Exists(FilePath)) return false;

        try
        {
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length == 0) return false;

            var header = lines[0].Split(',');
            balance = long.Parse(header[0], CultureInfo.InvariantCulture);
            startingBalance = long.Parse(header[1], CultureInfo.InvariantCulture);
            totalFunded = long.Parse(header[2], CultureInfo.InvariantCulture);
            nextSpinIndex = int.Parse(header[3], CultureInfo.InvariantCulture);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = lines[i].Split(',');
                records.Add(new SpinRecord(
                    int.Parse(f[0], CultureInfo.InvariantCulture),
                    int.Parse(f[1], CultureInfo.InvariantCulture),
                    long.Parse(f[2], CultureInfo.InvariantCulture),
                    long.Parse(f[3], CultureInfo.InvariantCulture),
                    long.Parse(f[4], CultureInfo.InvariantCulture)));
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveSystem: failed to load '{FilePath}' — {e.Message}");
            return false;
        }
    }
}
