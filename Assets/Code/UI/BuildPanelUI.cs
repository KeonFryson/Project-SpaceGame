using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Right-hand build panel for the selected OWNED planet / moon. Drop-in replacement for the old BuildPanelUI.
///
///  - Status summary: energy, population / staffing, battery, and a stockpile with net rates per second.
///  - Tabs: Economy / Power / Colony / Ships. Ships has sub-tabs by role (Support / Combat / Capital) with
///    buildable counts, a yard requirement line, and locked tabs shown muted.
///  - One-line rows: cost chips coloured by resource (red + missing amount when short), owned count,
///    a "*" suggestion marker. Everything else is in a hover tooltip (impact preview, warnings, totals).
///  - Rows are sorted (ready, short on resources, queue full, locked). Buildings that can never work on
///    this body (no deposit, nothing grows) live in a collapsed "Unavailable here" fold.
///  - Disabled rows stay clickable: clicking explains why in a toast.
///  - Queue: only existing rows, total time, reorder waiting jobs, refund preview, x1 / x5 / Max amount,
///    "All planets" (apply to every owned body in this system), Starter pack template.
///  - Hotkeys: Tab / Shift+Tab = main tabs, Comma / Period = ship sub-tabs, Numpad 1-9 = first nine rows.
///  - Row flash when something finishes. Panel collapses to its header.
/// Lives under RTSUI's canvas. Created by RTSBootstrap after RTSUI (same as before).
/// </summary>
public class BuildPanelUI : MonoBehaviour
{
    enum Tab { Economy, Power, Colony, Ships }
    enum Kind { Ready, Short, Wait, Locked }

    sealed class ItemRow
    {
        public GameObject root;
        public Button button;
        public TMP_Text label;
        public Image flashImg;
        public BuildingDef building;
        public ShipDef ship;
        public float flash;
        // per refresh
        public string reason;
        public Kind kind;
        public bool impossible, suggested;
        public int sortKey;
    }

    sealed class QueueRow
    {
        public GameObject root;
        public TMP_Text label;
        public UITheme.Bar bar;
        public Button up, down, cancel;
        public bool isShip;
        public int index;
    }

    const float PanelW = 420f;
    const float ListMax = 360f;
    const float RowSpacing = 3f;
    const float FlashTime = 1.5f;

    static readonly int[] QtySteps = { 1, 5, 99 };
    static readonly string[] TabNames = { "Economy", "Power", "Colony", "Ships" };
    static readonly ShipRole[] Roles = { ShipRole.Support, ShipRole.Combat, ShipRole.Capital };
    static readonly string[] FilterNames = { "Show: all", "Show: affordable", "Show: hide locked" };
    static readonly BuildingType[] StarterPack =
    {
        BuildingType.ColonyHub, BuildingType.SolarCollector, BuildingType.Farm, BuildingType.MineralMine
    };
    static readonly ResourceType[] StockOrder =
    {
        ResourceType.Minerals, ResourceType.Gas, ResourceType.RareMetals,
        ResourceType.Fuel, ResourceType.Food, ResourceType.ExoticMatter
    };

    // ---- widgets
    readonly List<ItemRow> items = new List<ItemRow>();
    readonly List<QueueRow> queueRows = new List<QueueRow>();
    GameObject panel, body, tipBox;
    TMP_Text title, qtyLabel, collapseLabel, energyLabel, popLabel, batteryLabel, stockLabel;
    TMP_Text shipHeader, foldLabel, emptyNote, queueHeading, tipText;
    TMP_Text applyAllLabel, filterLabel;
    UITheme.Bar energyBar, popBar, batteryBar;
    Button[] tabBtns = new Button[4];
    TMP_Text[] tabLabels = new TMP_Text[4];
    Button[] roleBtns = new Button[3];
    TMP_Text[] roleLabels = new TMP_Text[3];
    GameObject subTabsRoot, foldBtn;
    LayoutElement listLE;
    ScrollRect scroll;

    // ---- state
    GalaxyManager galaxy;
    BodyState current;
    string currentName;
    float timer;
    Tab tab = Tab.Economy;
    int roleIdx;
    int qtyIdx;
    int filter;
    bool collapsed, foldOpen, applyAll;
    bool hasSuggest;
    BuildingType suggestType;
    string suggestWhy;
    readonly int[] shipsOwned = new int[ShipDefs.Count];
    readonly List<ItemRow> avail = new List<ItemRow>();
    readonly List<ItemRow> unavail = new List<ItemRow>();
    readonly List<ItemRow> hotkeyRows = new List<ItemRow>();
    readonly HashSet<BodyState> armed = new HashSet<BodyState>();   // Starter pack waiting for its Colony Hub

    GameObject tipOwner;
    System.Func<string> tipFunc;

    // ------------------------------------------------------------------ lifecycle

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
        EconomySystem.BuildingCompleted += OnBuilt;      // static events: removed in OnDestroy
        EconomySystem.ShipCompleted += OnShipDone;
    }

    void OnDestroy()
    {
        EconomySystem.BuildingCompleted -= OnBuilt;
        EconomySystem.ShipCompleted -= OnShipDone;
    }

    void Update()
    {
        TickFlashes();
        HandleHotkeys();

        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 0.1f;
        Refresh();
    }

    void TickFlashes()
    {
        foreach (var r in items)
        {
            if (r.flash <= 0f) continue;
            r.flash -= Time.unscaledDeltaTime;
            var c = r.flashImg.color;
            c.a = Mathf.Clamp01(r.flash / FlashTime) * 0.55f;
            r.flashImg.color = c;
        }
    }

    void HandleHotkeys()
    {
        if (current == null || panel == null || !panel.activeSelf || GameFlow.InputBlocked) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        if (kb.tabKey.wasPressedThisFrame) SetTab((Tab)(((int)tab + (shift ? 3 : 1)) % 4));
        if (tab == Tab.Ships)
        {
            if (kb.periodKey.wasPressedThisFrame) SetRole((roleIdx + 1) % 3);
            if (kb.commaKey.wasPressedThisFrame) SetRole((roleIdx + 2) % 3);
        }
        if (collapsed) return;

        for (int i = 0; i < 9 && i < hotkeyRows.Count; i++)
            if (kb[(Key)((int)Key.Numpad1 + i)].wasPressedThisFrame) OnItem(hotkeyRows[i]);
    }

    // ------------------------------------------------------------------ events

    void OnBuilt(BodyState st, BuildingType t)
    {
        // a Starter pack was waiting for this body's Hub: queue the rest now
        if (t == BuildingType.ColonyHub && armed.Remove(st)) ApplyStarter(st);

        if (st != current) return;
        foreach (var r in items)
            if (r.building != null && r.building.type == t) r.flash = FlashTime;
    }

    void OnShipDone(BodyState st, ShipClass c)
    {
        if (st != current) return;
        foreach (var r in items)
            if (r.ship != null && r.ship.cls == c) r.flash = FlashTime;
    }

    // ------------------------------------------------------------------ rules helpers

    static Tab TabOf(BuildingType t)
    {
        switch (t)
        {
            case BuildingType.MineralMine:
            case BuildingType.RareMetalMine:
            case BuildingType.Farm:
            case BuildingType.GasSkimmer:
            case BuildingType.Refinery: return Tab.Economy;
            case BuildingType.SolarCollector:
            case BuildingType.PowerPlant:
            case BuildingType.BatteryBank: return Tab.Power;
            case BuildingType.ColonyHub: return Tab.Colony;
            default: return Tab.Ships;   // Shipyard
        }
    }

    static string TabHex(Tab t)
    {
        switch (t)
        {
            case Tab.Economy: return "E0B060";
            case Tab.Power: return "FFE659";
            case Tab.Colony: return "7DFF9B";
            default: return "4DD9C8";
        }
    }

    static string RoleHex(ShipRole r) => r == ShipRole.Combat ? "F24D4D" : r == ShipRole.Capital ? "FFCC40" : "40D9C7";

    static string ShortName(ResourceType t)
    {
        switch (t)
        {
            case ResourceType.Minerals: return "Min";
            case ResourceType.RareMetals: return "Rare";
            case ResourceType.ExoticMatter: return "Exotic";
            default: return ResourceInfo.Name(t);
        }
    }

    /// <summary>Can never work on this body (as opposed to "not yet").</summary>
    static bool Impossible(BodyState st, BuildingDef d)
    {
        if (d.extractsDeposit && (st.DepositMax(d.output) <= 0f || st.Deposit(d.output) <= 0f)) return true;
        if (d.needsFertility && st.fertility <= 0f) return true;
        return false;
    }

    static string ImpossibleText(BodyState st, BuildingDef d)
    {
        if (d.extractsDeposit && st.DepositMax(d.output) <= 0f) return "No " + ResourceInfo.Name(d.output) + " deposit here";
        if (d.extractsDeposit && st.Deposit(d.output) <= 0f) return "Deposit depleted";
        return "Nothing will grow here";
    }

    static Kind Classify(string reason)
    {
        if (reason == null) return Kind.Ready;
        if (reason.Contains(" more ")) return Kind.Short;
        if (reason == "Build queue is full" || reason == "Ship queue is full") return Kind.Wait;
        return Kind.Locked;
    }

    static bool CanUse(BodyState st, BuildingType t)
    {
        var d = Buildings.Def(t);
        return !Impossible(st, d) && Buildings.Total(st, t) < d.maxPerBody;
    }

    /// <summary>Approximate net change per second of a resource on this body.</summary>
    static float NetRate(BodyState st, ResourceType t)
    {
        float mult = Colony.OutputMultiplier(st);
        float net = 0f;
        for (int i = 0; i < Buildings.Count; i++)
        {
            int n = st.buildings[i];
            if (n == 0) continue;
            var d = Buildings.Defs[i];
            float eff = Buildings.EffectiveCount(d, n);
            if (d.hasOutput && d.output == t && !(d.extractsDeposit && st.Deposit(t) <= 0f))
                net += Buildings.EffectiveRate(st, d) * eff * mult;
            if (d.hasInput && d.input == t) net -= d.inputRate * eff * mult;
            if (d.gasBurn > 0f && t == ResourceType.Gas) net -= d.gasBurn * n;
        }
        if (t == ResourceType.Food && st.Built(BuildingType.ColonyHub) > 0) net -= st.population * Colony.FoodPerPerson;
        return net;
    }

    static string Rate(float r)
    {
        if (Mathf.Abs(r) < 0.005f) return UITheme.Dim("0");
        return UITheme.Tint((r > 0f ? "+" : "") + r.ToString("0.0") + "/s", r > 0f ? UITheme.Good : UITheme.Danger);
    }

    bool Suggest(BodyState st, out BuildingType type, out string why)
    {
        type = BuildingType.ColonyHub;
        why = null;
        var gs = GameState.Instance;
        int hubs = st.Built(BuildingType.ColonyHub);

        if (hubs == 0)
        {
            why = "Needs a Colony Hub to unlock everything else";
            return CanUse(st, BuildingType.ColonyHub);
        }
        float food = NetRate(st, ResourceType.Food);
        if ((st.starving || food < -0.01f) && CanUse(st, BuildingType.Farm))
        {
            type = BuildingType.Farm;
            why = st.starving ? "Starving: output is halved" : "Food is running out";
            return true;
        }
        if (st.efficiency < 0.999f && CanUse(st, BuildingType.SolarCollector))
        {
            type = BuildingType.SolarCollector;
            why = "Brownout: energy demand exceeds supply";
            return true;
        }
        if (st.population >= hubs * Colony.HousingPerHub - 1f && CanUse(st, BuildingType.ColonyHub))
        {
            type = BuildingType.ColonyHub;
            why = "Population is at the housing cap";
            return true;
        }
        if (st.isHome && gs != null && gs.Ships.Count == 0 && st.Built(BuildingType.Shipyard) == 0 &&
            CanUse(st, BuildingType.Shipyard))
        {
            type = BuildingType.Shipyard;
            why = "Ships haul cargo and claim new planets";
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ text helpers

    static string Chip(BodyState st, ResourceType t, float amt)
    {
        float have = st.stock.Get(t);
        string s = amt.ToString("0") + " " + ShortName(t);
        if (have >= amt) return "<color=#" + ResourceInfo.Hex(t) + ">" + s + "</color>";
        return UITheme.Tint(s + " (-" + Mathf.CeilToInt(amt - have) + ")", UITheme.Danger);
    }

    static string ChipList(BodyState st, Cost[] cost, BuildingDef scaleDef, int ahead)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cost.Length; i++)
        {
            float amt = scaleDef != null ? Buildings.ScaledCost(scaleDef, cost[i].amount, ahead) : cost[i].amount;
            if (i > 0) sb.Append("  ");
            sb.Append(Chip(st, cost[i].type, amt));
        }
        return sb.ToString();
    }

    static string PlainTotals(float[] sums)
    {
        var sb = new StringBuilder();
        foreach (var t in StockOrder)
        {
            float v = sums[(int)t];
            if (v <= 0f) continue;
            if (sb.Length > 0) sb.Append("  ");
            sb.Append("<color=#").Append(ResourceInfo.Hex(t)).Append('>').Append(v.ToString("0")).Append(' ')
              .Append(ShortName(t)).Append("</color>");
        }
        return sb.ToString();
    }

    static string F(float v) => v.ToString("0.#");

    string Impact(BodyState st, BuildingDef d)
    {
        var sb = new StringBuilder();

        float dPower = d.power * (d.scalesWithSolar ? st.solar : 1f);
        float made = st.energyMade + Mathf.Max(0f, dPower);
        float used = st.energyUsed + Mathf.Max(0f, -dPower);
        if (Mathf.Abs(dPower) > 0.001f)
        {
            bool bad = used > made + 0.01f;
            string after = F(made) + " made / " + F(used) + " used";
            sb.AppendLine("Energy " + F(st.energyMade) + " / " + F(st.energyUsed) + "  ->  " +
                          (bad ? UITheme.Tint(after, UITheme.Danger) : after));
        }

        float newNeed = st.workersNeeded + d.workers;
        float newStaff = newNeed > 0f ? Mathf.Clamp01(st.population / newNeed) : 1f;
        if (d.workers > 0f)
        {
            sb.AppendLine("Workers needed " + F(st.workersNeeded) + "  ->  " + F(newNeed) + "  " +
                          UITheme.Dim("(pop " + F(st.population) + ")"));
        }

        if (d.type == BuildingType.ColonyHub)
        {
            float cap = st.Built(BuildingType.ColonyHub) * Colony.HousingPerHub;
            sb.AppendLine("Housing " + F(cap) + "  ->  " + F(cap + Colony.HousingPerHub));
        }
        if (d.batteryCapacity > 0f)
        {
            float cap = Colony.BatteryCapacity(st);
            sb.AppendLine("Battery capacity " + F(cap) + "  ->  " + F(cap + d.batteryCapacity));
        }

        if (d.hasOutput)
        {
            int n = Buildings.Total(st, d.type);
            float marginal = Buildings.EffectiveRate(st, d) *
                             (Buildings.EffectiveCount(d, n + 1) - Buildings.EffectiveCount(d, n));
            float mult = st.efficiency * newStaff * (st.starving ? Colony.StarvationOutput : 1f);
            sb.AppendLine("Next copy adds about +" + (marginal * mult).ToString("0.0") + "/s " +
                          ResourceInfo.Name(d.output) + " " + UITheme.Dim("at current power and staffing"));
            if (d.hasInput)
                sb.AppendLine("Uses about " + (d.inputRate * (Buildings.EffectiveCount(d, n + 1) - Buildings.EffectiveCount(d, n)) * mult).ToString("0.0") +
                              "/s " + ResourceInfo.Name(d.input));
            if (d.extractsDeposit)
                sb.AppendLine("Deposit left: " + st.Deposit(d.output).ToString("N0") + " " + ResourceInfo.Name(d.output));
        }

        // warnings
        if (dPower < 0f && used > made + 0.01f)
            sb.AppendLine(UITheme.Tint("[!] Energy short by " + F(used - made) + ": all production slows", UITheme.Danger));
        if (d.workers > 0f && newStaff < st.staffing - 0.001f)
            sb.AppendLine(UITheme.Tint("[!] Staffing drops to " + Mathf.RoundToInt(newStaff * 100f) + "%", UITheme.Danger));
        if (d.gasBurn > 0f && st.stock.Get(ResourceType.Gas) < d.gasBurn * 60f)
            sb.AppendLine(UITheme.Tint("[!] Low on Gas: it burns " + d.gasBurn.ToString("0.##") + "/s", UITheme.Warn));
        if (d.hasInput && st.stock.Get(d.input) < 20f)
            sb.AppendLine(UITheme.Tint("[!] Little " + ResourceInfo.Name(d.input) + " in stock to process", UITheme.Warn));
        return sb.ToString();
    }

    int RemainingSlots(BodyState st, ItemRow r) =>
        r.building != null ? Buildings.MaxQueue - st.queue.Count : ShipDefs.MaxQueue - st.shipQueue.Count;

    string BuildingTip(ItemRow r)
    {
        var st = current;
        var d = r.building;
        var sb = new StringBuilder();
        sb.AppendLine("<b>" + d.name + "</b>  " + UITheme.Dim(TabNames[(int)TabOf(d.type)]));
        sb.AppendLine(d.description);
        sb.AppendLine(UITheme.Dim("Build time ") + d.buildTime.ToString("0") + "s");
        sb.AppendLine(Buildings.EffectText(st, d));

        string impact = Impact(st, d);
        if (impact.Length > 0) { sb.AppendLine(); sb.Append(impact); }

        if (r.suggested) sb.AppendLine("\n" + UITheme.Tint("* Suggested: " + suggestWhy, UITheme.Warn));
        if (r.reason != null) sb.AppendLine("\n" + UITheme.Tint(r.reason, UITheme.Danger));

        int n = Mathf.Min(QtySteps[qtyIdx], d.maxPerBody - Buildings.Total(st, d.type), RemainingSlots(st, r));
        if (n > 1)
        {
            var sums = new float[ResourceInfo.Count];
            for (int i = 0; i < n; i++)
                foreach (var c in d.cost)
                    sums[(int)c.type] += Buildings.ScaledCost(d, c.amount, Buildings.Total(st, d.type) + i);
            sb.AppendLine("\n<b>x" + n + " total:</b> " + PlainTotals(sums));
        }
        sb.Append("\n" + UITheme.Dim("Click: queue   Shift+click: x5" + HotkeyText(r)));
        return sb.ToString();
    }

    string ShipTip(ItemRow r)
    {
        var st = current;
        var d = r.ship;
        float range = d.fuelPerUnit > 0f ? d.fuelTank / d.fuelPerUnit : 0f;
        float costSum = 0f;
        foreach (var c in d.cost) costSum += c.amount;

        var sb = new StringBuilder();
        sb.AppendLine("<b>" + d.name + "</b>  " + UITheme.Dim(d.role + "  -  " + d.sizeName));
        sb.AppendLine(UITheme.Dim("Needs: ") + (d.yard == ShipYard.Shipyard ? "Shipyard" :
                      d.yard == ShipYard.SpaceDock ? "Space Dock (planned)" : "Capital Slipway (planned)"));
        sb.AppendLine("Cargo " + d.cargo.ToString("0") + "   Fuel tank " + d.fuelTank.ToString("0") +
                      "   Speed " + d.speed.ToString("0"));
        sb.AppendLine("Range " + range.ToString("0") + " units   Burn " + d.fuelPerUnit.ToString("0.00") + " per unit");
        sb.AppendLine("Cargo per cost: " + (d.cargo / Mathf.Max(1f, costSum)).ToString("0.00") +
                      "   Cargo-range: " + (d.cargo * range).ToString("N0"));
        sb.AppendLine(UITheme.Dim("Build time ") + d.buildTime.ToString("0") + "s " +
                      UITheme.Dim("(slower during brownouts / short staffing)"));
        sb.AppendLine(UITheme.Dim("Cost: ") + ChipList(st, d.cost, null, 0));
        sb.AppendLine(UITheme.Dim("Owned: ") + shipsOwned[(int)d.cls]);

        if (r.reason != null) sb.AppendLine("\n" + UITheme.Tint(r.reason, UITheme.Danger));

        int n = Mathf.Min(QtySteps[qtyIdx], RemainingSlots(st, r));
        if (n > 1)
        {
            var sums = new float[ResourceInfo.Count];
            foreach (var c in d.cost) sums[(int)c.type] += c.amount * n;
            sb.AppendLine("\n<b>x" + n + " total:</b> " + PlainTotals(sums));
        }
        sb.Append("\n" + UITheme.Dim("Click: queue   Shift+click: x5" + HotkeyText(r)));
        return sb.ToString();
    }

    string HotkeyText(ItemRow r)
    {
        int i = hotkeyRows.IndexOf(r);
        return i >= 0 && i < 9 ? "   Numpad " + (i + 1) : "";
    }

    string RefundText(QueueRow q)
    {
        var st = current;
        if (st == null) return "";
        var sums = new float[ResourceInfo.Count];
        if (q.isShip)
        {
            if (q.index >= st.shipQueue.Count) return "";
            foreach (var c in ShipDefs.Get(st.shipQueue[q.index].cls).cost) sums[(int)c.type] += c.amount;
        }
        else
        {
            if (q.index >= st.queue.Count) return "";
            var job = st.queue[q.index];
            var d = Buildings.Def(job.type);
            int ahead = st.Built(job.type);
            for (int i = 0; i < q.index; i++) if (st.queue[i].type == job.type) ahead++;
            foreach (var c in d.cost) sums[(int)c.type] += Buildings.ScaledCost(d, c.amount, ahead);
        }
        return "<b>Cancel</b> and refund: " + PlainTotals(sums);
    }

    // ------------------------------------------------------------------ resolve / targets

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

    List<BodyState> Targets()
    {
        var list = new List<BodyState>();
        if (current == null) return list;
        list.Add(current);
        var gs = GameState.Instance;
        if (!applyAll || gs == null || galaxy == null || galaxy.CurrentSystem == null) return list;
        foreach (var st in gs.Owned)
            if (st != current && st.systemId == galaxy.CurrentSystem.id) list.Add(st);
        return list;
    }

    // ------------------------------------------------------------------ refresh

    void Refresh()
    {
        current = GameFlow.Mode == GameMode.Playing ? Resolve(out currentName) : null;
        bool show = current != null;
        if (panel.activeSelf != show) panel.SetActive(show);
        if (!show) { HideTip(); hotkeyRows.Clear(); return; }

        title.text = "BUILD  -  " + currentName;
        qtyLabel.text = QtySteps[qtyIdx] >= 99 ? "xMax" : "x" + QtySteps[qtyIdx];
        collapseLabel.text = collapsed ? "+" : "-";
        if (body.activeSelf == collapsed) body.SetActive(!collapsed);
        if (collapsed) { HideTip(); hotkeyRows.Clear(); return; }

        hasSuggest = Suggest(current, out suggestType, out suggestWhy);
        System.Array.Clear(shipsOwned, 0, shipsOwned.Length);
        var gs = GameState.Instance;
        if (gs != null)
            foreach (var s in gs.Ships)
                if (s.cls >= 0 && s.cls < shipsOwned.Length) shipsOwned[s.cls]++;

        RefreshSummary();
        RefreshTabs();
        RefreshToolbar();
        RefreshList();
        RefreshQueue();

        if (tipOwner != null)
        {
            if (!tipOwner.activeInHierarchy) HideTip();
            else tipText.text = tipFunc();
        }
    }

    void RefreshSummary()
    {
        var st = current;
        bool brown = st.efficiency < 0.999f;
        energyLabel.text = "Energy " + F(st.energyMade) + " / " + F(st.energyUsed) +
                           (brown ? "  " + UITheme.Tint("brownout " + Mathf.RoundToInt(st.efficiency * 100f) + "%", UITheme.Danger) : "");
        energyBar.Set(st.energyUsed > 0.01f ? Mathf.Clamp01(st.energyMade / st.energyUsed) : 1f,
                      brown ? UITheme.Danger : UITheme.Good);

        float housing = st.Built(BuildingType.ColonyHub) * Colony.HousingPerHub;
        popLabel.text = "Pop " + F(st.population) + " / " + F(housing) + "  " +
                        (st.starving ? UITheme.Tint("starving", UITheme.Danger)
                         : st.staffing < 0.999f ? UITheme.Tint("staff " + Mathf.RoundToInt(st.staffing * 100f) + "%", UITheme.Warn)
                         : UITheme.Dim("staffed"));
        popBar.Set(housing > 0f ? Mathf.Clamp01(st.population / housing) : 0f,
                   st.starving ? UITheme.Danger : UITheme.Accent);

        float cap = Colony.BatteryCapacity(st);
        batteryLabel.text = cap > 0f ? "Battery " + F(st.battery) + " / " + F(cap) : UITheme.Dim("Battery none");
        batteryBar.Set(cap > 0f ? Mathf.Clamp01(st.battery / cap) : 0f, ResourceInfo.Tint(ResourceType.Energy));

        var sb = new StringBuilder();
        for (int i = 0; i < StockOrder.Length; i += 2)
        {
            sb.Append(StockCell(st, StockOrder[i])).Append("<pos=50%>").Append(StockCell(st, StockOrder[i + 1]));
            if (i + 2 < StockOrder.Length) sb.Append('\n');
        }
        stockLabel.text = sb.ToString();
    }

    static string StockCell(BodyState st, ResourceType t) =>
        ResourceInfo.Coloured(t) + " " + st.stock.Get(t).ToString("N0") + " " + Rate(NetRate(st, t));

    void RefreshTabs()
    {
        for (int i = 0; i < 4; i++) UITheme.SetActive(tabBtns[i], i == (int)tab);
        tabLabels[3].text = current.shipQueue.Count > 0 ? "Ships (" + current.shipQueue.Count + ")" : "Ships";

        bool ships = tab == Tab.Ships;
        subTabsRoot.SetActive(ships);
        shipHeader.gameObject.SetActive(ships);
        if (!ships) return;

        for (int i = 0; i < 3; i++)
        {
            int ok = 0, total = 0, inQueue = 0;
            foreach (var d in ShipDefs.Defs)
            {
                if (d.role != Roles[i]) continue;
                total++;
                if (d.buildable) ok++;
            }
            foreach (var job in current.shipQueue)
                if (ShipDefs.Get(job.cls).role == Roles[i]) inQueue++;

            roleLabels[i].text = Roles[i] + " " + ok + "/" + total + (inQueue > 0 ? " *" : "");
            roleLabels[i].color = ok == 0 ? UITheme.TextDim : UITheme.TextMain;
            UITheme.SetActive(roleBtns[i], i == roleIdx);
        }

        var yards = new List<ShipYard>();
        foreach (var d in ShipDefs.Defs)
            if (d.role == Roles[roleIdx] && !yards.Contains(d.yard)) yards.Add(d.yard);

        var sb = new StringBuilder("Yards: ");
        for (int i = 0; i < yards.Count; i++)
        {
            if (i > 0) sb.Append("  -  ");
            if (yards[i] == ShipYard.Shipyard)
                sb.Append("Shipyard ").Append(current.Built(BuildingType.Shipyard) > 0
                    ? UITheme.Tint("(built)", UITheme.Good) : UITheme.Tint("(build one below first)", UITheme.Danger));
            else
                sb.Append(yards[i] == ShipYard.SpaceDock ? "Space Dock " : "Capital Slipway ").Append(UITheme.Dim("(planned)"));
        }
        shipHeader.text = sb.ToString();
    }

    void RefreshToolbar()
    {
        var targets = Targets();
        int owned = 0;
        var gs = GameState.Instance;
        if (gs != null && galaxy.CurrentSystem != null)
            foreach (var st in gs.Owned) if (st.systemId == galaxy.CurrentSystem.id) owned++;
        applyAllLabel.text = applyAll ? "All planets: ON (" + targets.Count + ")" : "All planets: OFF (" + owned + ")";
        filterLabel.text = FilterNames[filter];
    }

    void RefreshList()
    {
        avail.Clear();
        unavail.Clear();
        bool shipsTab = tab == Tab.Ships;

        foreach (var row in items)
        {
            bool visible = row.building != null
                ? TabOf(row.building.type) == tab
                : shipsTab && row.ship.role == Roles[roleIdx];
            if (!visible) { SetActive(row.root, false); continue; }

            EvaluateRow(row);
            if (row.impossible) { unavail.Add(row); continue; }
            if (filter == 1 && row.kind != Kind.Ready) { SetActive(row.root, false); continue; }
            if (filter == 2 && row.kind == Kind.Locked) { SetActive(row.root, false); continue; }
            avail.Add(row);
        }
        avail.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));
        unavail.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));

        int idx = 0;
        float h = 0f;
        foreach (var row in avail)
        {
            SetActive(row.root, true);
            Place(row.root, idx++);
            h += RowHeight(row) + RowSpacing;
        }

        bool anyUnavail = unavail.Count > 0;
        foldBtn.SetActive(anyUnavail);
        if (anyUnavail)
        {
            Place(foldBtn, idx++);
            h += 26f + RowSpacing;
            foldLabel.text = (foldOpen ? "v " : "> ") + "Unavailable here (" + unavail.Count + ")";
        }
        foreach (var row in unavail)
        {
            SetActive(row.root, foldOpen);
            if (!foldOpen) continue;
            Place(row.root, idx++);
            h += RowHeight(row) + RowSpacing;
        }

        bool empty = avail.Count == 0;
        emptyNote.gameObject.SetActive(empty);
        if (empty)
        {
            Place(emptyNote.gameObject, idx++);
            emptyNote.text = UITheme.Dim(filter == 0 ? "Nothing to build in this tab" : "Nothing matches this filter");
            h += 24f + RowSpacing;
        }

        hotkeyRows.Clear();
        hotkeyRows.AddRange(avail);

        float height = Mathf.Max(24f, Mathf.Min(h, ListMax));
        listLE.preferredHeight = listLE.minHeight = height;
    }

    static float RowHeight(ItemRow r) => r.building != null ? 32f : 48f;

    static void SetActive(GameObject go, bool on)
    {
        if (go.activeSelf != on) go.SetActive(on);
    }

    static void Place(GameObject go, int index)
    {
        if (go.transform.GetSiblingIndex() != index) go.transform.SetSiblingIndex(index);
    }

    void EvaluateRow(ItemRow r)
    {
        var st = current;
        if (r.building != null)
        {
            var d = r.building;
            r.impossible = Impossible(st, d);
            r.reason = r.impossible ? ImpossibleText(st, d) : Buildings.CanBuild(st, d);
            r.kind = Classify(r.reason);
            r.suggested = hasSuggest && d.type == suggestType && r.kind != Kind.Locked;
            r.sortKey = (int)r.kind * 100 + (int)d.type;

            int total = Buildings.Total(st, d.type);
            string max = d.maxPerBody >= 99 ? "" : "/" + d.maxPerBody;
            string left = "<color=#" + TabHex(TabOf(d.type)) + ">\u25A0</color> <b>" + d.name + "</b> " +
                          UITheme.Dim("x" + total + max) +
                          (r.suggested ? " " + UITheme.Tint("*", UITheme.Warn) : "");
            string right = r.kind == Kind.Locked ? UITheme.Tint(r.reason, UITheme.Danger)
                                                 : ChipList(st, d.cost, d, total);
            r.label.text = left + "<pos=42%>" + right;
        }
        else
        {
            var d = r.ship;
            r.impossible = false;
            r.suggested = false;
            r.reason = ShipDefs.CanBuild(st, d);
            r.kind = Classify(r.reason);
            r.sortKey = 10000 + (int)r.kind * 100 + (d.buildable ? 0 : 50) + (int)d.cls;

            int queued = 0;
            foreach (var job in st.shipQueue) if (job.cls == d.cls) queued++;
            float range = d.fuelPerUnit > 0f ? d.fuelTank / d.fuelPerUnit : 0f;
            float costSum = 0f;
            foreach (var c in d.cost) costSum += c.amount;

            string line1 = "<color=#" + RoleHex(d.role) + ">\u25A0</color> <b>" + d.name + "</b> " +
                           UITheme.Dim("(" + shipsOwned[(int)d.cls] + ")") +
                           (queued > 0 ? " " + UITheme.Tint("+" + queued + " building", UITheme.Good) : "") +
                           "<pos=42%>" + (r.kind == Kind.Locked ? UITheme.Tint(r.reason, UITheme.Danger)
                                                                  : ChipList(st, d.cost, null, 0));
            string line2 = "<size=80%>" + UITheme.Dim("Cargo ") + d.cargo.ToString("0") +
                           "<pos=20%>" + UITheme.Dim("Fuel ") + d.fuelTank.ToString("0") +
                           "<pos=40%>" + UITheme.Dim("Spd ") + d.speed.ToString("0") +
                           "<pos=57%>" + UITheme.Dim("Rng ") + range.ToString("0") +
                           "<pos=76%>" + UITheme.Dim("C/cost ") + (d.cargo / Mathf.Max(1f, costSum)).ToString("0.00") +
                           "</size>";
            r.label.text = line1 + "\n" + line2;
        }

        r.label.alpha = r.kind == Kind.Ready ? 1f : r.kind == Kind.Short ? 0.9f : r.kind == Kind.Wait ? 0.7f : 0.6f;
    }

    void RefreshQueue()
    {
        var st = current;
        float bTime = 0f, sTime = 0f;
        foreach (var j in st.queue) bTime += j.remaining;
        foreach (var j in st.shipQueue) sTime += j.remaining;
        bool any = st.queue.Count + st.shipQueue.Count > 0;
        queueHeading.text = any
            ? "CONSTRUCTION QUEUE  " + UITheme.Dim("buildings " + Mathf.CeilToInt(bTime) + "s   ships " + Mathf.CeilToInt(sTime) + "s")
            : UITheme.Dim("CONSTRUCTION QUEUE (empty)");

        foreach (var q in queueRows)
        {
            int count = q.isShip ? st.shipQueue.Count : st.queue.Count;
            bool has = q.index < count;
            SetActive(q.root, has);
            if (!has) continue;

            string name;
            float remaining, total;
            if (q.isShip)
            {
                var job = st.shipQueue[q.index];
                var d = ShipDefs.Get(job.cls);
                name = d.name; remaining = job.remaining; total = d.buildTime;
            }
            else
            {
                var job = st.queue[q.index];
                var d = Buildings.Def(job.type);
                name = d.name; remaining = job.remaining; total = d.buildTime;
            }

            float progress = q.index == 0 ? Mathf.Clamp01(1f - remaining / total) : 0f;
            string status = q.index == 0
                ? Mathf.RoundToInt(100f * progress) + "%  " + Mathf.CeilToInt(remaining) + "s"
                : "waiting";
            q.label.text = name + "  " + UITheme.Dim(status);
            q.bar.Set(progress);

            // only waiting jobs can be reordered (the front job is already in progress)
            q.up.interactable = q.index >= 2;
            q.down.interactable = q.index >= 1 && q.index < count - 1;
        }
    }

    // ------------------------------------------------------------------ actions

    void SetTab(Tab t)
    {
        tab = t;
        HideTip();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        Refresh();
    }

    void SetRole(int i)
    {
        roleIdx = i;
        HideTip();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        Refresh();
    }

    void OnCycleQty() { qtyIdx = (qtyIdx + 1) % QtySteps.Length; Refresh(); }
    void OnToggleCollapse() { collapsed = !collapsed; HideTip(); Refresh(); }
    void OnToggleFold() { foldOpen = !foldOpen; Refresh(); }
    void OnToggleApplyAll() { applyAll = !applyAll; Refresh(); }
    void OnCycleFilter() { filter = (filter + 1) % FilterNames.Length; Refresh(); }

    int QueueCount()
    {
        int n = QtySteps[qtyIdx];
        var kb = Keyboard.current;
        if (n == 1 && kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) n = 5;
        return n;
    }

    void OnItem(ItemRow r)
    {
        if (current == null) return;
        int n = QueueCount();
        int made = 0, planets = 0;
        string firstReason = null;

        foreach (var st in Targets())
        {
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                string why;
                bool ok = r.building != null
                    ? Buildings.TryQueue(st, r.building.type, out why)
                    : ShipDefs.TryQueue(st, r.ship.cls, out why);
                if (!ok)
                {
                    if (firstReason == null) firstReason = why;
                    break;
                }
                k++;
            }
            if (k > 0) { made += k; planets++; }
        }

        string name = r.building != null ? r.building.name : r.ship.name;
        if (made > 0)
            ToastUI.Show("Queued " + made + " x " + name + (planets > 1 ? " on " + planets + " planets" : ""));
        else
            ToastUI.Show(firstReason ?? "Can't build " + name + " right now");
        Refresh();
    }

    void OnStarter()
    {
        if (current == null) return;
        int queued = 0, planets = 0;
        foreach (var st in Targets())
        {
            int q = ApplyStarter(st);
            if (q > 0) { queued += q; planets++; }
        }
        bool waiting = false;
        foreach (var st in Targets()) if (armed.Contains(st)) waiting = true;

        if (queued > 0)
            ToastUI.Show("Starter pack: queued " + queued + (planets > 1 ? " on " + planets + " planets" : "") +
                         (waiting ? " (the rest follows once the Hub is built)" : ""));
        else
            ToastUI.Show(waiting ? "Starter pack armed: it continues once the Hub is built"
                                 : "Starter pack: nothing to queue right now");
        Refresh();
    }

    /// <summary>Hub, Solar Collector, Farm, Mineral Mine. Anything past the Hub waits for the Hub to finish.</summary>
    int ApplyStarter(BodyState st)
    {
        int queued = 0;
        foreach (var t in StarterPack)
        {
            if (Buildings.Total(st, t) > 0) continue;
            if (!CanUse(st, t)) continue;
            if (t != BuildingType.ColonyHub && st.Built(BuildingType.ColonyHub) == 0)
            {
                armed.Add(st);
                continue;
            }
            if (Buildings.TryQueue(st, t, out _)) queued++;
        }
        return queued;
    }

    void OnCancel(QueueRow q)
    {
        if (current == null) return;
        if (q.isShip) ShipDefs.Cancel(current, q.index);
        else Buildings.Cancel(current, q.index);
        HideTip();
        Refresh();
    }

    void OnMove(QueueRow q, int dir)
    {
        if (current == null) return;
        int i = q.index, j = i + dir;
        if (q.isShip)
        {
            var l = current.shipQueue;
            if (i < 1 || j < 1 || i >= l.Count || j >= l.Count) return;
            (l[i], l[j]) = (l[j], l[i]);
        }
        else
        {
            var l = current.queue;
            if (i < 1 || j < 1 || i >= l.Count || j >= l.Count) return;
            (l[i], l[j]) = (l[j], l[i]);
        }
        Refresh();
    }

    // ------------------------------------------------------------------ tooltip

    void Attach(GameObject go, System.Func<string> tip)
    {
        var h = go.AddComponent<BuildHoverRelay>();
        h.onHover = on =>
        {
            if (on) ShowTip(go, tip);
            else if (tipOwner == go) HideTip();
        };
    }

    void ShowTip(GameObject owner, System.Func<string> f)
    {
        string text = f();
        if (string.IsNullOrEmpty(text)) return;
        tipOwner = owner;
        tipFunc = f;
        tipText.text = text;
        tipBox.SetActive(true);
    }

    void HideTip()
    {
        tipOwner = null;
        tipFunc = null;
        if (tipBox != null && tipBox.activeSelf) tipBox.SetActive(false);
    }

    // ------------------------------------------------------------------ building the UI

    void Build(Transform parent)
    {
        var rt = UITheme.Panel(parent, "BuildPanel");
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -100f);     // below the Home / Menu buttons
        rt.sizeDelta = new Vector2(PanelW, 100f);            // height is set by the layout below
        panel = rt.gameObject;

        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(10, 10, 10, 8);
        v.spacing = 4f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // header: title, amount, collapse
        var head = UITheme.Rect("Header", rt);
        UITheme.Height(head.gameObject, 30f);
        var hh = head.gameObject.AddComponent<HorizontalLayoutGroup>();
        hh.spacing = 6f;
        hh.childAlignment = TextAnchor.MiddleLeft;
        hh.childControlWidth = hh.childControlHeight = true;
        hh.childForceExpandWidth = false;
        hh.childForceExpandHeight = true;

        title = UITheme.MakeText(head, "Title", 20, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 12f);
        title.color = UITheme.Accent;
        var tl = title.gameObject.AddComponent<LayoutElement>();
        tl.flexibleWidth = 1f;
        tl.minWidth = 100f;

        var qb = UITheme.MakeButton(head, "x1", 62f, 30f, OnCycleQty, 14f, out qtyLabel);
        Attach(qb.gameObject, () => "<b>Amount per click</b>\nx1, x5 or Max (as many as fit the queue and your stock).\nShift+click always queues x5.");
        var cb = UITheme.MakeButton(head, "-", 34f, 30f, OnToggleCollapse, 18f, out collapseLabel);
        Attach(cb.gameObject, () => collapsed ? "Expand the build panel" : "Collapse the build panel");

        body = UITheme.Rect("Body", rt).gameObject;
        var bv = body.AddComponent<VerticalLayoutGroup>();
        bv.spacing = 4f;
        bv.childControlWidth = bv.childControlHeight = true;
        bv.childForceExpandWidth = true;
        bv.childForceExpandHeight = false;
        var brt = (RectTransform)body.transform;

        // summary
        energyBar = MakeGauge(brt, out energyLabel, ResourceInfo.Tint(ResourceType.Energy));
        popBar = MakeGauge(brt, out popLabel, UITheme.Accent);
        batteryBar = MakeGauge(brt, out batteryLabel, ResourceInfo.Tint(ResourceType.Energy));
        stockLabel = UITheme.MakeText(brt, "Stock", 12, TextAlignmentOptions.TopLeft, FontStyles.Normal, 9f);
        UITheme.Height(stockLabel.gameObject, 52f);

        // main tabs
        var tabs = UITheme.Rect("Tabs", brt);
        UITheme.Height(tabs.gameObject, 32f);
        var th = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        th.spacing = 4f;
        th.childControlWidth = th.childControlHeight = true;
        th.childForceExpandWidth = th.childForceExpandHeight = true;
        for (int i = 0; i < 4; i++)
        {
            var t = (Tab)i;
            tabBtns[i] = UITheme.MakeButton(tabs, TabNames[i], 0f, 32f, () => SetTab(t), 14f, out tabLabels[i]);
        }
        Attach(tabBtns[0].gameObject, () => "Tab / Shift+Tab switches tabs");

        // ship sub-tabs
        subTabsRoot = UITheme.Rect("SubTabs", brt).gameObject;
        UITheme.Height(subTabsRoot, 28f);
        var sh = subTabsRoot.AddComponent<HorizontalLayoutGroup>();
        sh.spacing = 4f;
        sh.childControlWidth = sh.childControlHeight = true;
        sh.childForceExpandWidth = sh.childForceExpandHeight = true;
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            roleBtns[i] = UITheme.MakeButton(subTabsRoot.transform, Roles[i].ToString(), 0f, 28f, () => SetRole(idx), 13f, out roleLabels[i]);
        }
        Attach(roleBtns[0].gameObject, () => "Comma / Period switches ship tabs\nNumbers show buildable / defined ships. * = something in production.");

        shipHeader = UITheme.MakeText(brt, "ShipHeader", 12, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        UITheme.Height(shipHeader.gameObject, 20f);

        // toolbar
        var tools = UITheme.Rect("Toolbar", brt);
        UITheme.Height(tools.gameObject, 30f);
        var tb = tools.gameObject.AddComponent<HorizontalLayoutGroup>();
        tb.spacing = 4f;
        tb.childControlWidth = tb.childControlHeight = true;
        tb.childForceExpandWidth = tb.childForceExpandHeight = true;
        var starter = UITheme.MakeButton(tools, "Starter pack", 0f, 30f, OnStarter, 13f);
        Attach(starter.gameObject, () => "<b>Starter pack</b>\nQueues Colony Hub, Solar Collector, Farm and Mineral Mine (skipping what is already there or can't work here).\nOn a planet with no Hub it queues the Hub and finishes the rest when the Hub completes (not kept across save / load).");
        var all = UITheme.MakeButton(tools, "All planets", 0f, 30f, OnToggleApplyAll, 13f, out applyAllLabel);
        Attach(all.gameObject, () => "<b>All planets</b>\nWhen ON, every build click and the Starter pack apply to all planets you own in this system. Each one pays from its own stockpile.");
        var flt = UITheme.MakeButton(tools, "Show", 0f, 30f, OnCycleFilter, 13f, out filterLabel);
        Attach(flt.gameObject, () => "Cycle: show all / affordable only / hide locked");

        // scrolling list
        var scrollRt = UITheme.Rect("Scroll", brt);
        listLE = scrollRt.gameObject.AddComponent<LayoutElement>();
        listLE.preferredHeight = listLE.minHeight = 100f;
        scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        var viewport = UITheme.Rect("Viewport", scrollRt);
        UITheme.Stretch(viewport, 0f, 0f, 0f, 0f);
        var vi = viewport.gameObject.AddComponent<Image>();
        vi.color = new Color(0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UITheme.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var cv = content.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = RowSpacing;
        cv.childControlWidth = cv.childControlHeight = true;
        cv.childForceExpandWidth = true;
        cv.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;

        foreach (var def in Buildings.Defs) items.Add(MakeItemRow(content, def, null));
        foreach (var def in ShipDefs.Defs) items.Add(MakeItemRow(content, null, def));

        var fold = UITheme.MakeButton(content, "Unavailable", 0f, 26f, OnToggleFold, 13f, out foldLabel);
        foldBtn = fold.gameObject;
        Attach(foldBtn, () => "Buildings that can never work on this body (no deposit, nothing will grow).");

        emptyNote = UITheme.MakeText(content, "Empty", 13, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        UITheme.Height(emptyNote.gameObject, 24f);

        // queue
        queueHeading = UITheme.MakeText(brt, "QueueHeading", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 10f);
        queueHeading.color = UITheme.Accent;
        UITheme.Height(queueHeading.gameObject, 24f);
        for (int i = 0; i < Buildings.MaxQueue; i++) queueRows.Add(MakeQueueRow(brt, false, i));
        for (int i = 0; i < ShipDefs.MaxQueue; i++) queueRows.Add(MakeQueueRow(brt, true, i));

        BuildTooltip(parent);
        panel.SetActive(false);
    }

    void BuildTooltip(Transform parent)
    {
        var rt = UITheme.Panel(parent, "BuildTip", false, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f - PanelW - 8f, -100f);   // just left of the build panel
        rt.sizeDelta = new Vector2(380f, 60f);
        rt.GetComponent<Image>().raycastTarget = false;                 // never steals clicks or hover
        tipBox = rt.gameObject;

        var v = tipBox.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(12, 12, 10, 10);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        tipBox.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        tipText = UITheme.MakeText(rt, "Text", 14, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        tipText.textWrappingMode = TextWrappingModes.Normal;
        tipText.raycastTarget = false;
        tipBox.SetActive(false);
    }

    UITheme.Bar MakeGauge(Transform parent, out TMP_Text label, Color color)
    {
        var row = UITheme.Rect("Gauge", parent);
        UITheme.Height(row.gameObject, 18f);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        label = UITheme.MakeText(row, "Label", 13, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        var ll = label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = ll.preferredWidth = 215f;

        var bar = UITheme.MakeBar(row, "Bar", color, 8f);
        bar.root.GetComponent<LayoutElement>().flexibleWidth = 1f;
        return bar;
    }

    ItemRow MakeItemRow(Transform parent, BuildingDef bdef, ShipDef sdef)
    {
        var row = new ItemRow { building = bdef, ship = sdef };
        TMP_Text label;
        float h = bdef != null ? 32f : 48f;
        var btn = UITheme.MakeButton(parent, bdef != null ? bdef.name : sdef.name, 0f, h, () => OnItem(row), 14f, out label);

        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontStyle = FontStyles.Normal;
        UITheme.Stretch(label.rectTransform, 10f, 2f, 10f, 2f);

        // completion flash, drawn behind the label
        var fr = UITheme.Rect("Flash", btn.transform);
        UITheme.Stretch(fr, 0f, 0f, 0f, 0f);
        var fi = fr.gameObject.AddComponent<Image>();
        fi.color = new Color(0.49f, 1f, 0.61f, 0f);
        fi.raycastTarget = false;
        fr.SetSiblingIndex(0);

        row.root = btn.gameObject;
        row.button = btn;
        row.label = label;
        row.flashImg = fi;
        Attach(btn.gameObject, () => bdef != null ? BuildingTip(row) : ShipTip(row));
        return row;
    }

    QueueRow MakeQueueRow(Transform parent, bool isShip, int index)
    {
        var q = new QueueRow { isShip = isShip, index = index };

        var rt = UITheme.Rect((isShip ? "ShipQueue" : "Queue") + index, parent);
        rt.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.85f, 1f, 0.06f);
        UITheme.Height(rt.gameObject, 30f);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 4, 2, 5);
        h.spacing = 4f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        q.label = UITheme.MakeText(rt, "Label", 14, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 9f);
        var ll = q.label.gameObject.AddComponent<LayoutElement>();
        ll.minWidth = 50f;
        ll.flexibleWidth = 1f;

        q.up = UITheme.MakeButton(rt, "^", 30f, 0f, () => OnMove(q, -1), 13f);
        q.down = UITheme.MakeButton(rt, "v", 30f, 0f, () => OnMove(q, 1), 13f);
        q.cancel = UITheme.MakeButton(rt, "X", 34f, 0f, () => OnCancel(q), 13f);
        Attach(q.up.gameObject, () => "Move earlier (waiting jobs only)");
        Attach(q.down.gameObject, () => "Move later (waiting jobs only)");
        Attach(q.cancel.gameObject, () => RefundText(q));

        q.bar = UITheme.MakeOverlayBar(rt, "Progress", isShip ? UITheme.Good : UITheme.Accent, 3f);
        q.root = rt.gameObject;
        return q;
    }
}

/// <summary>Reports pointer enter / exit to the build panel's tooltip.</summary>
public class BuildHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public System.Action<bool> onHover;
    public void OnPointerEnter(PointerEventData e) => onHover?.Invoke(true);
    public void OnPointerExit(PointerEventData e) => onHover?.Invoke(false);
}
