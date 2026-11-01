using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right-hand build panel. Shows when an OWNED planet / moon is selected: one button per building
/// and per buildable ship (cost, build time, and the reason when it's unavailable), plus the
/// construction and ship queues with progress bars. Sizes itself to its content.
/// Lives under RTSUI's canvas so it hides with the HUD. Created by RTSBootstrap after RTSUI.
/// </summary>
public class BuildPanelUI : MonoBehaviour
{
    sealed class ItemRow { public Button button; public TMP_Text label; public BuildingDef building; public ShipDef ship; }
    sealed class QueueRow { public GameObject root; public TMP_Text label; public UITheme.Bar bar; public bool isShip; public int index; }

    readonly List<ItemRow> items = new List<ItemRow>();
    readonly List<QueueRow> queueRows = new List<QueueRow>();
    GameObject panel;
    TMP_Text title, slotsLabel, queueHeading;   // slotsLabel shows population / workers
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

        title.text = "BUILD  -  " + currentName;
        slotsLabel.text = "Pop " + current.population.ToString("0") + " / " +
                          (current.Built(BuildingType.ColonyHub) * Colony.HousingPerHub).ToString("0") +
                          "   Workers " + current.workersNeeded.ToString("0") +
                          (current.staffing < 0.999f
                              ? "   " + UITheme.Tint("staffing " + Mathf.RoundToInt(current.staffing * 100f) + "%", UITheme.Danger)
                              : "   " + UITheme.Dim("fully staffed"));

        foreach (var row in items)
        {
            string reason, head, line2, effect;
            if (row.building != null)
            {
                var d = row.building;
                reason = Buildings.CanBuild(current, d);
                string max = d.maxPerBody >= 99 ? "" : "/" + d.maxPerBody;
                head = "<b>" + d.name + "</b>  " + UITheme.DimTag + Buildings.Total(current, d.type) + max +
                       "  -  " + d.buildTime.ToString("0") + "s" + UITheme.End;
                line2 = Buildings.CostText(current, d);
                effect = Buildings.EffectText(current, d);
            }
            else
            {
                var d = row.ship;
                reason = ShipDefs.CanBuild(current, d);
                head = "<b>" + d.name + "</b>  " + UITheme.AccentTag + "ship" + UITheme.End + UITheme.DimTag +
                       "  -  " + d.buildTime.ToString("0") + "s" + UITheme.End;
                line2 = ShipDefs.CostText(current, d);
                effect = ShipDefs.StatsText(d);
            }

            row.label.text = head + "\n<size=85%>" + line2 + "</size>\n<size=85%>" +
                             (reason != null ? UITheme.Tint(reason, UITheme.Danger)
                                             : "<color=#9FB4CC>" + effect + "</color>") + "</size>";
            row.button.interactable = reason == null;
            row.label.alpha = reason == null ? 1f : 0.75f;
        }

        bool anyQueue = current.queue.Count + current.shipQueue.Count > 0;
        queueHeading.text = anyQueue ? "CONSTRUCTION QUEUE" : UITheme.Dim("CONSTRUCTION QUEUE (empty)");

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

            float progress = q.index == 0 ? Mathf.Clamp01(1f - remaining / total) : 0f;
            string status = q.index == 0
                ? Mathf.RoundToInt(100f * progress) + "%  " + Mathf.CeilToInt(remaining) + "s"
                : "waiting";
            q.label.text = name + "  " + UITheme.Dim(status);
            q.bar.Set(progress);
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
        var rt = UITheme.Panel(parent, "BuildPanel");
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -100f);     // below the Home / Menu buttons
        rt.sizeDelta = new Vector2(380f, 100f);             // height is set by the layout below
        panel = rt.gameObject;

        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(10, 10, 10, 8);
        v.spacing = 3f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = UITheme.MakeText(rt, "Title", 20, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 12f);
        title.color = UITheme.Accent;
        UITheme.Height(title.gameObject, 28f);
        slotsLabel = UITheme.MakeText(rt, "Workers", 13, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        UITheme.Height(slotsLabel.gameObject, 18f);

        foreach (var def in Buildings.Defs) items.Add(MakeItemRow(rt, def, null));
        foreach (var def in ShipDefs.Defs) if (def.buildable) items.Add(MakeItemRow(rt, null, def));

        queueHeading = UITheme.MakeText(rt, "QueueHeading", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 10f);
        queueHeading.color = UITheme.Accent;
        UITheme.Height(queueHeading.gameObject, 24f);
        for (int i = 0; i < Buildings.MaxQueue; i++) queueRows.Add(MakeQueueRow(rt, false, i));
        for (int i = 0; i < ShipDefs.MaxQueue; i++) queueRows.Add(MakeQueueRow(rt, true, i));

        panel.SetActive(false);
    }

    ItemRow MakeItemRow(Transform parent, BuildingDef bdef, ShipDef sdef)
    {
        TMP_Text label;
        Button btn;
        if (bdef != null)
        {
            var t = bdef.type;
            btn = UITheme.MakeButton(parent, bdef.name, 0f, 52f, () => OnBuildBuilding(t), 14f, out label);
        }
        else
        {
            var c = sdef.cls;
            btn = UITheme.MakeButton(parent, sdef.name, 0f, 52f, () => OnBuildShip(c), 14f, out label);
        }

        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontStyle = FontStyles.Normal;
        UITheme.Stretch(label.rectTransform, 10f, 3f, 10f, 3f);
        return new ItemRow { button = btn, label = label, building = bdef, ship = sdef };
    }

    QueueRow MakeQueueRow(Transform parent, bool isShip, int index)
    {
        var rt = UITheme.Rect((isShip ? "ShipQueue" : "Queue") + index, parent);
        rt.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.85f, 1f, 0.06f);
        UITheme.Height(rt.gameObject, 28f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 4, 2, 5);
        h.spacing = 6f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        var label = UITheme.MakeText(rt, "Label", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        var ll = label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = 50f;
        ll.flexibleWidth = 1f;

        UITheme.MakeButton(rt, "Cancel", 64f, 0f, () => OnCancel(isShip, index), 13f);

        var bar = UITheme.MakeOverlayBar(rt, "Progress", isShip ? UITheme.Good : UITheme.Accent, 3f);

        return new QueueRow { root = rt.gameObject, label = label, bar = bar, isShip = isShip, index = index };
    }
}
