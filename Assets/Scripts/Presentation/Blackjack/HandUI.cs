using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// Displays one hand: a row of CardUI plus a live total badge underneath. Used for
// both the dealer (always exactly one hand) and the player (one per split, laid out
// side by side by the caller via different anchoredPos values). New cards pop in as
// they're added; cards already shown just reposition/re-text without re-animating,
// tracked via lastRenderedCount. Plain class, not a MonoBehaviour — it doesn't run
// any coroutines itself (CardUI owns its own pop-in animation via its own
// MonoBehaviour instance), so there's no need for it to live on a GameObject; it's
// just instantiated with `new HandUI()` and Build() creates its own root GameObject.
public class HandUI
{
    const float CardSpacing = 46f;
    static readonly Vector2 CardSize = new Vector2(64f, 90f);
    static readonly Color HighlightBlue = new Color(0.25f, 0.55f, 1f);
    static readonly Color BustRed = new Color(0.75f, 0.15f, 0.15f);
    static readonly Color NaturalGold = new Color(0.85f, 0.68f, 0.21f);

    RectTransform root;
    readonly List<CardUI> cardVisuals = new List<CardUI>();
    GameObject totalBadgeBg;
    Image totalBadgeImg;
    Text totalText;
    Text doubledTag;
    Text playingLabel;
    int lastRenderedCount;

    public RectTransform Root => root;

    public void Build(Transform canvas, Vector2 anchoredPos)
    {
        var rootGO = new GameObject("HandUI");
        rootGO.transform.SetParent(canvas, false);
        root = rootGO.AddComponent<RectTransform>();
        root.sizeDelta = new Vector2(320, 140);
        root.anchoredPosition = anchoredPos;

        playingLabel = UIFactory.MakeText(root, "PlayingLabel", new Vector2(0, 55), 12,
            sizeDelta: new Vector2(160, 20), color: HighlightBlue, style: FontStyle.Bold);
        playingLabel.text = "▶ PLAYING";
        playingLabel.gameObject.SetActive(false);

        totalBadgeBg = UIFactory.MakePanel(root, "TotalBadgeBg", new Vector2(0, -70), new Vector2(110, 28), UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(totalBadgeBg, UIFactory.AccentDim, square: true);
        totalBadgeImg = totalBadgeBg.GetComponent<Image>();
        totalText = UIFactory.MakeText(root, "TotalText", new Vector2(0, -70), 15,
            sizeDelta: new Vector2(100, 24), color: UIFactory.TextLight, style: FontStyle.Bold);
        totalText.text = "";

        doubledTag = UIFactory.MakeText(root, "DoubledTag", new Vector2(65, -70), 12,
            sizeDelta: new Vector2(40, 22), color: UIFactory.Accent, style: FontStyle.Bold);
        doubledTag.text = "2×";
        doubledTag.gameObject.SetActive(false);
    }

    // hideHoleCard: dealer's second card shows face-down (and the total badge shows
    // only the up-card's value) until the round is over and RevealHole is called.
    // highlighted: blue-tinges the total badge — same treatment the roulette wheel/
    // belt use for "this is the number/hand your bet is riding on". maxCards: caps
    // how many of the hand's cards actually render — used to stagger the initial
    // deal one card at a time rather than snapping all of them in at once; the total
    // badge stays blank while truncated, same as a real table not showing a running
    // total until every card's actually down.
    public void Render(BlackjackHand hand, bool hideHoleCard = false, bool highlighted = false, int? maxCards = null)
    {
        root.gameObject.SetActive(true);
        int count = Mathf.Min(hand.Cards.Count, maxCards ?? hand.Cards.Count);
        EnsureCardCount(count);

        float totalWidth = (count - 1) * CardSpacing;
        float startX = -totalWidth / 2f;
        for (int i = 0; i < count; i++)
        {
            var cardUI = cardVisuals[i];
            cardUI.gameObject.SetActive(true);
            var cardRt = cardUI.GetComponent<RectTransform>();
            cardRt.anchoredPosition = new Vector2(startX + i * CardSpacing, 0f);

            bool isNew = i >= lastRenderedCount;
            if (hideHoleCard && i == 1)
                cardUI.SetFaceDown(animatePopIn: isNew);
            else
                cardUI.SetCard(hand.Cards[i], animatePopIn: isNew);
        }
        for (int i = count; i < cardVisuals.Count; i++)
            cardVisuals[i].gameObject.SetActive(false);
        lastRenderedCount = count;

        bool truncated = count < hand.Cards.Count;
        int shownTotal = hideHoleCard && count >= 1 ? hand.Cards[0].HardValue : hand.BestTotal;
        totalText.text = truncated ? "" : shownTotal.ToString();

        // Bust (red) and natural-21 (gold) read as more informative than the plain
        // "this is the active hand" blue highlight, so they take priority over it —
        // a busted or blackjack hand is never still "the one you're playing" anyway.
        bool showBust = !truncated && !hideHoleCard && hand.IsBust;
        bool showNatural = !truncated && !hideHoleCard && hand.BestTotal == 21 && !hand.IsBust;
        totalBadgeImg.color = showBust ? Color.Lerp(UIFactory.PanelDarker, BustRed, 0.6f)
            : showNatural ? Color.Lerp(UIFactory.PanelDarker, NaturalGold, 0.55f)
            : highlighted ? Color.Lerp(UIFactory.PanelDarker, HighlightBlue, 0.55f)
            : UIFactory.PanelDarker;

        doubledTag.gameObject.SetActive(!truncated && hand.DoubledDown);
        playingLabel.gameObject.SetActive(highlighted);
    }

    public void Clear()
    {
        var cardsToFlyOut = new List<CardUI>(cardVisuals);
        cardVisuals.Clear();
        lastRenderedCount = 0;
        totalText.text = "";
        doubledTag.gameObject.SetActive(false);
        playingLabel.gameObject.SetActive(false);

        if (cardsToFlyOut.Count == 0)
        {
            root.gameObject.SetActive(false);
            return;
        }
        foreach (var card in cardsToFlyOut)
            card.FlyOut(() => { if (card != null) Object.Destroy(card.gameObject); });
        // Root stays active (and visible) through the fly-out so the cards' motion
        // actually renders — deactivating it immediately would hide them mid-flight.
        var rootGO = root.gameObject;
        DOVirtual.DelayedCall(0.3f, () => { if (rootGO != null) rootGO.SetActive(false); });
    }

    void EnsureCardCount(int count)
    {
        while (cardVisuals.Count < count)
            cardVisuals.Add(CardUI.Create(root, Vector2.zero, CardSize));
    }
}
