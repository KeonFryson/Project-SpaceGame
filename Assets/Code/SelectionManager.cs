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
/// Drag a box to select several ships, Shift adds to the selection (click or box).
/// Click empty space or press Esc to deselect. H = jump to the home planet.
/// Selected = the "primary" selection (first ship of a group); SelectedShips = the whole group.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    const float HomeZoom = 20f;
    const float DragThreshold = 6f;     // pixels before a click becomes a box
    const int RingPoints = 64;

    public Component Selected { get; private set; }
    public readonly List<Ship> SelectedShips = new List<Ship>();
    public event System.Action SelectionChanged;

    GalaxyManager galaxy;
    SolarSystemSpawner spawner;
    TopDownCameraController camCtl;
    Camera cam;

    LineRenderer ring;
    readonly List<LineRenderer> extraRings = new List<LineRenderer>();
    readonly Vector3[] ringBuf = new Vector3[RingPoints];
    Material ringMat;
    bool hasSelection;
    bool pendingHomeFocus;

    // drag-box state
    bool pressing, boxActive;
    Vector2 dragStart;

    void Awake()
    {
        Instance = this;
        galaxy = FindFirstObjectByType<GalaxyManager>();
        spawner = FindFirstObjectByType<SolarSystemSpawner>();
        camCtl = FindFirstObjectByType<TopDownCameraController>();

        // Subscribe in Awake: GalaxyManager.Start raises SystemLoaded during its own Start.
        if (galaxy != null) galaxy.SystemLoaded += OnSystemLoaded;
        if (GameState.Instance != null) GameState.Instance.NewGameStarted += OnNewGame;
        BuildRing();
    }

    void OnDestroy()
    {
        if (galaxy != null) galaxy.SystemLoaded -= OnSystemLoaded;
        if (GameState.Instance != null) GameState.Instance.NewGameStarted -= OnNewGame;
        if (ringMat != null) Destroy(ringMat);
    }

    void OnNewGame() => pendingHomeFocus = true;

    void OnSystemLoaded(SolarSystemData sd)
    {
        Select(null);   // old bodies / ships were destroyed
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
        PruneShips();
        if (GameFlow.InputBlocked) { CancelDrag(); return; }               // title / menus / pause
        if (GameFlow.Mode != GameMode.Playing) { CancelDrag(); Select(null); return; }   // only in RTS mode

        var rts = GameInput.Actions.RTS;
        if (rts.Deselect.WasPressedThisFrame()) { CancelDrag(); Select(null); }
        if (rts.ToggleMap.WasPressedThisFrame()) { CancelDrag(); Select(null); }
        if (rts.FocusHome.WasPressedThisFrame()) FocusHome();

        HandlePointer(rts);
    }

    void HandlePointer(InputSystem_Actions.RTSActions rts)
    {
        Vector2 pos = rts.Point.ReadValue<Vector2>();

        if (rts.Select.WasPressedThisFrame())
        {
            pressing = !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
            boxActive = false;
            dragStart = pos;
        }
        if (!pressing) return;

        if (!boxActive && rts.Select.IsPressed() &&
            (pos - dragStart).sqrMagnitude > DragThreshold * DragThreshold)
            boxActive = true;

        if (rts.Select.WasReleasedThisFrame())
        {
            bool shift = ShiftHeld();
            bool wasBox = boxActive;
            CancelDrag();
            if (wasBox) BoxSelect(dragStart, pos, shift);
            else ClickSelect(pos, shift);
        }
    }

    void CancelDrag() { pressing = false; boxActive = false; }

    static bool ShiftHeld()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
    }

    void ClickSelect(Vector2 screenPos, bool shift)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector2 world = cam.ScreenToWorldPoint(screenPos);
        var pick = Pick(world);

        if (shift)
        {
            if (pick is Ship s) ToggleShip(s);   // shift-click on anything else does nothing
            return;
        }
        Select(pick);
    }

    void BoxSelect(Vector2 a, Vector2 b, bool additive)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                   Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        var found = new List<Ship>();
        foreach (var s in FindObjectsByType<Ship>(FindObjectsSortMode.None))
        {
            Vector3 sp = cam.WorldToScreenPoint(s.transform.position);
            if (sp.z >= 0f && rect.Contains(new Vector2(sp.x, sp.y))) found.Add(s);
        }

        if (found.Count == 0)
        {
            if (!additive) Select(null);
            return;
        }
        SelectShips(found, additive);
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

    /// <summary>Selects one thing (or nothing). Clears any ship group.</summary>
    public void Select(Component c)
    {
        SelectedShips.Clear();
        if (c is Ship s) SelectedShips.Add(s);
        SetPrimary(c);
    }

    /// <summary>Selects a group of ships. additive = keep the ships already selected.</summary>
    public void SelectShips(List<Ship> ships, bool additive)
    {
        if (!additive) SelectedShips.Clear();
        foreach (var s in ships)
            if (s != null && !SelectedShips.Contains(s)) SelectedShips.Add(s);
        SetPrimary(SelectedShips.Count > 0 ? SelectedShips[0] : null);
    }

    public void ToggleShip(Ship s)
    {
        if (s == null) return;
        if (!SelectedShips.Remove(s)) SelectedShips.Add(s);
        SetPrimary(SelectedShips.Count > 0 ? SelectedShips[0] : null);
    }

    void SetPrimary(Component c)
    {
        Selected = c;
        hasSelection = c != null;
        ring.gameObject.SetActive(hasSelection);
        if (hasSelection) SetRingRadius(ring, RadiusOf(c));
        SelectionChanged?.Invoke();
    }

    void PruneShips()
    {
        bool removed = SelectedShips.RemoveAll(s => s == null) > 0;
        if (!hasSelection) return;

        if (Selected == null)   // primary was destroyed
        {
            SetPrimary(SelectedShips.Count > 0 ? SelectedShips[0] : null);
        }
        else if (removed)
        {
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

    static readonly Color RingColor = new Color(0.35f, 0.9f, 1f, 0.95f);

    LineRenderer MakeRing(string name)
    {
        if (ringMat == null) ringMat = new Material(Shader.Find("Sprites/Default"));
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = ringMat;
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = RingPoints;
        lr.startColor = lr.endColor = RingColor;
        lr.sortingOrder = 5000;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        go.SetActive(false);
        return lr;
    }

    void BuildRing() => ring = MakeRing("SelectionRing");

    void SetRingRadius(LineRenderer lr, float r)
    {
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i / (float)RingPoints * Mathf.PI * 2f;
            ringBuf[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r;
        }
        lr.SetPositions(ringBuf);
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

        if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);

        int used = 0;
        if (show)
        {
            if (cam == null) cam = Camera.main;
            float ortho = cam != null ? cam.orthographicSize : 10f;
            float width = Mathf.Max(0.08f, ortho * 0.006f);   // constant on-screen thickness

            ring.transform.position = Selected.transform.position;
            ring.widthMultiplier = width;
            if (Selected is Ship) SetRingRadius(ring, RadiusOf(Selected));   // ships change size with zoom

            // rings for the rest of the group
            foreach (var s in SelectedShips)
            {
                if (s == null || s == Selected) continue;
                if (used >= extraRings.Count) extraRings.Add(MakeRing("GroupRing" + used));
                var r = extraRings[used++];
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                r.transform.position = s.transform.position;
                r.widthMultiplier = width;
                SetRingRadius(r, RadiusOf(s));
            }
        }
        for (int i = used; i < extraRings.Count; i++)
            if (extraRings[i].gameObject.activeSelf) extraRings[i].gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ drag box (drawn with OnGUI)

    void OnGUI()
    {
        if (!boxActive || Mouse.current == null) return;

        Vector2 cur = Mouse.current.position.ReadValue();
        float x = Mathf.Min(dragStart.x, cur.x), xMax = Mathf.Max(dragStart.x, cur.x);
        float yMin = Mathf.Min(dragStart.y, cur.y), yMax = Mathf.Max(dragStart.y, cur.y);
        // GUI space has y pointing down
        var r = new Rect(x, Screen.height - yMax, xMax - x, yMax - yMin);

        var old = GUI.color;
        GUI.color = new Color(0.35f, 0.9f, 1f, 0.15f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.35f, 0.9f, 1f, 0.9f);
        const float t = 1f;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    // ------------------------------------------------------------------ info text

    /// <summary>Title + rich-text body for the info panel. False when nothing is selected.</summary>
    public bool GetInfo(out string title, out string body)
    {
        title = body = null;
        if (!hasSelection || Selected == null) return false;

        if (SelectedShips.Count > 1)
        {
            title = SelectedShips.Count + " ships selected";
            body = DescribeGroup();
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

    string DescribeGroup()
    {
        var sb = new StringBuilder();
        float fuel = 0f, cargo = 0f;
        const int MaxLines = 20;
        int shown = 0;

        foreach (var s in SelectedShips)
        {
            if (s == null) continue;
            fuel += s.data.fuel;
            cargo += s.data.CargoTotal();
            if (shown++ < MaxLines)
                sb.AppendLine("<b>" + s.data.name + "</b>  <color=#888888>" + s.StatusText() + "</color>");
        }
        if (shown > MaxLines) sb.AppendLine("<color=#888888>+" + (shown - MaxLines) + " more</color>");

        sb.AppendLine();
        sb.AppendLine("Total fuel: " + fuel.ToString("0") + "   Total cargo: " + cargo.ToString("0"));
        sb.AppendLine("\n<color=#888888>Right-click: order all selected\nShift-click: add / remove   Drag: box select</color>");
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