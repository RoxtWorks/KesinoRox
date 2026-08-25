using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChipSelectorUI : MonoBehaviour
{
    public long SelectedChip { get; private set; } = ChipDenominations.Values[0];

    readonly List<Image> ringImages = new List<Image>();
    static readonly Color[] ChipFillColors =
    {
        new Color(0.65f, 0.12f, 0.12f), // 25 — red
        new Color(0.1f, 0.35f, 0.6f),   // 100 — blue
        new Color(0.1f, 0.1f, 0.1f),    // 500 — black
    };

    CanvasGroup group;
    SoundManager soundManager;

    public void Build(Transform canvas, SoundManager soundManager = null)
    {
        this.soundManager = soundManager;
        var rootGO = new GameObject("ChipSelectorRoot");
        rootGO.transform.SetParent(canvas, false);
        var rt = rootGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        group = rootGO.AddComponent<CanvasGroup>();
        var root = rootGO.transform;

        UIFactory.MakePanel(root, "ChipPanelBg", new Vector2(-810, 335), new Vector2(260, 130), UIFactory.PanelDark);
        UIFactory.MakeSectionHeader(root, "Chips", new Vector2(-810, 385), new Vector2(240, 20));

        float x = -890;
        for (int i = 0; i < ChipDenominations.Values.Length; i++)
        {
            long value = ChipDenominations.Values[i];
            Color fill = ChipFillColors[i % ChipFillColors.Length];
            var btn = UIFactory.MakeChip(root, $"Chip_{value}", new Vector2(x, 330), 64, value.ToString(), fill, () => Select(value));
            ringImages.Add(btn.GetComponent<Image>());
            x += 80;
        }
        HighlightSelected();
    }

    public void SetVisible(bool visible)
    {
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }

    void Select(long value)
    {
        SelectedChip = value;
        HighlightSelected();
        soundManager?.PlayClick();
    }

    // Bright near-white silver-blue vs a dark, clearly-dimmed grey — wide contrast
    // so which chip is selected reads at a glance instead of blending in.
    static readonly Color SelectedRing = new Color(0.85f, 0.92f, 1f);
    static readonly Color UnselectedRing = new Color(0.26f, 0.28f, 0.32f);

    void HighlightSelected()
    {
        for (int i = 0; i < ringImages.Count; i++)
            ringImages[i].color = ChipDenominations.Values[i] == SelectedChip ? SelectedRing : UnselectedRing;
    }
}
