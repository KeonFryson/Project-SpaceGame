using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bottom-centre ship bar for the selected ship: Stop, Refuel, Claim, and a cargo transfer table
/// (unload all / unload 100 / load 100 / load all per resource) that appears when the ship is
/// docked at one of your bodies. Lives under RTSUI's canvas. Created by RTSBootstrap after RTSUI.
/// </summary>
public class ShipPanelUI : MonoBehaviour
{
    const float Step = 100f;

    static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.10f, 0.92f);
    static readonly Color BtnIdle = new Color(0.16f, 0.20f, 0.27f, 1f);
    static readonly Color BtnHover = new Color(0.23f, 0.32f, 0.45f, 1f);
    static readonly Color BtnPress = new Color(0.25f, 0.55f, 0.95f, 1f);
    static readonly Color BtnOff = new Color(0.10f, 0.12f, 0.15f, 0.8f);

    sealed class Row { public GameObject root; public TMP_Text label; public ResourceType type; }

    readonly List<Row> rows = new List<Row>();
    GameObject panel;
    TMP_Text title, heading, claimLabel;
    Button stopBtn, refuelBtn, claimBtn;
    GalaxyManager galaxy;
    Ship ship;
    BodyState dockState;
    BodyData dockBody;
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

    bool CanTransfer => dockState != null && dockState.owned;

    void Refresh()
    {
        var sel = SelectionManager.Instance;
        ship = GameFlow.Mode == GameMode.Playing && sel != null ? sel.Selected as Ship : null;
        bool show = ship != null && ship.data != null && galaxy != null && galaxy.CurrentSystem != null;
        if (panel.activeSelf != show) panel.SetActive(show);
        if (!show) return;

        var d = ship.data;
        var sd = galaxy.CurrentSystem;

        dockState = null;
        dockBody = null;
        if (d.mode == ShipData.Docked && d.dockedBody >= 0 && d.dockedBody < sd.bodies.Count)
        {
            dockBody = sd.bodies[d.dockedBody];
            dockState = GameState.Instance.GetBodyState(sd, d.dockedBody);
        }

        title.text = "<b>" + d.name + "</b>  <color=#888888>" + ship.StatusText() + "</color>";

        stopBtn.interactable = d.mode == ShipData.Moving;
        refuelBtn.interactable = CanTransfer && d.fuel < ship.def.fuelTank - 0.5f &&
                                 dockState.stock.Get(ResourceType.Fuel) > 0.5f;

        bool canClaim = dockState != null && !dockState.owned && dockBody.type != PlanetType.Star;
        claimBtn.interactable = canClaim;
        claimLabel.text = canClaim ? "Claim " + dockBody.name
                        : dockState != null && dockState.owned ? "Owned"
                        : "Claim (dock first)";

        heading.text = CanTransfer
            ? "Transfer cargo  -  " + dockBody.name + " stockpile"
            : "<color=#888888>Dock at one of your planets to load, unload or refuel</color>";

        foreach (var r in rows)
        {
            if (r.root.activeSelf != CanTransfer) r.root.SetActive(CanTransfer);
            if (!CanTransfer) continue;
            r.label.text = ResourceInfo.Coloured(r.type) + "   <color=#888888>ship</color> " +
                           d.cargo[(int)r.type].ToString("N0") + "   <color=#888888>here</color> " +
                           dockState.stock.Get(r.type).ToString("N0");
        }
    }

    // ------------------------------------------------------------------ actions

    void OnStop() { if (ship != null) ship.OrderStop(); }

    void OnRefuel()
    {
        if (ship == null || !CanTransfer) return;
        ShipOps.Refuel(ship.data, ship.def, dockState);
    }

    void OnClaim()
    {
        if (ship == null || dockState == null || dockState.owned) return;
        GameState.Instance.Claim(dockState);
        ToastUI.Show("Claimed " + dockBody.name);
    }

    void OnTransfer(ResourceType t, float amount, bool load)
    {
        if (ship == null || !CanTransfer) return;
        if (load) ShipOps.Load(ship.data, ship.def, dockState, t, amount);
        else ShipOps.Unload(ship.data, dockState, t, amount);
        Refresh();
    }

    // ------------------------------------------------------------------ building the UI

    void Build(Transform parent)
    {
        var rt = NewRect("ShipPanel", parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 40f);
        rt.sizeDelta = new Vector2(560f, 100f);               // height set by the layout
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

        // action row
        var actions = NewRect("Actions", rt);
        Height(actions.gameObject, 40f);
        var h = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        stopBtn = MakeButton(actions, "Stop", 0f, 40f, OnStop, 16f, out _);
        refuelBtn = MakeButton(actions, "Refuel", 0f, 40f, OnRefuel, 16f, out _);
        claimBtn = MakeButton(actions, "Claim", 0f, 40f, OnClaim, 16f, out claimLabel);

        heading = NewText(rt, "Heading", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 9f);
        Height(heading.gameObject, 22f);

        foreach (var t in ResourceInfo.Physical) rows.Add(MakeRow(rt, t));
        panel.SetActive(false);
    }

    Row MakeRow(Transform parent, ResourceType type)
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

        return new Row { root = rt.gameObject, label = label, type = type };
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
