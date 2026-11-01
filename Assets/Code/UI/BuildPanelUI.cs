using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right-hand build panel. Shows when an OWNED planet / moon is selected: one button per building
/// (cost, build time, and the reason when it is unavailable) plus the construction queue.
/// Lives under RTSUI's canvas so it hides with the HUD. Created by RTSBootstrap after RTSUI.
/// </summary>
public class BuildPanelUI : MonoBehaviour
{
    static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.92f);
    static readonly Color BtnIdle = new Color(0.16f, 0.20f, 0.27f, 1f);
    static readonly Color BtnHover = new Color(0.23f, 0.32f, 0.45f, 1f);
    static readonly Color BtnPress = new Color(0.25f, 0.55f, 0.95f, 1f);
    static readonly Color BtnOff = new Color(0.10f, 0.12f, 0.15f, 0.8f);

    sealed class BuildRow { public BuildingDef def; public Button button; public TMP_Text label; }
    sealed class QueueRow { public GameObject root; public TMP_Text label; }

    readonly List<BuildRow> buildRows = new List<BuildRow>();
    readonly List<QueueRow> queueRows = new List<QueueRow>();
    GameObject panel;
    TMP_Text title, slotsLabel, queueHeading;
    GalaxyManager galaxy;
    BodyState current;
    string currentName;
    float timer;

    void Awake()
    {
        galaxy = FindFirstObjectByType<GalaxyManager>();
        var safe = transform.Find("RTSCanvas/SafeArea");
        if (safe == null)
        {
            Debug.LogError("BuildPanelUI: RTSUI canvas not found. Add BuildPanelUI AFTER RTSUI.", this);
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

    // ------------------------------------------------------------------ refresh

    BodyState Resolve(out string bodyName)
    {
        bodyName = null;
        var sel = SelectionManager.Instance;
        var gs = GameState.Instance;
        if (sel == null || gs == null || galaxy == null || galaxy.CurrentSystem == null) return null;

        var cb = sel.Selected as CelestialBody;
        if (cb == null || cb.data.type == PlanetType.Star) return null;

        var st = gs.GetBodyState(galaxy.CurrentSystem, cb.index);
        if (!st.owned) return null;
        bodyName = cb.data.name;
        return st;
    }

    void Refresh()
    {
        current = GameFlow.Mode == GameMode.Playing ? Resolve(out currentName) : null;
        bool show = current != null;
        if (panel.activeSelf != show) panel.SetActive(show);
        if (!show) return;

        title.text = "Build  -  " + currentName;
        slotsLabel.text = "Slots: " + Buildings.SlotsUsed(current) + " / " + current.slots +
                          "   <color=#888888>(Hub uses none)</color>";

        foreach (var row in buildRows)
        {
            var d = row.def;
            string reason = Buildings.CanBuild(current, d);
            string max = d.maxPerBody >= 99 ? "" : "/" + d.maxPerBody;
            row.label.text =
                "<b>" + d.name + "</b>  <color=#888888>" + Buildings.Total(current, d.type) + max + "</color>\n" +
                "<size=80%>" + Buildings.CostText(current, d) + "  -  " + d.buildTime.ToString("0") + "s</size>\n" +
                "<size=80%>" + (reason != null
                    ? "<color=#ff7777>" + reason + "</color>"
                    : "<color=#9fb4cc>" + Buildings.EffectText(current, d) + "</color>") + "</size>";
            row.button.interactable = reason == null;
            row.label.alpha = reason == null ? 1f : 0.75f;
        }

        queueHeading.text = current.queue.Count > 0 ? "Construction queue" : "<color=#888888>Construction queue (empty)</color>";
        for (int i = 0; i < queueRows.Count; i++)
        {
            var q = queueRows[i];
            bool has = i < current.queue.Count;
            if (q.root.activeSelf != has) q.root.SetActive(has);
            if (!has) continue;

            var job = current.queue[i];
            var d = Buildings.Def(job.type);
            string status = i == 0
                ? Mathf.RoundToInt(100f * (1f - job.remaining / d.buildTime)) + "%  " + Mathf.CeilToInt(job.remaining) + "s"
                : "waiting";
            q.label.text = d.name + "  <color=#888888>" + status + "</color>";
        }
    }

    void OnBuild(BuildingType t)
    {
        if (current == null) return;
        Buildings.TryQueue(current, t, out _);
        Refresh();
    }

    void OnCancel(int index)
    {
        if (current == null) return;
        Buildings.Cancel(current, index);
        Refresh();
    }

    // ------------------------------------------------------------------ building the UI

    void Build(Transform parent)
    {
        var rt = NewRect("BuildPanel", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -100f);     // below the Home / Menu buttons
        rt.sizeDelta = new Vector2(340f, 660f);
        rt.gameObject.AddComponent<Image>().color = PanelColor;   // blocks clicks to the game below
        panel = rt.gameObject;

        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(12, 12, 10, 10);
        v.spacing = 6f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        title = NewText(rt, "Title", 20, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 12f);
        Height(title.gameObject, 30f);
        slotsLabel = NewText(rt, "Slots", 13, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        Height(slotsLabel.gameObject, 20f);

        foreach (var def in Buildings.Defs) buildRows.Add(MakeBuildRow(rt, def));

        queueHeading = NewText(rt, "QueueHeading", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 10f);
        Height(queueHeading.gameObject, 24f);
        for (int i = 0; i < Buildings.MaxQueue; i++) queueRows.Add(MakeQueueRow(rt, i));

        panel.SetActive(false);
    }

    BuildRow MakeBuildRow(Transform parent, BuildingDef def)
    {
        var rt = NewRect("Build_" + def.name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        StyleButton(btn);
        var type = def.type;
        btn.onClick.AddListener(() => OnBuild(type));
        Height(rt.gameObject, 70f);

        var label = NewText(rt, "Label", 15, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        Stretch(label.rectTransform, 10f, 4f, 10f, 4f);
        return new BuildRow { def = def, button = btn, label = label };
    }

    QueueRow MakeQueueRow(Transform parent, int index)
    {
        var rt = NewRect("Queue" + index, parent);
        rt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        Height(rt.gameObject, 34f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 4, 3, 3);
        h.spacing = 6f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var label = NewText(rt, "Label", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        var ll = label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = 50f;
        ll.flexibleWidth = 1f;

        var b = NewRect("Cancel", rt);
        var img = b.gameObject.AddComponent<Image>();
        img.color = Color.white;
        var btn = b.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        StyleButton(btn);
        int captured = index;
        btn.onClick.AddListener(() => OnCancel(captured));
        var le = b.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 56f;
        var t = NewText(b, "Label", 13, TextAlignmentOptions.Midline, FontStyles.Bold);
        Stretch(t.rectTransform, 0f, 0f, 0f, 0f);
        t.text = "Cancel";

        return new QueueRow { root = rt.gameObject, label = label };
    }

    static void StyleButton(Button btn)
    {
        btn.navigation = new Navigation { mode = Navigation.Mode.None };   // keep WASD / Enter out of the UI
        var cb = btn.colors;
        cb.normalColor = BtnIdle;
        cb.highlightedColor = BtnHover;
        cb.selectedColor = BtnIdle;
        cb.pressedColor = BtnPress;
        cb.disabledColor = BtnOff;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() =>
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        });
    }

    // ------------------------------------------------------------------ helpers

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Height(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = h;
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
