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
        bg.color = CardWhite;
        label.color = card.IsRed ? RedSuit : BlackSuit;
        label.text = $"{RankLabel(card.Rank)}\n{SuitSymbol(card.Suit)}";
        if (animatePopIn) JuiceTweens.PopIn(this, rt);
    }

    public void SetFaceDown(bool animatePopIn = true)
    {
        bg.color = CardBack;
        label.text = "";
        if (animatePopIn) JuiceTweens.PopIn(this, rt);
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
