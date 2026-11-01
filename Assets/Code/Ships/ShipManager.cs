using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the ship VIEWS. The ship DATA lives in GameState.Ships. Spawns the views for the loaded
/// system, adds ships when a Shipyard finishes one, and turns right-click into orders for ALL
/// selected ships (move to a point in a loose grid, or dock at a body). Created by RTSBootstrap.
/// </summary>
public class ShipManager : MonoBehaviour
{
    public static ShipManager Instance { get; private set; }

    GalaxyManager galaxy;
    SolarSystemSpawner spawner;
    Transform container;
    Camera cam;
    readonly List<Ship> ships = new List<Ship>();

    public Vector3 Origin => spawner != null ? spawner.transform.position : Vector3.zero;

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

    // ------------------------------------------------------------------ lookups used by Ship

    public CelestialBody FindBody(int index)
    {
        if (spawner == null) return null;
        foreach (var cb in spawner.Bodies)
            if (cb != null && cb.index == index) return cb;
        return null;
    }

    public string BodyName(int index)
    {
        var sd = galaxy != null ? galaxy.CurrentSystem : null;
        return sd != null && index >= 0 && index < sd.bodies.Count ? sd.bodies[index].name : "?";
    }

    // ------------------------------------------------------------------ spawning

    void OnSystemLoaded(SolarSystemData sd)
    {
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

    // ------------------------------------------------------------------ orders

    void Update()
    {
        if (container != null && galaxy != null)
        {
            bool visible = !galaxy.MapOpen;           // the galaxy map hides the system, so hide ships too
            if (container.gameObject.activeSelf != visible) container.gameObject.SetActive(visible);
        }

        if (GameFlow.Mode != GameMode.Playing || (galaxy != null && galaxy.MapOpen)) return;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var sel = SelectionManager.Instance;
        if (sel == null || sel.SelectedShips.Count == 0) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());

        var group = new List<Ship>(sel.SelectedShips.Count);
        foreach (var s in sel.SelectedShips) if (s != null) group.Add(s);
        if (group.Count == 0) return;

        var body = PickBody(world);
        if (body != null)
        {
            if (body.data.type == PlanetType.Star) { ToastUI.Show("Ships can't dock at a star"); return; }
            foreach (var s in group) s.OrderDock(body);
        }
        else
        {
            // spread the group into a loose grid around the click so ships don't stack
            int n = group.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt(n / (float)cols);
            float spacing = Mathf.Max(1.5f, cam.orthographicSize * 0.05f);

            for (int i = 0; i < n; i++)
            {
                Vector2 off = n == 1 ? Vector2.zero : new Vector2(
                    (i % cols - (cols - 1) * 0.5f) * spacing,
                    (i / cols - (rows - 1) * 0.5f) * spacing);
                group[i].OrderMove(world + off);
            }
        }
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