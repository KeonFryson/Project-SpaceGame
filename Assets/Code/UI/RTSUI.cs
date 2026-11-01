using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Builds the whole RTS HUD from code (no prefabs to wire):
///  - top bar: system name, the 8 resources (empire totals), game clock + speed buttons
///  - info panel (bottom-left): whatever is selected
///  - controls hint (bottom-right)
/// Scales to any screen shape (see UIFit) and respects the device safe area.
/// Uses TextMeshPro (needs TMP Essential Resources imported once:
/// Window > TextMeshPro > Import TMP Essential Resources).
/// </summary>
public class RTSUI : MonoBehaviour
{
    static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.92f);
    static readonly Color BtnIdle = new Color(0.16f, 0.20f, 0.27f, 1f);
    static readonly Color BtnActive = new Color(0.25f, 0.55f, 0.95f, 1f);

    TMP_Text systemLabel, clockLabel, infoTitle, infoBody;
    readonly TMP_Text[] resLabels = new TMP_Text[ResourceInfo.Count];
    readonly Image[] speedImgs = new Image[4];
    GameObject infoPanel;
    GalaxyManager galaxy;
    float timer;

    void Awake()
    {
        EnsureEventSystem();
        BuildCanvas();
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 0.2f;
        Refresh();
    }

    // ------------------------------------------------------------------ refresh

    void Refresh()
    {
        var gs = GameState.Instance;
        var clock = GameClock.Instance;
        if (galaxy == null) galaxy = FindFirstObjectByType<GalaxyManager>();

        if (galaxy != null && galaxy.CurrentSystem != null)
            systemLabel.text = galaxy.CurrentSystem.name + "  <color=#888888>" + galaxy.CurrentSystem.starClass + "</color>";

        if (gs != null)
        {
            for (int i = 0; i < ResourceInfo.Count; i++)
            {
                var t = (ResourceType)i;
                string value = t == ResourceType.Energy
                    ? gs.EnergyText()
                    : gs.GetEmpireTotal(t).ToString("N0");
                resLabels[i].text = ResourceInfo.Coloured(t) + " " + value;
            }
        }

        if (clock != null)
        {
            int total = (int)clock.GameTime;
            clockLabel.text = "T+" + (total / 60).ToString("00") + ":" + (total % 60).ToString("00") +
                              (clock.Paused ? " <color=#ffcc55>PAUSED</color>" : "");
            for (int i = 0; i < speedImgs.Length; i++)
                speedImgs[i].color = i == clock.SpeedIndex ? BtnActive : BtnIdle;
        }

        var sel = SelectionManager.Instance;
        if (sel != null && sel.GetInfo(out string title, out string body))
        {
            infoPanel.SetActive(true);
            infoTitle.text = title;
            infoBody.text = body;
        }
        else if (infoPanel.activeSelf)
        {
            infoPanel.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ building the UI

    void BuildCanvas()
    {
        var cgo = new GameObject("RTSCanvas");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        cgo.AddComponent<CanvasScaler>();
        cgo.AddComponent<UIFit>();            // fits the 1920x1080 layout to any aspect ratio
        cgo.AddComponent<GraphicRaycaster>();

        // everything lives inside the safe area (notches / rounded corners)
        var safe = NewRect("SafeArea", cgo.transform);
        Stretch(safe, 0f, 0f, 0f, 0f);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        BuildTopBar(safe);
        BuildInfoPanel(safe);
        BuildHint(safe);
    }

    void BuildTopBar(Transform parent)
    {
        var bar = NewRect("TopBar", parent);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 44f);
        bar.anchoredPosition = Vector2.zero;
        bar.gameObject.AddComponent<Image>().color = PanelColor;

        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 0, 0);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Labels shrink their font instead of overflowing, so the bar always fits the 1920 reference width.
        systemLabel = NewText(bar, "System", 18, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 11f);
        SetLayout(systemLabel.gameObject, 200f);

        for (int i = 0; i < ResourceInfo.Count; i++)
        {
            resLabels[i] = NewText(bar, "Res" + i, 16, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 10f);
            SetLayout(resLabels[i].gameObject, i == (int)ResourceType.ResearchData ? 150f : 135f);
        }

        var spacer = NewRect("Spacer", bar);
        SetLayout(spacer.gameObject, 0f, 1f);

        clockLabel = NewText(bar, "Clock", 16, TextAlignmentOptions.MidlineRight, FontStyles.Normal, 10f);
        SetLayout(clockLabel.gameObject, 120f);

        string[] labels = { "II", "1x", "2x", "4x" };
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var b = NewRect("Speed" + i, bar);
            speedImgs[i] = b.gameObject.AddComponent<Image>();
            speedImgs[i].color = BtnIdle;
            var btn = b.gameObject.AddComponent<Button>();
            btn.targetGraphic = speedImgs[i];
            btn.onClick.AddListener(() => { if (GameClock.Instance != null) GameClock.Instance.SetSpeed(index); });
            SetLayout(b.gameObject, 44f);

            var t = NewText(b, "Label", 15, TextAlignmentOptions.Midline, FontStyles.Bold);
            Stretch(t.rectTransform, 0f, 0f, 0f, 0f);
            t.text = labels[i];
        }
    }

    void BuildInfoPanel(Transform parent)
    {
        var rt = NewRect("InfoPanel", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(12f, 12f);
        rt.sizeDelta = new Vector2(380f, 700f);
        rt.gameObject.AddComponent<Image>().color = PanelColor;
        infoPanel = rt.gameObject;

        infoTitle = NewText(rt, "Title", 22, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 14f);
        var tr = infoTitle.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(-24f, 34f);
        tr.anchoredPosition = new Vector2(0f, -8f);

        infoBody = NewText(rt, "Body", 14, TextAlignmentOptions.TopLeft, FontStyles.Normal, 9f);
        infoBody.textWrappingMode = TextWrappingModes.Normal;
        infoBody.overflowMode = TextOverflowModes.Truncate;
        Stretch(infoBody.rectTransform, 12f, 12f, 12f, 46f);

        infoPanel.SetActive(false);
    }

    void BuildHint(Transform parent)
    {
        var t = NewText(parent, "Hint", 13, TextAlignmentOptions.BottomRight, FontStyles.Normal, 9f);
        t.color = new Color(1f, 1f, 1f, 0.45f);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-12f, 10f);
        rt.sizeDelta = new Vector2(900f, 22f);
        t.text = "LMB select   RMB order ship   MMB drag / WASD pan   Scroll zoom   P pause   1-3 speed   H home   M map   Esc deselect / pause";
    }

    // ------------------------------------------------------------------ helpers

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(Transform parent, string name, float size, TextAlignmentOptions align,
                     FontStyles style = FontStyles.Normal, float minSize = 0f)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();   // uses the default TMP font asset
        t.fontSize = size;
        t.alignment = align;
        t.fontStyle = style;
        t.color = Color.white;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        if (minSize > 0f)   // shrink to fit instead of spilling into the neighbour
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = minSize;
        }
        return t;
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static void SetLayout(GameObject go, float minWidth, float flexibleWidth = 0f)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = minWidth;
        if (flexibleWidth <= 0f) le.preferredWidth = minWidth;
        le.flexibleWidth = flexibleWidth;
    }
}