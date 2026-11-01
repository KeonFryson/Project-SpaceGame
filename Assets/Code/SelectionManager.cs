using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Implement on future ships / stations so SelectionManager can pick them and show their info.</summary>
public interface ISelectable
{
    string SelectionTitle { get; }
    string Describe();
    float SelectionRadius { get; }
}

/// <summary>
/// Left-click selects the smallest thing under the cursor (ISelectable first, then CelestialBody).
/// Ships can be multi-selected: Shift+click toggles a ship, and dragging a box selects every ship inside
/// (hold Shift to add to the current selection). Click empty space or press Esc to deselect. H = home.
/// Selected is the primary item (the first ship of a group); SelectedShips is the whole ship group.
/// A highlight ring follows every selected ship.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    const float HomeZoom = 20f;
    const float DragThreshold = 8f;   // pixels before a click becomes a box drag

    public Component Selected { get; private set; }
    public IReadOnlyList<Ship> SelectedShips => shipGroup;
    public event System.Action SelectionChanged;

    sealed class RingView
    {
        public LineRenderer lr;
        public float radius = -1f;
    }

    readonly List<Ship> shipGroup = new List<Ship>();

    GalaxyManager galaxy;
    SolarSystemSpawner spawner;
    TopDownCameraController camCtl;
    Camera cam;

    RingView mainRing;
    readonly List<RingView> extraRings = new List<RingView>();
    Material ringMat;
    bool hasSelection;
    bool pendingHomeFocus;

    // box select
    bool wasDown, pressing, dragging;
    Vector2 dragStart, dragNow;
    Texture2D boxTex;

    void Awake()
    {
        Instance = this;
        galaxy = FindFirstObjectByType<GalaxyManager>();
        spawner = FindFirstObjectByType<SolarSystemSpawner>();
        camCtl = FindFirstObjectByType<TopDownCameraController>();

        // Subscribe in Awake: GalaxyManager.Start raises SystemLoaded during its own Start.
        if (galaxy != null) galaxy.SystemLoaded += OnSystemLoaded;
        if (GameState.Instance != null) GameState.Instance.NewGameStarted += OnNewGame;
        BuildRings();
    }

    void OnDestroy()
    {
        if (galaxy != null) galaxy.SystemLoaded -= OnSystemLoaded;
        if (GameState.Instance != null) GameState.Instance.NewGameStarted -= OnNewGame;
        if (ringMat != null) Destroy(ringMat);
        if (boxTex != null) Destroy(boxTex);
    }

    void OnNewGame() => pendingHomeFocus = true;

    void OnSystemLoaded(SolarSystemData sd)
    {
        Select(null);   // old bodies and ships were destroyed
        var gs = GameState.Instance;
        if (pendingHomeFocus && gs != null && gs.HomeSystemId == sd.id)
        {
            pendingHomeFocus = false;
            FocusHomeBody();
        }
    }

    // ------------------------------------------------------------------ input

    void Update()
    {
        PruneDestroyed();
        if (GameFlow.InputBlocked) { CancelDrag(); return; }   // title / menus / pause
        if (GameFlow.Mode != GameMode.Playing) { Select(null); return; }

        var rts = GameInput.Actions.RTS;
        if (rts.Deselect.WasPressedThisFrame()) Select(null);
        if (rts.ToggleMap.WasPressedThisFrame()) Select(null);
        if (rts.FocusHome.WasPressedThisFrame()) FocusHome();

        // the ship panel is waiting for a click on a planet: leave that click alone
        var sm = ShipManager.Instance;
        if (sm != null && sm.IsPicking) { CancelDrag(); wasDown = rts.Select.IsPressed(); return; }

        HandleMouse(rts);
    }

    void HandleMouse(InputSystem_Actions.RTSActions rts)
    {
        bool down = rts.Select.IsPressed();
        bool pressed = rts.Select.WasPressedThisFrame();
        bool released = wasDown && !down;
        wasDown = down;

        Vector2 screen = rts.Point.ReadValue<Vector2>();

        if (pressed)
        {
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            pressing = !overUI;
            dragging = false;
            dragStart = dragNow = screen;
        }
        if (!pressing) return;

        dragNow = screen;
        if (!dragging && (dragNow - dragStart).sqrMagnitude > DragThreshold * DragThreshold) dragging = true;
        if (!released) return;

        pressing = false;
        bool additive = Keyboard.current != null &&
                        (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        if (cam == null) cam = Camera.main;
        if (cam == null) { dragging = false; return; }

        if (dragging)
        {
            dragging = false;
            BoxSelect(additive);
        }
        else
        {
            Component picked = Pick(cam.ScreenToWorldPoint(screen));
            if (picked is Ship ship && additive) ToggleShip(ship);
            else if (picked == null && additive) { /* shift-click on empty space keeps the selection */ }
            else Select(picked);
        }
    }

    void CancelDrag()
    {
        pressing = false;
        dragging = false;
    }

    void BoxSelect(bool additive)
    {
        var sm = ShipManager.Instance;
        if (sm == null) return;

        var r = Rect.MinMaxRect(Mathf.Min(dragStart.x, dragNow.x), Mathf.Min(dragStart.y, dragNow.y),
                                Mathf.Max(dragStart.x, dragNow.x), Mathf.Max(dragStart.y, dragNow.y));

        var list = new List<Ship>();
        if (additive) list.AddRange(shipGroup);
        foreach (var s in sm.Ships)
        {
            if (s == null || list.Contains(s)) continue;
            Vector3 sp = cam.WorldToScreenPoint(s.transform.position);
            if (r.Contains(new Vector2(sp.x, sp.y))) list.Add(s);
        }

        if (list.Count == 0) { if (!additive) Select(null); return; }
        SelectShips(list);
    }

    Component Pick(Vector2 world)
    {
        Component best = null;
        float bestScore = float.MaxValue;

        foreach (var hit in Physics2D.OverlapPointAll(world))
        {
            var sel = hit.GetComponentInParent<ISelectable>();
            if (sel is Component sc)
            {
                float score = sel.SelectionRadius - 1000f;   // units/ships always win over bodies
                if (score < bestScore) { bestScore = score; best = sc; }
                continue;
            }

            var cb = hit.GetComponentInParent<CelestialBody>();
            if (cb != null)
            {
                float score = cb.data.diameter * 0.5f;       // smallest body wins (moon over its planet)
                if (score < bestScore) { bestScore = score; best = cb; }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ selection API

    /// <summary>Selects one thing (or nothing). Selecting a ship makes it a group of one.</summary>
    public void Select(Component c)
    {
        shipGroup.Clear();
        if (c is Ship s) shipGroup.Add(s);
        Apply(c);
    }

    /// <summary>Selects several ships. The first one becomes the primary.</summary>
    public void SelectShips(IList<Ship> list)
    {
        var copy = new List<Ship>();
        if (list != null) foreach (var s in list) if (s != null) copy.Add(s);   // list may alias shipGroup
        shipGroup.Clear();
        shipGroup.AddRange(copy);
        Apply(copy.Count > 0 ? copy[0] : null);
    }

    public void ToggleShip(Ship ship)
    {
        if (ship == null) return;
        if (!shipGroup.Remove(ship)) shipGroup.Add(ship);
        Apply(shipGroup.Count > 0 ? shipGroup[0] : null);
    }

    void Apply(Component primary)
    {
        Selected = primary;
        hasSelection = primary != null;
        mainRing.lr.gameObject.SetActive(hasSelection);
        if (hasSelection) SetRingRadius(mainRing, RadiusOf(primary));
        SyncExtraRings();
        SelectionChanged?.Invoke();
    }

    void PruneDestroyed()
    {
        if (!hasSelection) return;

        bool changed = false;
        for (int i = shipGroup.Count - 1; i >= 0; i--)
            if (shipGroup[i] == null) { shipGroup.RemoveAt(i); changed = true; }

        if (Selected == null)
        {
            if (shipGroup.Count > 0) Apply(shipGroup[0]);
            else Select(null);
        }
        else if (changed)
        {
            SyncExtraRings();
            SelectionChanged?.Invoke();
        }
    }

    // ------------------------------------------------------------------ home

    public void FocusHome()
    {
        var gs = GameState.Instance;
        if (gs == null || galaxy == null || gs.HomeSystemId < 0) return;

        // The galaxy map hides the system and zooms way out, so leave it first.
        if (galaxy.MapOpen) galaxy.CloseMap();

        if (galaxy.CurrentSystem != null && galaxy.CurrentSystem.id == gs.HomeSystemId)
        {
            FocusHomeBody();
        }
        else
        {
            pendingHomeFocus = true;
            galaxy.LoadSystem(gs.HomeSystemId);   // OnSystemLoaded finishes the job
        }
    }

    void FocusHomeBody()
    {
        var gs = GameState.Instance;
        if (spawner == null || gs == null) return;
        foreach (var cb in spawner.Bodies)
        {
            if (cb.index != gs.HomeBodyIndex) continue;
            Select(cb);
            if (camCtl != null)
            {
                camCtl.FocusOn(cb.transform.position);
                camCtl.ZoomTo(Mathf.Min(camCtl.OrthoSize, HomeZoom));   // never stay zoomed far out
            }
            return;
        }
    }

    // ------------------------------------------------------------------ highlight rings

    RingView MakeRing(string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = ringMat;
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = 64;
        lr.startColor = lr.endColor = color;
        lr.sortingOrder = 5000;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        go.SetActive(false);
        return new RingView { lr = lr };
    }

    void BuildRings()
    {
        ringMat = new Material(Shader.Find("Sprites/Default"));
        mainRing = MakeRing("SelectionRing", new Color(0.35f, 0.9f, 1f, 0.95f));
    }

    /// <summary>Makes sure there is one extra ring for each ship after the first.</summary>
    void SyncExtraRings()
    {
        int need = Mathf.Max(0, shipGroup.Count - 1);
        while (extraRings.Count < need)
            extraRings.Add(MakeRing("GroupRing" + extraRings.Count, new Color(0.35f, 0.9f, 1f, 0.6f)));
        for (int i = 0; i < extraRings.Count; i++)
        {
            bool on = i < need && hasSelection;
            if (extraRings[i].lr.gameObject.activeSelf != on) extraRings[i].lr.gameObject.SetActive(on);
        }
    }

    static void SetRingRadius(RingView rv, float r)
    {
        if (Mathf.Abs(rv.radius - r) < r * 0.005f) return;   // ships rescale with zoom: only redraw on real change
        rv.radius = r;
        int n = rv.lr.positionCount;
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            rv.lr.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r);
        }
    }

    static float RadiusOf(Component c)
    {
        if (c is ISelectable s) return Mathf.Max(s.SelectionRadius, 0.5f);
        if (c is CelestialBody cb) return Mathf.Max(cb.data.diameter * 0.5f + 0.6f, 1f);
        return 1f;
    }

    void LateUpdate()
    {
        // No selection ring behind the title screen / main menu.
        bool inMenu = GameFlow.Mode == GameMode.Title || GameFlow.Mode == GameMode.MainMenu;
        bool show = hasSelection && Selected != null && !inMenu;

        if (mainRing.lr.gameObject.activeSelf != show) mainRing.lr.gameObject.SetActive(show);
        if (!show)
        {
            foreach (var e in extraRings)
                if (e.lr.gameObject.activeSelf) e.lr.gameObject.SetActive(false);
            return;
        }

        if (cam == null) cam = Camera.main;
        float ortho = cam != null ? cam.orthographicSize : 10f;
        float width = Mathf.Max(0.08f, ortho * 0.006f);   // constant on-screen thickness

        UpdateRing(mainRing, Selected, width);
        SyncExtraRings();
        for (int i = 0; i < extraRings.Count; i++)
        {
            if (i + 1 >= shipGroup.Count) break;
            var s = shipGroup[i + 1];
            if (s == null) { extraRings[i].lr.gameObject.SetActive(false); continue; }
            UpdateRing(extraRings[i], s, width);
        }
    }

    static void UpdateRing(RingView rv, Component target, float width)
    {
        rv.lr.transform.position = target.transform.position;
        rv.lr.widthMultiplier = width;
        SetRingRadius(rv, RadiusOf(target));
    }

    // ------------------------------------------------------------------ drag box

    void OnGUI()
    {
        if (!dragging || Event.current.type != EventType.Repaint) return;

        float x0 = Mathf.Min(dragStart.x, dragNow.x), x1 = Mathf.Max(dragStart.x, dragNow.x);
        float y0 = Screen.height - Mathf.Max(dragStart.y, dragNow.y), y1 = Screen.height - Mathf.Min(dragStart.y, dragNow.y);
        var r = new Rect(x0, y0, x1 - x0, y1 - y0);

        if (boxTex == null)
        {
            boxTex = new Texture2D(1, 1);
            boxTex.SetPixel(0, 0, Color.white);
            boxTex.Apply();
        }

        var old = GUI.color;
        GUI.color = new Color(0.35f, 0.9f, 1f, 0.15f);
        GUI.DrawTexture(r, boxTex);
        GUI.color = new Color(0.35f, 0.9f, 1f, 0.9f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), boxTex);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), boxTex);
        GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), boxTex);
        GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), boxTex);
        GUI.color = old;
    }

    // ------------------------------------------------------------------ info text

    /// <summary>Title + rich-text body for the info panel. False when nothing is selected.</summary>
    public bool GetInfo(out string title, out string body)
    {
        title = body = null;
        if (!hasSelection || Selected == null) return false;

        if (shipGroup.Count > 1)
        {
            title = shipGroup.Count + " ships selected";
            body = DescribeGroup(shipGroup);
            return true;
        }
        if (Selected is ISelectable s)
        {
            title = s.SelectionTitle;
            body = s.Describe();
            return true;
        }
        if (Selected is CelestialBody cb && galaxy != null && galaxy.CurrentSystem != null)
        {
            title = cb.data.name;
            body = DescribeBody(galaxy.CurrentSystem, cb);
            return true;
        }
        return false;
    }

    static string DescribeGroup(IReadOnlyList<Ship> group)
    {
        int moving = 0, docked = 0, idle = 0, routes = 0;
        float fuel = 0f, tank = 0f, cargo = 0f, hold = 0f;
        var totals = new float[ResourceInfo.Count];
        var classes = new Dictionary<string, int>();

        foreach (var s in group)
        {
            if (s == null || s.data == null) continue;
            var d = s.data;
            if (d.mode == ShipData.Moving) moving++;
            else if (d.mode == ShipData.Docked) docked++;
            else idle++;
            if (d.autoOn) routes++;

            fuel += d.fuel; tank += s.def.fuelTank;
            cargo += d.CargoTotal(); hold += s.def.cargo;
            foreach (var t in ResourceInfo.Physical) totals[(int)t] += d.cargo[(int)t];

            classes.TryGetValue(s.def.name, out int n);
            classes[s.def.name] = n + 1;
        }

        var sb = new StringBuilder();
        foreach (var kv in classes) sb.AppendLine(kv.Key + " x" + kv.Value);
        sb.AppendLine();
        sb.AppendLine("Moving " + moving + "   Docked " + docked + "   Idle " + idle);
        sb.AppendLine("On auto route: " + routes);
        sb.AppendLine("Fuel: " + fuel.ToString("0") + " / " + tank.ToString("0"));
        sb.AppendLine("Cargo: " + cargo.ToString("0") + " / " + hold.ToString("0"));
        foreach (var t in ResourceInfo.Physical)
            if (totals[(int)t] > 0.5f)
                sb.AppendLine("  " + ResourceInfo.Coloured(t) + "  " + totals[(int)t].ToString("N0"));
        sb.AppendLine("\n<color=#888888>Right-click: move in formation, or dock all\nUse the ship panel for routes</color>");
        return sb.ToString();
    }

    static string TypeName(PlanetType t)
    {
        switch (t)
        {
            case PlanetType.RiverWorld: return "River world";
            case PlanetType.GasGiant: return "Gas giant";
            case PlanetType.RingedGiant: return "Ringed gas giant";
            case PlanetType.Barren: return "Barren world";
            case PlanetType.Asteroid: return "Asteroid";
            default: return t + " world";
        }
    }

    static string Heading(string s) => "\n<color=#8fb4ff><b>" + s + "</b></color>\n";

    static string DescribeBody(SolarSystemData sd, CelestialBody cb)
    {
        var d = cb.data;
        var sb = new StringBuilder();

        if (d.type == PlanetType.Star)
        {
            sb.AppendLine(sd.starClass + " star");
            sb.AppendLine("Diameter: " + d.diameter.ToString("0.0"));
            sb.AppendLine("Bodies in system: " + (sd.bodies.Count - 1));

            var a = ResourceGenerator.RollAnomaly(sd);
            if (a.present)
                sb.AppendLine("\n<color=#ff73d9>Anomaly detected:</color> " + a.name +
                              "\n(on " + sd.bodies[a.bodyIndex].name + ")");
            return sb.ToString();
        }

        sb.AppendLine(TypeName(d.type));
        if (d.parentIndex > 0) sb.AppendLine("Moon of " + sd.bodies[d.parentIndex].name);
        sb.AppendLine("Diameter: " + d.diameter.ToString("0.0") + "   Orbit: " + d.orbitRadius.ToString("0") +
                      (d.parentIndex > 0 ? " (from parent)" : ""));

        var gs = GameState.Instance;
        if (gs == null) return sb.ToString();
        var st = gs.GetBodyState(sd, cb.index);

        sb.AppendLine(st.isHome ? "<color=#7dff9b>Owned - home planet</color>"
                      : st.owned ? "<color=#7dff9b>Owned</color>" : "<color=#aaaaaa>Unclaimed</color>");
        if (st.anomalyName != null)
            sb.AppendLine("<color=#ff73d9>Anomaly: " + st.anomalyName + "</color>");

        sb.Append(Heading("Deposits"));
        bool any = false;
        foreach (var t in ResourceInfo.Deposited)
        {
            float max = st.DepositMax(t);
            if (max <= 0f) continue;
            any = true;
            sb.AppendLine(ResourceInfo.Coloured(t) + "  " + st.Deposit(t).ToString("N0") + " / " + max.ToString("N0"));
        }
        if (!any) sb.AppendLine("<color=#888888>None</color>");

        if (st.fertility > 0f)
        {
            string label = st.fertility >= 0.9f ? "High" : st.fertility >= 0.35f ? "Medium" : "Low";
            sb.AppendLine(ResourceInfo.Coloured(ResourceType.Food) + " potential: " + label +
                          " (" + Mathf.RoundToInt(st.fertility * 100f) + "%)");
        }

        if (st.owned)
        {
            sb.Append(Heading("Stockpile"));
            foreach (var t in ResourceInfo.Physical)
                sb.AppendLine(ResourceInfo.Coloured(t) + "  " + st.stock.Get(t).ToString("N0"));
            sb.AppendLine("<color=#888888>Capacity per resource: " + st.stock.capacity.ToString("N0") + "</color>");

            sb.Append(Heading("Power & Population"));
            sb.Append(Colony.Describe(st));
            sb.Append(Heading("Buildings"));
            sb.Append(Buildings.Describe(st));
        }
        return sb.ToString();
    }
}
