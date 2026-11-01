using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Info card for the system selected on the galaxy map, with a "Go to system" button.
/// Lives under RTSUI's canvas (bottom-left, same spot as the selection info panel).
/// Created by RTSBootstrap after RTSUI.
/// </summary>
public class MapCardUI : MonoBehaviour
{
    static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.92f);
    static readonly Color BtnIdle = new Color(0.16f, 0.20f, 0.27f, 1f);
    static readonly Color BtnHover = new Color(0.23f, 0.32f, 0.45f, 1f);
    static readonly Color BtnPress = new Color(0.25f, 0.55f, 0.95f, 1f);

    GalaxyManager galaxy;
    GameObject panel;
    TMP_Text title, body, goLabel;
    int shownId = -2;
    float timer;

    void Awake()
    {
        galaxy = FindFirstObjectByType<GalaxyManager>();
        var safe = transform.Find("RTSCanvas/SafeArea");
        if (safe == null)
        {
            Debug.LogError("MapCardUI: RTSUI canvas not found. Add MapCardUI AFTER RTSUI.", this);
            enabled = false;
            return;
        }
        Build(safe);
    }

    void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 0.1f;
        Refresh();
    }

    void Refresh()
    {
        bool show = galaxy != null && galaxy.MapOpen && galaxy.MapSelectedId >= 0 &&
                    galaxy.Galaxy != null && GameFlow.Mode == GameMode.Playing;
        if (panel.activeSelf != show) panel.SetActive(show);
        if (!show) { shownId = -2; return; }

        if (galaxy.MapSelectedId != shownId)
        {
            shownId = galaxy.MapSelectedId;
            Fill(shownId);
        }
    }

    void Fill(int id)
    {
        var g = galaxy.Galaxy;
        if (id < 0 || id >= g.systems.Count) return;

        var info = g.systems[id];
        var sd = SolarSystemGenerator.Generate(info.seed, info.id, info.position);

        int planets = 0, moons = 0, belts = 0;
        foreach (var b in sd.bodies)
        {
            if (b.parentIndex < 0) continue;      // the star
            if (b.isBelt) belts++;
            else if (b.parentIndex == 0) planets++;
            else moons++;
        }

        bool here = galaxy.CurrentSystem != null && galaxy.CurrentSystem.id == id;
        var gs = GameState.Instance;
        bool home = gs != null && gs.HomeSystemId == id;

        title.text = sd.name;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<color=#" + ClassHex(sd.starClass) + ">" + sd.starClass + " star</color>");
        if (home) sb.AppendLine("<color=#7dff9b>Your home system</color>");
        if (here) sb.AppendLine("<color=#7dff9b>You are here</color>");
        sb.AppendLine();
        sb.AppendLine("Planets: " + planets);
        sb.AppendLine("Moons: " + moons);
        sb.AppendLine("Asteroid belt: " + (belts > 0 ? "yes" : "no"));
        if (galaxy.CurrentSystem != null && !here)
            sb.AppendLine("Distance: " +
                          Vector2.Distance(info.position, galaxy.CurrentSystem.galaxyPosition).ToString("0") + " ly");
        sb.AppendLine("<color=#888888>Deposits and anomalies are unknown until you visit.</color>");
        body.text = sb.ToString();

        goLabel.text = here ? "Return to system" : "Go to system";
    }

    static string ClassHex(StarClass c)
    {
        switch (c)
        {
            case StarClass.RedDwarf: return "ff5a41";
            case StarClass.Orange: return "ffa54d";
            case StarClass.Yellow: return "fff28c";
            case StarClass.White: return "e6f2ff";
            default: return "8cb4ff";
        }
    }

    // ------------------------------------------------------------------ building the UI

    void Build(Transform parent)
    {
        var rt = NewRect("MapCard", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(12f, 12f);
        rt.sizeDelta = new Vector2(380f, 330f);
        rt.gameObject.AddComponent<Image>().color = PanelColor;
        panel = rt.gameObject;

        title = NewText(rt, "Title", 24, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 14f);
        var tr = title.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(-24f, 36f);
        tr.anchoredPosition = new Vector2(0f, -8f);

        body = NewText(rt, "Body", 15, TextAlignmentOptions.TopLeft, FontStyles.Normal, 10f);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Truncate;
        Stretch(body.rectTransform, 12f, 70f, 12f, 50f);

        var b = NewRect("GoButton", rt);
        b.anchorMin = b.anchorMax = b.pivot = new Vector2(0.5f, 0f);
        b.anchoredPosition = new Vector2(0f, 14f);
        b.sizeDelta = new Vector2(340f, 48f);
        var img = b.gameObject.AddComponent<Image>();
        img.color = Color.white;
        var btn = b.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = btn.colors;
        cb.normalColor = BtnIdle;
        cb.highlightedColor = BtnHover;
        cb.selectedColor = BtnIdle;
        cb.pressedColor = BtnPress;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() =>
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            if (galaxy != null) galaxy.GoToSelectedSystem();
        });

        goLabel = NewText(b, "Label", 22, TextAlignmentOptions.Midline, FontStyles.Bold);
        Stretch(goLabel.rectTransform, 0f, 0f, 0f, 0f);
        goLabel.text = "Go to system";

        panel.SetActive(false);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static TMP_Text NewText(Transform parent, string name, float size, TextAlignmentOptions align,
                            FontStyles style = FontStyles.Normal, float minSize = 0f)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = align;
        t.fontStyle = style;
        t.color = Color.white;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        if (minSize > 0f)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = minSize;
        }
        return t;
    }
}
