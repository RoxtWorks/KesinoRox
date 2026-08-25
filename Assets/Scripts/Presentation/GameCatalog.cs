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
        new Entry("Main", "ROULETTE", new Color(0.16f, 0.55f, 0.32f)),
        new Entry("Blackjack", "BLACKJACK", new Color(0.62f, 0.16f, 0.18f)),
        new Entry("Baccarat", "BACCARAT", new Color(0.16f, 0.32f, 0.58f)),
        new Entry("Craps", "CRAPS", new Color(0.62f, 0.46f, 0.1f)),
    };
}
