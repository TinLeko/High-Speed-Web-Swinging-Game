using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the whole time trial UI in code (prompt, HUD, countdown, results) so nothing needs to be set up in the editor.
/// </summary>
/// <remarks>Created automatically by <see cref="TimeTrialManager"/>. Uses the legacy UI <see cref="Text"/> and Unity's built-in font.</remarks>
public class TimeTrialUI : MonoBehaviour
{
    private Font font;

    private GameObject promptPanel, hudRoot, resultsPanel;
    private Text promptTitle, promptBody;
    private Text timerText, infoText, limitText, toastText, hintText, countdownText;
    private Text resultTitle, resultMedal, resultTime, resultBest, resultRecord, resultSpeed, resultDetail, resultNext, resultHint;
    private Image medalIcon;
    private float countdownHideTime = -1f;
    private float toastHideTime = -1f;

    private static readonly Color Soft = new Color(0.85f, 0.9f, 1f, 1f);
    private static readonly Color Urgent = new Color(1f, 0.3f, 0.25f, 1f);

    private void Awake()
    {
        Build();
        HideAll();
    }

    private void Update()
    {
        if (countdownHideTime > 0f && Time.unscaledTime >= countdownHideTime)
        {
            countdownHideTime = -1f;
            countdownText.gameObject.SetActive(false);
        }

        if (toastHideTime > 0f && Time.unscaledTime >= toastHideTime)
        {
            toastHideTime = -1f;
            toastText.gameObject.SetActive(false);
        }
    }

    public void ShowPrompt(string title, string body)
    {
        promptTitle.text = title;
        promptBody.text = body;
        promptPanel.SetActive(true);
    }

    public void HidePrompt() => promptPanel.SetActive(false);

    public void ShowHud(bool show, string hint = "")
    {
        hudRoot.SetActive(show);
        hintText.text = hint;

        if (!show)
            toastText.gameObject.SetActive(false);
    }

    public void SetHud(string time, string info)
    {
        timerText.text = time;
        infoText.text = info;
    }

    public void SetLimit(string text, bool urgent)
    {
        if (string.IsNullOrEmpty(text))
        {
            limitText.gameObject.SetActive(false);
            return;
        }

        limitText.gameObject.SetActive(true);
        limitText.text = text;

        if (urgent)
        {
            limitText.color = Urgent;
        }
        else
        {
            limitText.color = Soft;
        }
    }

    public void ShowMessage(string text, float seconds)
    {
        toastText.text = text;
        toastText.gameObject.SetActive(true);
        toastHideTime = Time.unscaledTime + seconds;
    }

    /// <summary> Used for 3, 2, 1. </summary>
    public void ShowCountdown(string text)
    {
        countdownText.text = text;
        countdownText.gameObject.SetActive(true);
        countdownHideTime = -1f;
    }

    /// <summary>Shows a big message and hides it by itself (used for GO!).</summary>
    public void FlashMessage(string text, float seconds)
    {
        countdownText.text = text;
        countdownText.gameObject.SetActive(true);
        countdownHideTime = Time.unscaledTime + seconds;
    }

    public void HideCountdown()
    {
        countdownHideTime = -1f;
        countdownText.gameObject.SetActive(false);
    }

    public void ShowResults(string trialName, string timeText, string bestText, bool newRecord, string topSpeedText, string detailText, string medalName, Color medalColor, bool hasMedal, string nextGoalText, string hint)
    {
        resultTitle.text = trialName.ToUpperInvariant() + " - COMPLETE";
        resultTime.text = "Time  " + timeText;
        resultBest.text = "Best  " + bestText;
        resultRecord.text = newRecord ? "NEW RECORD!" : "";
        resultSpeed.text = topSpeedText;
        resultDetail.text = detailText;
        resultMedal.text = medalName;
        resultMedal.color = medalColor;
        medalIcon.color = hasMedal ? medalColor : new Color(0.35f, 0.35f, 0.35f, 0.6f);
        resultNext.text = nextGoalText;
        resultHint.text = hint;
        resultsPanel.SetActive(true);
    }

    public void ShowFailed(string trialName, string progressText, string bestText, string topSpeedText, string detailText, string limitText, string hint)
    {
        resultTitle.text = trialName.ToUpperInvariant() + " - TIME'S UP";
        resultMedal.text = "DID NOT FINISH";
        resultMedal.color = Urgent;
        medalIcon.color = new Color(0.35f, 0.35f, 0.35f, 0.6f);
        resultTime.text = progressText;
        resultBest.text = bestText;
        resultRecord.text = "";
        resultSpeed.text = topSpeedText;
        resultDetail.text = detailText;
        resultNext.text = limitText;
        resultHint.text = hint;
        resultsPanel.SetActive(true);
    }

    public void HideResults() => resultsPanel.SetActive(false);

    public void HideAll()
    {
        promptPanel.SetActive(false);
        hudRoot.SetActive(false);
        resultsPanel.SetActive(false);
        toastText.gameObject.SetActive(false);
        HideCountdown();
    }

    // Building

    private void Build()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasObject = new GameObject("TimeTrialCanvas");
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasObject.transform;

        Color gold = new Color(1f, 0.84f, 0f, 1f);
        Color white = Color.white;
        Vector2 mid = new Vector2(0.5f, 0.5f);

        // prompt
        promptPanel = MakePanel(root, "Prompt", mid, new Vector2(0f, -150f), new Vector2(900f, 380f), new Color(0f, 0f, 0f, 0.7f));
        promptTitle = MakeText(promptPanel.transform, "Title", mid, new Vector2(0f, 140f), new Vector2(860f, 70f), 46, TextAnchor.MiddleCenter, gold, FontStyle.Bold);
        promptBody = MakeText(promptPanel.transform, "Body", mid, new Vector2(0f, -25f), new Vector2(860f, 250f), 28, TextAnchor.MiddleCenter, white);

        // HUD
        hudRoot = new GameObject("Hud", typeof(RectTransform));
        hudRoot.transform.SetParent(root, false);
        var hudRect = (RectTransform)hudRoot.transform;
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = hudRect.offsetMax = Vector2.zero;

        timerText = MakeText(hudRoot.transform, "Timer", new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(700f, 100f), 76, TextAnchor.UpperCenter, white, FontStyle.Bold);
        infoText = MakeText(hudRoot.transform, "Info", new Vector2(0.5f, 1f), new Vector2(0f, -125f), new Vector2(900f, 50f), 32, TextAnchor.UpperCenter, Soft);
        limitText = MakeText(hudRoot.transform, "Limit", new Vector2(0.5f, 1f), new Vector2(0f, -168f), new Vector2(900f, 50f), 36, TextAnchor.UpperCenter, Soft, FontStyle.Bold);
        toastText = MakeText(hudRoot.transform, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1100f, 56f), 40, TextAnchor.UpperCenter, gold, FontStyle.Bold);
        hintText = MakeText(hudRoot.transform, "Hint", new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1200f, 40f), 24, TextAnchor.LowerCenter, Soft);

        // Countdown / flash message
        countdownText = MakeText(root, "Countdown", mid, new Vector2(0f, 120f), new Vector2(800f, 220f), 170, TextAnchor.MiddleCenter, gold, FontStyle.Bold);

        // results
        resultsPanel = MakePanel(root, "Results", mid, Vector2.zero, new Vector2(940f, 720f), new Color(0f, 0f, 0f, 0.82f));
        Transform r = resultsPanel.transform;
        resultTitle = MakeText(r, "Title", mid, new Vector2(0f, 300f), new Vector2(900f, 70f), 46, TextAnchor.MiddleCenter, gold, FontStyle.Bold);
        var iconObject = new GameObject("MedalIcon", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(r, false);
        var iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = mid;
        iconRect.anchoredPosition = new Vector2(0f, 180f);
        iconRect.sizeDelta = new Vector2(140f, 140f);
        medalIcon = iconObject.GetComponent<Image>();
        medalIcon.sprite = MakeCircleSprite(128);
        medalIcon.raycastTarget = false;
        resultMedal = MakeText(r, "Medal", mid, new Vector2(0f, 65f), new Vector2(900f, 70f), 54, TextAnchor.MiddleCenter, white, FontStyle.Bold);
        resultTime = MakeText(r, "Time", mid, new Vector2(0f, -15f), new Vector2(900f, 70f), 58, TextAnchor.MiddleCenter, white, FontStyle.Bold);
        resultBest = MakeText(r, "Best", mid, new Vector2(0f, -75f), new Vector2(900f, 50f), 32, TextAnchor.MiddleCenter, Soft);
        resultRecord = MakeText(r, "Record", mid, new Vector2(0f, -118f), new Vector2(900f, 50f), 36, TextAnchor.MiddleCenter, gold, FontStyle.Bold);
        resultSpeed = MakeText(r, "TopSpeed", mid, new Vector2(0f, -178f), new Vector2(900f, 60f), 40, TextAnchor.MiddleCenter, white);
        resultDetail = MakeText(r, "Detail", mid, new Vector2(0f, -225f), new Vector2(900f, 50f), 28, TextAnchor.MiddleCenter, Soft);
        resultNext = MakeText(r, "Next", mid, new Vector2(0f, -272f), new Vector2(900f, 50f), 28, TextAnchor.MiddleCenter, Soft);
        resultHint = MakeText(r, "Hint", mid, new Vector2(0f, -325f), new Vector2(900f, 50f), 30, TextAnchor.MiddleCenter, white);
    }

    private GameObject MakePanel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = panel.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return panel;
    }

    private Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color, FontStyle style = FontStyle.Normal)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = (RectTransform)textObject.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        var shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static Sprite MakeCircleSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                float alpha = Mathf.Clamp01(radius - distance); // soft 1px edge
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
