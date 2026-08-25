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
    const float CardSpacing = 46f;
    static readonly Vector2 CardSize = new Vector2(64f, 90f);
    static readonly Color NaturalGold = new Color(0.85f, 0.68f, 0.21f);

    RectTransform root;
    readonly List<CardUI> cardVisuals = new List<CardUI>();
    GameObject totalBadgeBg;
    Image totalBadgeImg;
    Text totalText;
    int lastRenderedCount;
    Tween pendingHideTween;

    public RectTransform Root => root;

    public void Build(Transform canvas, Vector2 anchoredPos)
    {
        var rootGO = new GameObject("BaccaratHandUI");
        rootGO.transform.SetParent(canvas, false);
        root = rootGO.AddComponent<RectTransform>();
        root.sizeDelta = new Vector2(320, 140);
        root.anchoredPosition = anchoredPos;

        totalBadgeBg = UIFactory.MakePanel(root, "TotalBadgeBg", new Vector2(0, -70), new Vector2(90, 28), UIFactory.PanelDarker, shadow: false);
        totalBadgeImg = totalBadgeBg.GetComponent<Image>();
        totalText = UIFactory.MakeText(root, "TotalText", new Vector2(0, -70), 15,
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

        float totalWidth = (count - 1) * CardSpacing;
        float startX = -totalWidth / 2f;
        for (int i = 0; i < count; i++)
        {
            var cardUI = cardVisuals[i];
            cardUI.gameObject.SetActive(true);
            var cardRt = cardUI.GetComponent<RectTransform>();
            cardRt.anchoredPosition = new Vector2(startX + i * CardSpacing, 0f);
            bool isNew = i >= lastRenderedCount;
            cardUI.SetCard(hand.Cards[i], animatePopIn: isNew);
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
            cardVisuals.Add(CardUI.Create(root, Vector2.zero, CardSize));
    }
}
