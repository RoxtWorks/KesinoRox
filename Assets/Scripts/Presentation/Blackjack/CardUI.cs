using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// One playing card visual — a white rounded-rect (UIFactory.RoundedRect, same sprite
// every panel/button in this project already uses) plus big rank/suit text, red for
// hearts/diamonds and black for clubs/spades like a real card. Supports a face-down
// "back" state for the dealer's hole card until it's revealed.
public class CardUI : MonoBehaviour
{
    Image bg;
    Text label;
    RectTransform rt;

    static readonly Color CardWhite = Color.white;
    static readonly Color CardBack = new Color(0.13f, 0.22f, 0.5f);
    static readonly Color RedSuit = new Color(0.75f, 0.1f, 0.1f);
    static readonly Color BlackSuit = new Color(0.08f, 0.08f, 0.1f);

    public static CardUI Create(Transform parent, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject("Card");
        go.transform.SetParent(parent, false);
        var card = go.AddComponent<CardUI>();
        card.BuildSelf(anchoredPos, size);
        return card;
    }

    void BuildSelf(Vector2 anchoredPos, Vector2 size)
    {
        // Create() always builds a brand-new GameObject, so there's never an
        // existing RectTransform to find here — go straight to AddComponent
        // (same pattern every other UI builder in this project uses). The earlier
        // GetComponent<RectTransform>() ?? AddComponent<RectTransform>() form left
        // rt null at runtime (MissingComponentException on the next line) despite
        // looking equivalent on paper.
        rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        bg = gameObject.AddComponent<Image>();
        bg.sprite = UIFactory.RoundedRect();
        bg.type = Image.Type.Sliced;
        bg.color = CardWhite;

        var shadow = gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
        shadow.effectDistance = new Vector2(0f, -2f);

        label = UIFactory.MakeText(transform, "Label", Vector2.zero, 22,
            sizeDelta: size - new Vector2(6f, 6f), color: BlackSuit, style: FontStyle.Bold);
        label.text = "";
    }

    public void SetCard(Card card, bool animatePopIn = true)
    {
        // Reused CardUI instances can be mid-FlyOut (faded, off-position) from a
        // previous round — kill that before resetting, so a reused instance always
        // starts its next deal-in from a clean, fully-opaque state.
        bg.DOKill();
        label.DOKill();
        bg.color = CardWhite;
        label.color = card.IsRed ? RedSuit : BlackSuit;
        label.text = $"{RankLabel(card.Rank)}\n{SuitSymbol(card.Suit)}";
        if (animatePopIn) DealIn();
    }

    public void SetFaceDown(bool animatePopIn = true)
    {
        bg.DOKill();
        label.DOKill();
        bg.color = CardBack;
        label.text = "";
        if (animatePopIn) DealIn();
    }

    const float DealDuration = 0.2f;
    static readonly Vector2 DealFromOffset = new Vector2(35f, 55f);
    const float DealFromRotation = -14f;

    // Cards fly in from just above/beside their final slot with a quick scale-up
    // and a small rotation flourish that settles flat — reads much closer to a real
    // dealt card landing than a plain in-place pop.
    void DealIn()
    {
        rt.DOKill();
        Vector2 finalPos = rt.anchoredPosition;
        rt.anchoredPosition = finalPos + DealFromOffset;
        rt.localScale = Vector3.one * 0.6f;
        rt.localRotation = Quaternion.Euler(0f, 0f, DealFromRotation);

        var seq = DOTween.Sequence();
        seq.Join(rt.DOAnchorPos(finalPos, DealDuration).SetEase(Ease.OutBack));
        seq.Join(rt.DOScale(1f, DealDuration).SetEase(Ease.OutBack));
        seq.Join(rt.DORotateQuaternion(Quaternion.identity, DealDuration).SetEase(Ease.OutQuad));
        seq.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    const float FlyOutDuration = 0.22f;

    // Swept off toward the top of the table instead of just vanishing — this is the
    // "getting out" half of the deal-in/deal-out pairing.
    public void FlyOut(System.Action onComplete = null)
    {
        rt.DOKill();
        var seq = DOTween.Sequence();
        seq.Join(rt.DOAnchorPos(rt.anchoredPosition + new Vector2(0f, 90f), FlyOutDuration).SetEase(Ease.InBack));
        seq.Join(rt.DOScale(0.5f, FlyOutDuration).SetEase(Ease.InBack));
        seq.Join(bg.DOFade(0f, FlyOutDuration * 0.85f));
        seq.Join(label.DOFade(0f, FlyOutDuration * 0.7f));
        seq.OnComplete(() => onComplete?.Invoke());
        seq.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    static string RankLabel(Rank rank) => rank switch
    {
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        Rank.Ace => "A",
        _ => ((int)rank).ToString()
    };

    static string SuitSymbol(Suit suit) => suit switch
    {
        Suit.Hearts => "♥",
        Suit.Diamonds => "♦",
        Suit.Clubs => "♣",
        Suit.Spades => "♠",
        _ => "?"
    };
}
