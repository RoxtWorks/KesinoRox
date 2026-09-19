using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// Baccarat's much simpler equivalent of blackjack's HandUI — a row of cards plus a
// live point badge underneath. No soft/hard ace handling or bust concept (baccarat
// totals just wrap mod 10) and no highlight/hide-hole-card states — both hands
// reveal fully once dealt, there's no "your turn" since baccarat has zero player
// decisions mid-hand.
public class BaccaratHandUI
{
    float cardSpacing = 72f;
    Vector2 cardSize = new Vector2(64f, 90f);
    static readonly Color NaturalGold = new Color(0.85f, 0.68f, 0.21f);

    RectTransform root;
    readonly List<CardUI> cardVisuals = new List<CardUI>();
    GameObject totalBadgeBg;
    Image totalBadgeImg;
    Text totalText;
    int lastRenderedCount;
    Tween pendingHideTween;

    public RectTransform Root => root;

    // spacing must stay wider than the card, or the cards overlap
    public void Build(Transform canvas, Vector2 anchoredPos, Vector2 cardSize, float spacing)
    {
        this.cardSize = cardSize;
        cardSpacing = spacing;
        float half = cardSize.y / 2f;
        var rootGO = new GameObject("BaccaratHandUI");
        rootGO.transform.SetParent(canvas, false);
        root = rootGO.AddComponent<RectTransform>();
        root.sizeDelta = new Vector2(320, cardSize.y + 50f);
        root.anchoredPosition = anchoredPos;

        totalBadgeBg = UIFactory.MakePanel(root, "TotalBadgeBg", new Vector2(0, -half - 20f), new Vector2(90, 28), UIFactory.PanelDarker, shadow: false);
        UIFactory.AddSharpFrame(totalBadgeBg, UIFactory.AccentDim, square: true);
        totalBadgeImg = totalBadgeBg.GetComponent<Image>();
        totalText = UIFactory.MakeText(root, "TotalText", new Vector2(0, -half - 20f), 17,
            sizeDelta: new Vector2(80, 24), color: UIFactory.TextLight, style: FontStyle.Bold);
        totalText.text = "";
    }

    // maxCards truncates how many of the hand's cards actually render — used to
    // stagger the deal one card at a time, same trick blackjack's HandUI uses.
    public void Render(BaccaratHand hand, int? maxCards = null)
    {
        // A fresh deal can start moments after Clear() swept the previous hand out —
        // cancel that hand's delayed root-hide so it can't fire mid-way through this
        // one being dealt (both hands share this same root).
        pendingHideTween?.Kill();
        pendingHideTween = null;
        root.gameObject.SetActive(true);
        int count = Mathf.Min(hand.Cards.Count, maxCards ?? hand.Cards.Count);
        EnsureCardCount(count);

        float totalWidth = (count - 1) * cardSpacing;
        float startX = -totalWidth / 2f;
        for (int i = 0; i < count; i++)
        {
            var cardUI = cardVisuals[i];
            cardUI.gameObject.SetActive(true);
            var cardRt = cardUI.GetComponent<RectTransform>();
            bool isNew = i >= lastRenderedCount;
            // A card still flying in would finish at its old slot — land it first, then move it
            if (!isNew) cardRt.DOKill(true);
            cardRt.anchoredPosition = new Vector2(startX + i * cardSpacing, 0f);
            if (isNew) cardUI.SetCard(hand.Cards[i], animatePopIn: true);
        }
        for (int i = count; i < cardVisuals.Count; i++)
            cardVisuals[i].gameObject.SetActive(false);
        lastRenderedCount = count;

        bool truncated = count < hand.Cards.Count;
        totalText.text = truncated ? "" : hand.Point.ToString();

        bool showNatural = !truncated && hand.IsNatural;
        totalBadgeImg.color = showNatural ? Color.Lerp(UIFactory.PanelDarker, NaturalGold, 0.55f) : UIFactory.PanelDarker;
    }

    public void Clear()
    {
        pendingHideTween?.Kill();
        pendingHideTween = null;

        var cardsToFlyOut = new List<CardUI>(cardVisuals);
        cardVisuals.Clear();
        lastRenderedCount = 0;
        totalText.text = "";
        totalBadgeImg.color = UIFactory.PanelDarker;

        if (cardsToFlyOut.Count == 0)
        {
            root.gameObject.SetActive(false);
            return;
        }
        foreach (var card in cardsToFlyOut)
            card.FlyOut(() => { if (card != null) Object.Destroy(card.gameObject); });
        var rootGO = root.gameObject;
        pendingHideTween = DOVirtual.DelayedCall(0.3f, () => { if (rootGO != null) rootGO.SetActive(false); });
    }

    void EnsureCardCount(int count)
    {
        while (cardVisuals.Count < count)
        {
            var card = CardUI.Create(root, Vector2.zero, cardSize);
            foreach (var t in card.GetComponentsInChildren<Text>()) t.fontSize = Mathf.RoundToInt(cardSize.y * 0.24f);
            cardVisuals.Add(card);
        }
    }
}
