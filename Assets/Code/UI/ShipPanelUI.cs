using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bottom-centre ship bar for the selected ship(s). Two tabs:
///  - Cargo & Orders: Stop / Refuel / Unload / Claim. With ONE ship it also shows the cargo transfer table
///    (unload all / 100, load 100 / all) when docked at one of your bodies. With SEVERAL ships it acts on the
///    whole group and lists the ships (click a row to narrow the selection to that ship).
///  - Auto Route: pick two owned planets (A and B), choose per resource whether to carry A > B, B > A or
///    balance, and switch the route on. Settings apply to every selected ship. See ShipAutomation.
/// Lives under RTSUI's canvas. Created by RTSBootstrap after RTSUI.
/// </summary>
public class ShipPanelUI : MonoBehaviour
{
    const float Step = 100f;
    const int MaxListRows = 6;
    static readonly float[] Reserves = { 0f, 100f, 500f, 1000f };

    static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.92f);
    static readonly Color BtnIdle = new Color(0.16f, 0.20f, 0.27f, 1f);
    static readonly Color BtnHover = new Color(0.23f, 0.32f, 0.45f, 1f);
    static readonly Color BtnPress = new Color(0.25f, 0.55f, 0.95f, 1f);
    static readonly Color BtnOff = new Color(0.10f, 0.12f, 0.15f, 0.8f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.45f);

    sealed class CargoRow { public GameObject root; public TMP_Text label; public ResourceType type; }
    sealed class ListRow { public GameObject root; public TMP_Text label; public Ship ship; }
    sealed class RuleRow { public GameObject root; public TMP_Text label, ruleLabel; public ResourceType type; }

    readonly List<CargoRow> cargoRows = new List<CargoRow>();
    readonly List<ListRow> listRows = new List<ListRow>();
    readonly List<RuleRow> ruleRows = new List<RuleRow>();
    readonly List<Ship> group = new List<Ship>();

    GameObject panel, manualRoot, routeRoot;
    TMP_Text title, heading, moreLabel, claimLabel;
    Button stopBtn, refuelBtn, unloadBtn, claimBtn, manualTab, routeTab;

    TMP_Text routeStatus, labelA, labelB, routeLabel, refuelLabel, reserveLabel;
    Button routeBtn;

    GalaxyManager galaxy;
    ShipManager mgr;
    Ship primary;
    BodyState dockState;     // primary's docked body (single-ship transfers)
    BodyData dockBody;
    bool routeTabActive;
    int styledTab = -1;
    float timer;

    void Awake()
    {
        galaxy = FindFirstObjectByType<GalaxyManager>();
        var safe = transform.Find("RTSCanvas/SafeArea");
        if (safe == null)
        {
            Debug.LogError("ShipPanelUI: RTSUI canvas not found. Add ShipPanelUI AFTER RTSUI.", this);
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

    // ------------------------------------------------------------------ helpers

    bool Docked(Ship s, out BodyState st, out BodyData body)
    {
        st = null;
        body = null;
        var sd = galaxy.CurrentSystem;
        var d = s.data;
        if (d.mode != ShipData.Docked || d.dockedBody < 0 || d.dockedBody >= sd.bodies.Count) return false;
        body = sd.bodies[d.dockedBody];
        st = GameState.Instance.GetBodyState(sd, d.dockedBody);
        return true;
    }

    static string RuleText(int r) => r == ShipData.RuleAToB ? "A > B" : r == ShipData.RuleBToA ? "B > A"
                                   : r == ShipData.RuleBalance ? "Balance" : "Off";

    // ------------------------------------------------------------------ refresh

    void Refresh()
    {
        var sel = SelectionManager.Instance;
        mgr = ShipManager.Instance;

        group.Clear();
        if (GameFlow.Mode == GameMode.Playing && sel != null)
            foreach (var s in sel.SelectedShips)
                if (s != null && s.data != null) group.Add(s);

        bool show = group.Count > 0 && mgr != null && galaxy != null && galaxy.CurrentSystem != null;
        if (panel.activeSelf != show) panel.SetActive(show);
        if (!show) return;

        primary = group[0];
        primary.data.EnsureAuto();
        bool multi = group.Count > 1;

        title.text = multi
            ? "<b>" + group.Count + " ships selected</b>  <color=#888888>Shift+click or drag a box to change the group</color>"
            : "<b>" + primary.data.name + "</b>  <color=#888888>" + primary.StatusText() + "</color>" +
              (primary.data.autoOn ? "  <color=#7dff9b>ROUTE</color>" : "");

        int tabNow = routeTabActive ? 1 : 0;
        if (styledTab != tabNow)
        {
            styledTab = tabNow;
            StyleTab(manualTab, !routeTabActive);
            StyleTab(routeTab, routeTabActive);
        }

        if (manualRoot.activeSelf == routeTabActive) manualRoot.SetActive(!routeTabActive);
        if (routeRoot.activeSelf != routeTabActive) routeRoot.SetActive(routeTabActive);

        if (routeTabActive) RefreshRoute(multi);
        else RefreshManual(multi);
    }

    void RefreshManual(bool multi)
    {
        int moving = 0, docked = 0, refuelable = 0, claimable = 0, unloadable = 0;
        float fuel = 0f, tank = 0f, cargo = 0f, hold = 0f;

        foreach (var s in group)
        {
            var d = s.data;
            fuel += d.fuel; tank += s.def.fuelTank;
            cargo += d.CargoTotal(); hold += s.def.cargo;
            if (d.mode == ShipData.Moving) moving++;

            if (Docked(s, out var st, out var body))
            {
                docked++;
                if (st.owned)
                {
                    if (d.fuel < s.def.fuelTank - 0.5f && st.stock.Get(ResourceType.Fuel) > 0.5f) refuelable++;
                    if (d.CargoTotal() > 0.5f) unloadable++;
                }
                else if (body.type != PlanetType.Star) claimable++;
            }
        }

        dockState = null;
        dockBody = null;
        if (!multi) Docked(primary, out dockState, out dockBody);
        bool canTransfer = !multi && dockState != null && dockState.owned;

        stopBtn.interactable = moving > 0;
        refuelBtn.interactable = refuelable > 0;
        unloadBtn.interactable = unloadable > 0;
        claimBtn.interactable = claimable > 0;

        if (multi)
            claimLabel.text = claimable > 0 ? "Claim (" + claimable + ")" : "Claim";
        else
            claimLabel.text = claimable > 0 ? "Claim " + dockBody.name
                            : dockState != null && dockState.owned ? "Owned"
                            : "Claim (dock first)";

        if (multi)
            heading.text = "Moving " + moving + "   Docked " + docked +
                           "   <color=#888888>Fuel</color> " + fuel.ToString("0") + "/" + tank.ToString("0") +
                           "   <color=#888888>Cargo</color> " + cargo.ToString("0") + "/" + hold.ToString("0");
        else
            heading.text = canTransfer
                ? "Transfer cargo  -  " + dockBody.name + " stockpile"
                : "<color=#888888>Dock at one of your planets to load, unload or refuel</color>";

        // single ship: cargo table
        foreach (var r in cargoRows)
        {
            if (r.root.activeSelf != canTransfer) r.root.SetActive(canTransfer);
            if (!canTransfer) continue;
            r.label.text = ResourceInfo.Coloured(r.type) + "   <color=#888888>ship</color> " +
                           primary.data.cargo[(int)r.type].ToString("N0") + "   <color=#888888>here</color> " +
                           dockState.stock.Get(r.type).ToString("N0");
        }

        // group: ship list
        for (int i = 0; i < listRows.Count; i++)
        {
            var row = listRows[i];
            bool has = multi && i < group.Count;
            if (row.root.activeSelf != has) row.root.SetActive(has);
            if (!has) { row.ship = null; continue; }

            var s = group[i];
            row.ship = s;
            var d = s.data;
            row.label.text = "<b>" + d.name + "</b>  <color=#888888>" + s.StatusText() + "</color>   F " +
                             d.fuel.ToString("0") + "/" + s.def.fuelTank.ToString("0") + "  C " +
                             d.CargoTotal().ToString("0") + "/" + s.def.cargo.ToString("0") +
                             (d.autoOn ? "  <color=#7dff9b>ROUTE</color>" : "");
        }
        bool more = multi && group.Count > MaxListRows;
        if (moreLabel.gameObject.activeSelf != more) moreLabel.gameObject.SetActive(more);
        if (more) moreLabel.text = "<color=#888888>+" + (group.Count - MaxListRows) + " more ships</color>";
    }

    void RefreshRoute(bool multi)
    {
        var d = primary.data;
        var sa = d.autoA >= 0 ? mgr.StateOf(d.autoA) : null;
        var sb = d.autoB >= 0 ? mgr.StateOf(d.autoB) : null;
        bool valid = d.RouteValid && sa != null && sb != null && sa.owned && sb.owned;

        int on = 0;
        foreach (var s in group) if (s.data.autoOn) on++;

        string state = on == 0 ? "<color=#888888>Route is off</color>"
                               : "<color=#7dff9b>" + primary.auto.Status + "</color>";
        routeStatus.text = multi ? "<b>" + on + " / " + group.Count + " on route</b>   " + state : state;

        labelA.text = mgr.PickingEndpoint == 0 ? "Click an owned planet..."
                    : "A: " + (d.autoA >= 0 ? mgr.BodyName(d.autoA) : "not set");
        labelB.text = mgr.PickingEndpoint == 1 ? "Click an owned planet..."
                    : "B: " + (d.autoB >= 0 ? mgr.BodyName(d.autoB) : "not set");

        routeBtn.interactable = valid || d.autoOn;
        routeLabel.text = d.autoOn ? "Route: ON" : "Route: OFF";
        refuelLabel.text = "Auto refuel: " + (d.autoRefuel ? "ON" : "OFF");
        reserveLabel.text = "Keep " + d.autoReserve.ToString("0") + " at source";

        foreach (var r in ruleRows)
        {
            int rule = d.autoRules[(int)r.type];
            r.ruleLabel.text = RuleText(rule);
            r.ruleLabel.color = rule == ShipData.RuleOff ? Dim : Color.white;
            string stock = sa != null && sb != null
                ? "   <color=#888888>A</color> " + sa.stock.Get(r.type).ToString("N0") +
                  "   <color=#888888>B</color> " + sb.stock.Get(r.type).ToString("N0")
                : "";
            r.label.text = ResourceInfo.Coloured(r.type) + stock;
        }
    }

    // ------------------------------------------------------------------ actions: orders

    void SetTab(bool route)
    {
        routeTabActive = route;
        if (!route && ShipManager.Instance != null) ShipManager.Instance.CancelPick();
        Refresh();
    }

    void OnStop()
    {
        bool hadRoute = false;
        foreach (var s in group)
            if (s.data.mode == ShipData.Moving && s.data.autoOn) hadRoute = true;
        foreach (var s in group) s.OrderStop();
        if (hadRoute) ToastUI.Show("Route cancelled");
        Refresh();
    }

    void OnRefuel()
    {
        foreach (var s in group)
            if (Docked(s, out var st, out _) && st.owned) ShipOps.Refuel(s.data, s.def, st);
        Refresh();
    }

    void OnUnloadAll()
    {
        foreach (var s in group)
        {
            if (!Docked(s, out var st, out _) || !st.owned) continue;
            foreach (var t in ResourceInfo.Physical) ShipOps.Unload(s.data, st, t, float.MaxValue);
        }
        Refresh();
    }

    void OnClaim()
    {
        var done = new HashSet<BodyState>();
        foreach (var s in group)
        {
            if (!Docked(s, out var st, out var body)) continue;
            if (st.owned || body.type == PlanetType.Star || !done.Add(st)) continue;
            GameState.Instance.Claim(st);
            ToastUI.Show("Claimed " + body.name);
        }
        Refresh();
    }

    void OnTransfer(ResourceType t, float amount, bool load)
    {
        if (primary == null || dockState == null || !dockState.owned) return;
        if (load) ShipOps.Load(primary.data, primary.def, dockState, t, amount);
        else ShipOps.Unload(primary.data, dockState, t, amount);
        Refresh();
    }

    void OnPickShip(ListRow row)
    {
        var sel = SelectionManager.Instance;
        if (row.ship != null && sel != null) sel.SelectShips(new List<Ship> { row.ship });
    }

    // ------------------------------------------------------------------ actions: route

    void OnPickEnd(int endpoint)
    {
        if (mgr != null) mgr.BeginPick(endpoint);
    }

    void OnToggleRoute()
    {
        if (primary == null || mgr == null) return;
        bool target = !primary.data.autoOn;
        if (target)
        {
            var sa = mgr.StateOf(primary.data.autoA);
            var sb = mgr.StateOf(primary.data.autoB);
            if (!primary.data.RouteValid || sa == null || sb == null || !sa.owned || !sb.owned)
            {
                ToastUI.Show("Pick two different planets you own first");
                return;
            }
        }
        mgr.UnifyRoute();
        foreach (var s in group)
        {
            s.data.autoOn = target;
            if (!target) s.auto.Reset();
        }
        Refresh();
    }

    void OnToggleRefuel()
    {
        if (primary == null) return;
        primary.data.autoRefuel = !primary.data.autoRefuel;
        if (mgr != null) mgr.UnifyRoute();
        Refresh();
    }

    void OnCycleReserve()
    {
        if (primary == null) return;
        int idx = 0;
        for (int i = 0; i < Reserves.Length; i++)
            if (Mathf.Approximately(Reserves[i], primary.data.autoReserve)) idx = i;
        primary.data.autoReserve = Reserves[(idx + 1) % Reserves.Length];
        if (mgr != null) mgr.UnifyRoute();
        Refresh();
    }

    void OnCycleRule(ResourceType t)
    {
        if (primary == null) return;
        int i = (int)t;
        primary.data.autoRules[i] = (primary.data.autoRules[i] + 1) % ShipData.RuleCount;
        if (mgr != null) mgr.UnifyRoute();
        Refresh();
    }

    // ------------------------------------------------------------------ building the UI

    void Build(Transform parent)
    {
        var rt = NewRect("ShipPanel", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 40f);
        rt.sizeDelta = new Vector2(580f, 100f);               // height set by the layout
        rt.gameObject.AddComponent<Image>().color = PanelColor;
        panel = rt.gameObject;

        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(12, 12, 8, 10);
        v.spacing = 4f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = NewText(rt, "Title", 18, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 11f);
        Height(title.gameObject, 28f);

        var tabs = NewRect("Tabs", rt);
        Height(tabs.gameObject, 34f);
        var th = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 6f;
        th.childControlWidth = th.childControlHeight = true;
        th.childForceExpandWidth = th.childForceExpandHeight = true;
        manualTab = MakeButton(tabs, "Cargo & Orders", 0f, 34f, () => SetTab(false), 15f, out _);
        routeTab = MakeButton(tabs, "Auto Route", 0f, 34f, () => SetTab(true), 15f, out _);

        manualRoot = NewColumn("Manual", rt).gameObject;
        BuildManual(manualRoot.transform);
        routeRoot = NewColumn("Route", rt).gameObject;
        BuildRoute(routeRoot.transform);

        panel.SetActive(false);
    }

    void BuildManual(Transform parent)
    {
        var actions = NewRect("Actions", parent);
        Height(actions.gameObject, 40f);
        var h = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        stopBtn = MakeButton(actions, "Stop", 0f, 40f, OnStop, 16f, out _);
        refuelBtn = MakeButton(actions, "Refuel", 0f, 40f, OnRefuel, 16f, out _);
        unloadBtn = MakeButton(actions, "Unload", 0f, 40f, OnUnloadAll, 16f, out _);
        claimBtn = MakeButton(actions, "Claim", 0f, 40f, OnClaim, 16f, out claimLabel);

        heading = NewText(parent, "Heading", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 9f);
        Height(heading.gameObject, 22f);

        foreach (var t in ResourceInfo.Physical) cargoRows.Add(MakeCargoRow(parent, t));
        for (int i = 0; i < MaxListRows; i++) listRows.Add(MakeListRow(parent, i));

        moreLabel = NewText(parent, "More", 13, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        Height(moreLabel.gameObject, 20f);
    }

    void BuildRoute(Transform parent)
    {
        routeStatus = NewText(parent, "Status", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        Height(routeStatus.gameObject, 24f);

        var ends = NewRect("Ends", parent);
        Height(ends.gameObject, 38f);
        var eh = ends.gameObject.AddComponent<HorizontalLayoutGroup>();
        eh.spacing = 8f;
        eh.childControlWidth = eh.childControlHeight = true;
        eh.childForceExpandWidth = eh.childForceExpandHeight = true;
        MakeButton(ends, "A", 0f, 38f, () => OnPickEnd(0), 15f, out labelA);
        MakeButton(ends, "B", 0f, 38f, () => OnPickEnd(1), 15f, out labelB);

        var opts = NewRect("Options", parent);
        Height(opts.gameObject, 36f);
        var oh = opts.gameObject.AddComponent<HorizontalLayoutGroup>();
        oh.spacing = 8f;
        oh.childControlWidth = oh.childControlHeight = true;
        oh.childForceExpandWidth = oh.childForceExpandHeight = true;
        routeBtn = MakeButton(opts, "Route: OFF", 0f, 36f, OnToggleRoute, 15f, out routeLabel);
        MakeButton(opts, "Auto refuel", 0f, 36f, OnToggleRefuel, 15f, out refuelLabel);
        MakeButton(opts, "Keep", 0f, 36f, OnCycleReserve, 15f, out reserveLabel);

        var hint = NewText(parent, "Hint", 12, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 8f);
        hint.text = "<color=#888888>Click a resource to cycle:  Off / A > B / B > A / Balance.  Ships refuel at each stop.</color>";
        Height(hint.gameObject, 20f);

        foreach (var t in ResourceInfo.Physical) ruleRows.Add(MakeRuleRow(parent, t));
    }

    CargoRow MakeCargoRow(Transform parent, ResourceType type)
    {
        var rt = NewRect("Row_" + type, parent);
        rt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        Height(rt.gameObject, 32f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 4, 2, 2);
        h.spacing = 4f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var label = NewText(rt, "Label", 15, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        var ll = label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = 50f;
        ll.flexibleWidth = 1f;

        MakeButton(rt, "<<", 46f, 0f, () => OnTransfer(type, float.MaxValue, false), 15f, out _);   // unload all
        MakeButton(rt, "<", 46f, 0f, () => OnTransfer(type, Step, false), 15f, out _);              // unload 100
        MakeButton(rt, ">", 46f, 0f, () => OnTransfer(type, Step, true), 15f, out _);               // load 100
        MakeButton(rt, ">>", 46f, 0f, () => OnTransfer(type, float.MaxValue, true), 15f, out _);    // load all

        return new CargoRow { root = rt.gameObject, label = label, type = type };
    }

    ListRow MakeListRow(Transform parent, int index)
    {
        var row = new ListRow();
        var btn = MakeButton(parent, "ship" + index, 0f, 30f, () => OnPickShip(row), 14f, out var label);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontStyle = FontStyles.Normal;
        Stretch(label.rectTransform, 10f, 0f, 10f, 0f);
        row.root = btn.gameObject;
        row.label = label;
        return row;
    }

    RuleRow MakeRuleRow(Transform parent, ResourceType type)
    {
        var rt = NewRect("Rule_" + type, parent);
        rt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        Height(rt.gameObject, 30f);
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

        MakeButton(rt, "Off", 110f, 0f, () => OnCycleRule(type), 14f, out var ruleLabel);
        return new RuleRow { root = rt.gameObject, label = label, ruleLabel = ruleLabel, type = type };
    }

    static void StyleTab(Button b, bool active)
    {
        var cb = b.colors;
        cb.normalColor = active ? BtnPress : BtnIdle;
        cb.selectedColor = cb.normalColor;
        b.colors = cb;
    }

    Button MakeButton(Transform parent, string text, float w, float h, UnityAction onClick, float fontSize,
                      out TMP_Text label)
    {
        var rt = NewRect("Btn_" + text, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
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
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            onClick();
        });

        var le = rt.gameObject.AddComponent<LayoutElement>();
        if (w > 0f) le.minWidth = le.preferredWidth = w;
        if (h > 0f) le.minHeight = le.preferredHeight = h;

        label = NewText(rt, "Label", fontSize, TextAlignmentOptions.Midline, FontStyles.Bold, 9f);
        Stretch(label.rectTransform, 4f, 0f, 4f, 0f);
        label.text = text;
        return btn;
    }

    // ------------------------------------------------------------------ layout helpers

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform NewColumn(string name, Transform parent)
    {
        var rt = NewRect(name, parent);
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return rt;
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
