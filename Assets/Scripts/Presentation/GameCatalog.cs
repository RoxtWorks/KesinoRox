using System.Collections.Generic;
using UnityEngine;

// Single source of truth for "what games exist" — the main menu's button row and
// every in-game GameSwitcherPanel both read this same list, so adding a fourth game
// later means adding one entry here, not hunting down every place that names the
// other three.
public static class GameCatalog
{
    public readonly struct Entry
    {
        public readonly string SceneName;
        public readonly string DisplayName;
        public readonly Color Color;

        public Entry(string sceneName, string displayName, Color color)
        {
            SceneName = sceneName;
            DisplayName = displayName;
            Color = color;
        }
    }

    public static readonly List<Entry> Games = new List<Entry>
    {
        new Entry("Roulette",      "ROULETTE",     new Color(0.16f, 0.55f, 0.32f)),
        new Entry("Blackjack",     "BLACKJACK",    new Color(0.62f, 0.16f, 0.18f)),
        new Entry("Baccarat",      "BACCARAT",     new Color(0.16f, 0.32f, 0.58f)),
        new Entry("CraplessCraps", "CRAPLESS",     new Color(0.62f, 0.46f, 0.1f)),
        new Entry("NormalCraps",   "CRAPS",        new Color(0.55f, 0.38f, 0.08f)),
        new Entry("ThreePictures", "3 PICTURES",   new Color(0.55f, 0.15f, 0.48f)),
        new Entry("SicBo",         "SIC BO",       new Color(0.18f, 0.50f, 0.55f)),
        new Entry("ThreeCardPoker","3-CARD POKER", new Color(0.20f, 0.45f, 0.20f)),
    };
}
