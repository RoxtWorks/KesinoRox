using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class BankrollHudUI : MonoBehaviour
{
    Text hudText;
    Text bustHintText;
    InputField budgetInput;
    Button addFundsBtn;
    RectTransform addFundsRt;
    Bankroll bankroll;
    double displayedBalance;
    Coroutine animRoutine;
    bool wasBusted;

    public void Build(Transform canvas, Bankroll bankroll,
        UnityEngine.Events.UnityAction<long> onAddBudget, UnityEngine.Events.UnityAction<long> onReset)
    {
        this.bankroll = bankroll;

        // Centered on the canvas (x=0) instead of shifted left — the old -100 offset
        // was left over from an earlier layout and just reads as misaligned now that
        // nothing to its right forces it over. Framed panel (border + fill) so it
        // actually stands out from the near-black scene behind it instead of blending in.
        UIFactory.MakeFramedPanel(canvas, "HudPanelBg", new Vector2(0, 465), new Vector2(620, 90), UIFactory.PanelDarker);

        hudText = UIFactory.MakeText(canvas, "BankrollHud", new Vector2(0, 480), 24,
            sizeDelta: new Vector2(600, 40), color: UIFactory.TextLight, style: FontStyle.Bold);

        var inputGO = new GameObject("BudgetInput");
        inputGO.transform.SetParent(canvas, false);
        var inputRt = inputGO.AddComponent<RectTransform>();
        inputRt.sizeDelta = new Vector2(120, 32);
        inputRt.anchoredPosition = new Vector2(-120, 435);
        var inputImg = inputGO.AddComponent<Image>();
        inputImg.sprite = UIFactory.RoundedRect();
        inputImg.type = Image.Type.Sliced;
        inputImg.color = new Color(0.15f, 0.15f, 0.16f);
        budgetInput = inputGO.AddComponent<InputField>();
        var textGO = UIFactory.MakeText(inputGO.transform, "Text", Vector2.zero, 16, TextAnchor.MiddleLeft, new Vector2(110, 30));
        budgetInput.textComponent = textGO;
        budgetInput.text = "1000";

        // Adds to whatever's already in the bank — a top-up, not a wipe. RESET (below)
        // is the only button that actually zeroes the session out.
        addFundsBtn = UIFactory.MakeButton(canvas, "AddBudgetBtn", new Vector2(5, 435), new Vector2(110, 32),
            "ADD FUNDS", UIFactory.AccentDim, () =>
            {
                if (long.TryParse(budgetInput.text, out long value) && value > 0)
                    onAddBudget?.Invoke(value);
            }, 13);
        addFundsRt = addFundsBtn.GetComponent<RectTransform>();

        UIFactory.MakeButton(canvas, "ResetBtn", new Vector2(125, 435), new Vector2(110, 32),
            "RESET", UIFactory.Negative, () =>
            {
                long value = long.TryParse(budgetInput.text, out long v) && v > 0 ? v : bankroll.StartingBalance;
                onReset?.Invoke(value);
            }, 13);

        // Only shown when the balance can't cover even the cheapest chip — the one
        // real "why would I keep playing" dead end, since PlaceMulti otherwise just
        // silently refuses every click with no obvious next step.
        bustHintText = UIFactory.MakeText(canvas, "BustHint", new Vector2(0, 405), 13,
            sizeDelta: new Vector2(600, 20), color: UIFactory.Negative, style: FontStyle.Bold);
        bustHintText.text = "";

        Refresh();
    }

    // Animates the shown balance toward the real one instead of snapping instantly —
    // small wins tick up fast, big ones count for a beat, which reads as "counting
    // your money" rather than a static number that just changes.
    public void Refresh()
    {
        if (hudText == null || bankroll == null) return;
        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimateTo(bankroll.Balance));
    }

    IEnumerator AnimateTo(long target)
    {
        double start = displayedBalance;
        const float duration = 0.5f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float eased = 1f - Mathf.Pow(1f - u, 3f);
            displayedBalance = start + (target - start) * eased;
            UpdateText();
            yield return null;
        }
        displayedBalance = target;
        UpdateText();
    }

    void UpdateText()
    {
        long shown = (long)System.Math.Round(displayedBalance);
        long net = shown - bankroll.TotalFunded;
        hudText.color = net > 0 ? UIFactory.Positive : net < 0 ? UIFactory.Negative : UIFactory.TextLight;
        hudText.text = $"Balance: {shown}     Starting: {bankroll.StartingBalance}     Net: {net:+#;-#;0}";

        bool busted = bankroll.Balance < ChipDenominations.Values[0];
        bustHintText.text = busted ? "Out of chips — ADD FUNDS or hit RESET to keep playing" : "";
        if (busted && !wasBusted) JuiceTweens.Pulse(this, addFundsRt, peakScale: 1.15f, duration: 0.5f);
        wasBusted = busted;
    }
}
