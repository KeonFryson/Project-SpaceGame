using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Right-hand build panel. Shows when an OWNED planet / moon is selected: one button per building
/// and per buildable ship (cost, build time, and the reason when it's unavailable), plus the
/// construction and ship queues. Sizes itself to its content.
/// Lives under RTSUI's canvas so it hides with the HUD. Created by RTSBootstrap after RTSUI.
/// </summary>
public class BuildPanelUI : MonoBehaviour
{
    static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.92f);
    static readonly Color BtnIdle = new Color(0.16f, 0.20f, 0.27f, 1f);
    static readonly Color ShipIdle = new Color(0.14f, 0.24f, 0.26f, 1f);
    static readonly Color BtnHover = new Color(0.23f, 0.32f, 0.45f, 1f);
    static readonly Color BtnPress = new Color(0.25f, 0.55f, 0.95f, 1f);
    static readonly Color BtnOff = new Color(0.10f, 0.12f, 0.15f, 0.8f);

    sealed class ItemRow { public Button button; public TMP_Text label; public BuildingDef building; public ShipDef ship; }
    sealed class QueueRow { public GameObject root; public TMP_Text label; public bool isShip; public int index; }

    readonly List<ItemRow> items = new List<ItemRow>();
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

        foreach (var row in items)
        {
            string reason, head, line2, effect;
            if (row.building != null)
            {
                var d = row.building;
                reason = Buildings.CanBuild(current, d);
                string max = d.maxPerBody >= 99 ? "" : "/" + d.maxPerBody;
                head = "<b>" + d.name + "</b>  <color=#888888>" + Buildings.Total(current, d.type) + max +
                       "  -  " + d.buildTime.ToString("0") + "s</color>";
                line2 = Buildings.CostText(current, d);
                effect = Buildings.EffectText(current, d);
            }
            else
            {
                var d = row.ship;
                reason = ShipDefs.CanBuild(current, d);
                head = "<b>" + d.name + "</b>  <color=#888888>ship  -  " + d.buildTime.ToString("0") + "s</color>";
                line2 = ShipDefs.CostText(current, d);
                effect = ShipDefs.StatsText(d);
            }

            row.label.text = head + "\n<size=85%>" + line2 + "</size>\n<size=85%>" +
                             (reason != null ? "<color=#ff7777>" + reason + "</color>"
                                             : "<color=#9fb4cc>" + effect + "</color>") + "</size>";
            row.button.interactable = reason == null;
            row.label.alpha = reason == null ? 1f : 0.75f;
        }

        bool anyQueue = current.queue.Count + current.shipQueue.Count > 0;
        queueHeading.text = anyQueue ? "Construction queue" : "<color=#888888>Construction queue (empty)</color>";

        foreach (var q in queueRows)
        {
            int count = q.isShip ? current.shipQueue.Count : current.queue.Count;
            bool has = q.index < count;
            if (q.root.activeSelf != has) q.root.SetActive(has);
            if (!has) continue;

            string name;
            float remaining, total;
            if (q.isShip)
            {
                var job = current.shipQueue[q.index];
                var d = ShipDefs.Get(job.cls);
                name = d.name; remaining = job.remaining; total = d.buildTime;
            }
            else
            {
                var job = current.queue[q.index];
                var d = Buildings.Def(job.type);
                name = d.name; remaining = job.remaining; total = d.buildTime;
            }
            string status = q.index == 0
                ? Mathf.RoundToInt(100f * (1f - remaining / total)) + "%  " + Mathf.CeilToInt(remaining) + "s"
                : "waiting";
            q.label.text = name + "  <color=#888888>" + status + "</color>";
        }
    }

    void OnBuildBuilding(BuildingType t)
    {
        if (current == null) return;
        Buildings.TryQueue(current, t, out _);
        Refresh();
    }

    void OnBuildShip(ShipClass c)
    {
        if (current == null) return;
        ShipDefs.TryQueue(current, c, out _);
        Refresh();
    }

    void OnCancel(bool isShip, int index)
    {
        if (current == null) return;
        if (isShip) ShipDefs.Cancel(current, index);
        else Buildings.Cancel(current, index);
        Refresh();
    }

    // ------------------------------------------------------------------ building the UI

    void Build(Transform parent)
    {
        var rt = NewRect("BuildPanel", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -100f);     // below the Home / Menu buttons
        rt.sizeDelta = new Vector2(380f, 100f);             // height is set by the layout below
        rt.gameObject.AddComponent<Image>().color = PanelColor;   // blocks clicks to the game below
        panel = rt.gameObject;

        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(10, 10, 8, 8);
        v.spacing = 2f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = NewText(rt, "Title", 20, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 12f);
        Height(title.gameObject, 28f);
        slotsLabel = NewText(rt, "Slots", 13, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        Height(slotsLabel.gameObject, 18f);

        foreach (var def in Buildings.Defs) items.Add(MakeItemRow(rt, def, null));
        foreach (var def in ShipDefs.Defs) if (def.buildable) items.Add(MakeItemRow(rt, null, def));

        queueHeading = NewText(rt, "QueueHeading", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 10f);
        Height(queueHeading.gameObject, 22f);
        for (int i = 0; i < Buildings.MaxQueue; i++) queueRows.Add(MakeQueueRow(rt, false, i));
        for (int i = 0; i < ShipDefs.MaxQueue; i++) queueRows.Add(MakeQueueRow(rt, true, i));

        panel.SetActive(false);
    }

    ItemRow MakeItemRow(Transform parent, BuildingDef bdef, ShipDef sdef)
    {
        var rt = NewRect("Item_" + (bdef != null ? bdef.name : sdef.name), parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        StyleButton(btn, bdef != null ? BtnIdle : ShipIdle);
        if (bdef != null) { var t = bdef.type; btn.onClick.AddListener(() => OnBuildBuilding(t)); }
        else { var c = sdef.cls; btn.onClick.AddListener(() => OnBuildShip(c)); }
        Height(rt.gameObject, 50f);

        var label = NewText(rt, "Label", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 8f);
        Stretch(label.rectTransform, 10f, 3f, 10f, 3f);
        return new ItemRow { button = btn, label = label, building = bdef, ship = sdef };
    }

    QueueRow MakeQueueRow(Transform parent, bool isShip, int index)
    {
        var rt = NewRect((isShip ? "ShipQueue" : "Queue") + index, parent);
        rt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        Height(rt.gameObject, 26f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 4, 2, 2);
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
        StyleButton(btn, BtnIdle);
        btn.onClick.AddListener(() => OnCancel(isShip, index));
        var le = b.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 60f;
        var t = NewText(b, "Label", 13, TextAlignmentOptions.Midline, FontStyles.Bold);
        Stretch(t.rectTransform, 0f, 0f, 0f, 0f);
        t.text = "Cancel";

        return new QueueRow { root = rt.gameObject, label = label, isShip = isShip, index = index };
    }

    static void StyleButton(Button btn, Color idle)
    {
        btn.navigation = new Navigation { mode = Navigation.Mode.None };   // keep WASD / Enter out of the UI
        var cb = btn.colors;
        cb.normalColor = idle;
        cb.highlightedColor = BtnHover;
        cb.selectedColor = idle;
        cb.pressedColor = BtnPress;
        cb.disabledColor = BtnOff;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() =>
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
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
