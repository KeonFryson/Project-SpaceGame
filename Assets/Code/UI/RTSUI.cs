using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Builds the whole RTS HUD from code (no prefabs to wire), styled by UITheme:
///  - top bar: system name, the 8 resources (empire totals), game clock + speed buttons
///  - info panel (bottom-left): whatever is selected
///  - controls hint (bottom-right)
/// Scales to any screen shape (see UIFit) and respects the device safe area.
/// </summary>
public class RTSUI : MonoBehaviour
{
    TMP_Text systemLabel, clockLabel, infoTitle, infoBody;
    readonly TMP_Text[] resLabels = new TMP_Text[ResourceInfo.Count];
    readonly Button[] speedBtns = new Button[4];
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
            systemLabel.text = galaxy.CurrentSystem.name + "  " + UITheme.Dim(galaxy.CurrentSystem.starClass.ToString());

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
                              (clock.Paused ? "  " + UITheme.Tint("PAUSED", UITheme.Warn) : "");
            for (int i = 0; i < speedBtns.Length; i++)
                UITheme.SetActive(speedBtns[i], i == clock.SpeedIndex);
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
        var safe = UITheme.Rect("SafeArea", cgo.transform);
        UITheme.Stretch(safe, 0f, 0f, 0f, 0f);
        safe.gameObject.AddComponent<SafeAreaFitter>();

        BuildTopBar(safe);
        BuildInfoPanel(safe);
        BuildHint(safe);
    }

    void BuildTopBar(Transform parent)
    {
        var bar = UITheme.Panel(parent, "TopBar", false, false);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 44f);
        bar.anchoredPosition = Vector2.zero;

        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 2, 0);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Labels shrink their font instead of overflowing, so the bar always fits the 1920 reference width.
        systemLabel = UITheme.MakeText(bar, "System", 18, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 11f);
        systemLabel.color = UITheme.Accent;
        SetLayout(systemLabel.gameObject, 200f);

        for (int i = 0; i < ResourceInfo.Count; i++)
        {
            resLabels[i] = UITheme.MakeText(bar, "Res" + i, 16, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 10f);
            SetLayout(resLabels[i].gameObject, i == (int)ResourceType.ResearchData ? 150f : 135f);
        }

        var spacer = UITheme.Rect("Spacer", bar);
        SetLayout(spacer.gameObject, 0f, 1f);

        clockLabel = UITheme.MakeText(bar, "Clock", 16, TextAlignmentOptions.MidlineRight, FontStyles.Normal, 10f);
        SetLayout(clockLabel.gameObject, 150f);

        string[] labels = { "II", "1x", "2x", "4x" };
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            speedBtns[i] = UITheme.MakeButton(bar, labels[i], 44f, 0f,
                () => { if (GameClock.Instance != null) GameClock.Instance.SetSpeed(index); }, 15f);
        }
    }

    void BuildInfoPanel(Transform parent)
    {
        var rt = UITheme.Panel(parent, "InfoPanel");
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(12f, 12f);
        rt.sizeDelta = new Vector2(380f, 700f);
        infoPanel = rt.gameObject;

        infoTitle = UITheme.MakeText(rt, "Title", 22, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 14f);
        infoTitle.color = UITheme.Accent;
        var tr = infoTitle.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(-24f, 34f);
        tr.anchoredPosition = new Vector2(0f, -10f);

        infoBody = UITheme.MakeText(rt, "Body", 14, TextAlignmentOptions.TopLeft, FontStyles.Normal, 9f);
        infoBody.textWrappingMode = TextWrappingModes.Normal;
        infoBody.overflowMode = TextOverflowModes.Truncate;
        UITheme.Stretch(infoBody.rectTransform, 12f, 12f, 12f, 50f);

        infoPanel.SetActive(false);
    }

    void BuildHint(Transform parent)
    {
        var t = UITheme.MakeText(parent, "Hint", 13, TextAlignmentOptions.BottomRight, FontStyles.Normal, 9f);
        t.color = new Color(UITheme.TextDim.r, UITheme.TextDim.g, UITheme.TextDim.b, 0.8f);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-12f, 10f);
        rt.sizeDelta = new Vector2(900f, 22f);
        t.text = "LMB select   RMB order ship   MMB drag / WASD pan   Scroll zoom   P pause   1-3 speed   H home   M map   Esc deselect / pause";
    }

    static void SetLayout(GameObject go, float minWidth, float flexibleWidth = 0f)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = minWidth;
        if (flexibleWidth <= 0f) le.preferredWidth = minWidth;
        le.flexibleWidth = flexibleWidth;
    }
}
