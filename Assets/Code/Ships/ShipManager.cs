using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the ship VIEWS. The ship DATA lives in GameState.Ships. Spawns the views for the loaded
/// system, adds ships when a Shipyard finishes one, and turns right-click into orders for the
/// whole selected group (move in formation, or dock at a body). Also handles "pick a route end"
/// clicks for the ship panel. Created by RTSBootstrap.
/// </summary>
public class ShipManager : MonoBehaviour
{
    public static ShipManager Instance { get; private set; }

    GalaxyManager galaxy;
    SolarSystemSpawner spawner;
    Transform container;
    Camera cam;
    readonly List<Ship> ships = new List<Ship>();

    int pickEndpoint = -1;   // -1 = not picking, 0 = picking route end A, 1 = picking B
    bool endPick;

    public IReadOnlyList<Ship> Ships => ships;
    public Vector3 Origin => spawner != null ? spawner.transform.position : Vector3.zero;
    public SolarSystemData CurrentSystem => galaxy != null ? galaxy.CurrentSystem : null;

    public bool IsPicking => pickEndpoint >= 0;
    public int PickingEndpoint => pickEndpoint;

    void Awake()
    {
        Instance = this;
        galaxy = FindFirstObjectByType<GalaxyManager>();
        spawner = FindFirstObjectByType<SolarSystemSpawner>();
        container = new GameObject("Ships").transform;
        container.SetParent(transform, false);

        // Subscribe in Awake: GalaxyManager.Start raises SystemLoaded during its own Start.
        if (galaxy != null) galaxy.SystemLoaded += OnSystemLoaded;
        EconomySystem.ShipCompleted += OnShipCompleted;     // static event: must be removed in OnDestroy
    }

    void OnDestroy()
    {
        if (galaxy != null) galaxy.SystemLoaded -= OnSystemLoaded;
        EconomySystem.ShipCompleted -= OnShipCompleted;
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ lookups used by Ship / automation

    public CelestialBody FindBody(int index)
    {
        if (spawner == null) return null;
        foreach (var cb in spawner.Bodies)
            if (cb != null && cb.index == index) return cb;
        return null;
    }

    public string BodyName(int index)
    {
        var sd = CurrentSystem;
        return sd != null && index >= 0 && index < sd.bodies.Count ? sd.bodies[index].name : "?";
    }

    /// <summary>Economy state of a body in the loaded system (null if it doesn't exist).</summary>
    public BodyState StateOf(int bodyIndex)
    {
        var sd = CurrentSystem;
        var gs = GameState.Instance;
        if (sd == null || gs == null || bodyIndex < 0 || bodyIndex >= sd.bodies.Count) return null;
        return gs.GetBodyState(sd, bodyIndex);
    }

    // ------------------------------------------------------------------ route editing (used by ShipPanelUI)

    public void BeginPick(int endpoint)
    {
        pickEndpoint = endpoint;
        endPick = false;
        ToastUI.Show("Click one of your planets for route end " + (endpoint == 0 ? "A" : "B") + "  (right-click cancels)");
    }

    public void CancelPick() => pickEndpoint = -1;

    /// <summary>Copies the first selected ship's route setup to every other selected ship.</summary>
    public void UnifyRoute()
    {
        var sel = SelectionManager.Instance;
        if (sel == null || sel.SelectedShips.Count < 2) return;
        var primary = sel.SelectedShips[0];
        if (primary == null) return;
        for (int i = 1; i < sel.SelectedShips.Count; i++)
        {
            var s = sel.SelectedShips[i];
            if (s != null) s.data.CopyRouteFrom(primary.data);
        }
    }

    // ------------------------------------------------------------------ spawning

    void OnSystemLoaded(SolarSystemData sd)
    {
        pickEndpoint = -1;
        foreach (var s in ships) if (s != null) Destroy(s.gameObject);
        ships.Clear();

        var gs = GameState.Instance;
        if (gs == null) return;
        foreach (var data in gs.Ships)
            if (data.systemId == sd.id) SpawnView(data);
    }

    void SpawnView(ShipData data)
    {
        var go = new GameObject(data.name);
        go.transform.SetParent(container, false);
        var ship = go.AddComponent<Ship>();
        ship.Init(this, data);
        ships.Add(ship);
    }

    void OnShipCompleted(BodyState st, ShipClass cls)
    {
        var gs = GameState.Instance;
        if (gs == null) return;

        // work out where to park it (the body might be in a system that isn't loaded)
        SolarSystemData sd = galaxy != null ? galaxy.CurrentSystem : null;
        if (sd == null || sd.id != st.systemId)
        {
            var info = gs.Galaxy.systems[st.systemId];
            sd = SolarSystemGenerator.Generate(info.seed, info.id, info.position);
        }
        float r = Ship.DockRadius(sd.bodies[st.bodyIndex].diameter);
        float angle = gs.NextShipId * 2.39996f;      // golden angle: ships docked together don't stack
        var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

        var data = gs.AddShip(cls, st.systemId, st.bodyIndex, offset);
        if (galaxy != null && galaxy.CurrentSystem != null && galaxy.CurrentSystem.id == data.systemId)
            SpawnView(data);
    }

    // ------------------------------------------------------------------ input

    void Update()
    {
        if (container != null && galaxy != null)
        {
            bool visible = !galaxy.MapOpen;           // the galaxy map hides the system, so hide ships too
            if (container.gameObject.activeSelf != visible) container.gameObject.SetActive(visible);
        }

        if (GameFlow.Mode != GameMode.Playing || (galaxy != null && galaxy.MapOpen))
        {
            pickEndpoint = -1;
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (IsPicking) { HandlePick(mouse); return; }

        if (!mouse.rightButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        IssueOrders(mouse);
    }

    // Picking ends in LateUpdate so SelectionManager (which skips clicks while IsPicking) still sees
    // IsPicking == true this frame no matter which Update runs first.
    void LateUpdate()
    {
        if (endPick)
        {
            pickEndpoint = -1;
            endPick = false;
        }
    }

    void HandlePick(Mouse mouse)
    {
        var sel = SelectionManager.Instance;
        if (sel == null || sel.SelectedShips.Count == 0) { pickEndpoint = -1; return; }

        if (mouse.rightButton.wasPressedThisFrame) { endPick = true; return; }
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());

        var body = PickBody(world);
        if (body == null || body.data.type == PlanetType.Star)
        {
            ToastUI.Show("Pick one of your planets or moons");
            return;
        }
        var st = StateOf(body.index);
        if (st == null || !st.owned)
        {
            ToastUI.Show(body.data.name + " isn't yours yet. Claim it first.");
            return;
        }

        var primary = sel.SelectedShips[0];
        if (primary == null) { pickEndpoint = -1; return; }
        primary.data.EnsureAuto();

        int other = pickEndpoint == 0 ? primary.data.autoB : primary.data.autoA;
        if (other == body.index)
        {
            ToastUI.Show("Route ends must be two different planets");
            return;
        }

        if (pickEndpoint == 0) primary.data.autoA = body.index;
        else primary.data.autoB = body.index;
        UnifyRoute();

        ToastUI.Show("Route end " + (pickEndpoint == 0 ? "A" : "B") + ": " + body.data.name);
        endPick = true;
    }

    void IssueOrders(Mouse mouse)
    {
        var sel = SelectionManager.Instance;
        if (sel == null || sel.SelectedShips.Count == 0) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());

        var group = new List<Ship>(sel.SelectedShips);
        var body = PickBody(world);
        if (body != null && body.data.type == PlanetType.Star)
        {
            ToastUI.Show("Ships can't dock at a star");
            return;
        }

        bool hadRoute = false;
        foreach (var s in group) if (s != null && s.data.autoOn) hadRoute = true;
        if (hadRoute) ToastUI.Show("Manual order: route cancelled");

        float spacing = Mathf.Max(1.2f, (cam.orthographic ? cam.orthographicSize : 10f) * 0.05f);
        for (int i = 0; i < group.Count; i++)
        {
            var s = group[i];
            if (s == null) continue;
            if (body != null) s.OrderDock(body);
            else s.OrderMove(world + Formation(i, group.Count, spacing));
        }
    }

    /// <summary>Sunflower spread around the click point so a group doesn't stack on one spot.</summary>
    static Vector2 Formation(int index, int count, float spacing)
    {
        if (count <= 1) return Vector2.zero;
        float r = spacing * Mathf.Sqrt(index);
        float a = index * 2.39996f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
    }

    static CelestialBody PickBody(Vector2 world)
    {
        CelestialBody best = null;
        float bestDia = float.MaxValue;
        foreach (var hit in Physics2D.OverlapPointAll(world))
        {
            var cb = hit.GetComponentInParent<CelestialBody>();
            if (cb != null && cb.data.diameter < bestDia) { bestDia = cb.data.diameter; best = cb; }   // smallest wins (moon over planet)
        }
        return best;
    }
}
