using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ship simulation. Every ShipData is ticked here every frame - movement, fuel burn, refuelling,
/// warp spool-up / transit / arrival - regardless of which system is on screen. GameObjects (ShipView)
/// are built only for ships in the system currently shown by GalaxyManager, and destroyed when you
/// leave it or the ship warps out.
/// </summary>
public class FleetManager : MonoBehaviour
{
    public const int PlayerOwner = 0;

    const float ArriveRadius = 1.5f;     // a Move order is done inside this distance
    const float DriftDrag = 0.5f;        // fraction of normal deceleration when out of fuel
    const float MinTransitTime = 2f;     // shortest possible warp, seconds

    [Serializable]
    public struct StartingShips
    {
        public ShipClass shipClass;
        [Min(1)] public int count;
    }

    [Header("References")]
    [SerializeField] GalaxyManager galaxy;
    [Tooltip("Optional overrides. Any class left out uses the GDD prototype numbers.")]
    [SerializeField] ShipClassData[] classes;

    [Header("Start")]
    [SerializeField] StartingShips[] startingFleet =
    {
        new StartingShips { shipClass = ShipClass.Corvette, count = 3 },
        new StartingShips { shipClass = ShipClass.Cruiser, count = 1 }
    };

    [Header("Warp")]
    [Tooltip("Ships must be this far from the star to spool up; they fly out there automatically.")]
    [SerializeField] float minWarpDistanceFromStar = 40f;
    [Tooltip("How far inside the system edge ships appear.")]
    [SerializeField] float arrivalMargin = 15f;
    [Tooltip("Load the destination system when an ordered ship arrives (if you're still watching where it left).")]
    [SerializeField] bool cameraFollowsWarp = true;

    [Header("Fuel")]
    [Range(0f, 1f)] [SerializeField] float lowFuelFraction = 0.25f;
    [SerializeField] float refuelRadius = 6f;
    [SerializeField] float refuelRate = 30f;

    public readonly List<ShipData> Ships = new List<ShipData>();
    public readonly List<RefuelPoint> RefuelPoints = new List<RefuelPoint>();

    public event Action<ShipData> ShipRemoved;
    public event Action<ShipView> ViewCreated;
    public event Action<ShipData, string> Alert;     // "low fuel", "out of fuel", warp refused, ...

    public GalaxyManager Manager => galaxy;
    public Vector3 SystemOrigin => galaxy.SystemOrigin;
    public int ViewedSystemId => galaxy != null && galaxy.CurrentSystem != null ? galaxy.CurrentSystem.id : -1;
    public bool MapOpen => galaxy != null && galaxy.MapView != null && galaxy.MapView.IsOpen;
    List<StarSystemInfo> Systems => galaxy.Galaxy.systems;

    readonly Dictionary<int, ShipData> byId = new Dictionary<int, ShipData>();
    readonly Dictionary<int, ShipView> views = new Dictionary<int, ShipView>();
    readonly List<GameObject> markers = new List<GameObject>();
    readonly Dictionary<int, float> radiusCache = new Dictionary<int, float>();
    readonly List<ShipClassData> createdDefaults = new List<ShipClassData>();

    ShipClassData[] classLookup;
    GameObject viewRoot;
    GalaxyData lastGalaxy;
    int nextId = 1;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        int n = Enum.GetValues(typeof(ShipClass)).Length;
        classLookup = new ShipClassData[n];
        if (classes != null)
            foreach (var c in classes)
                if (c != null) classLookup[(int)c.shipClass] = c;
        for (int i = 0; i < n; i++)
            if (classLookup[i] == null)
            {
                classLookup[i] = ShipClassData.CreateDefault((ShipClass)i);
                createdDefaults.Add(classLookup[i]);
            }

        viewRoot = new GameObject("ShipViews");
        viewRoot.transform.SetParent(transform, false);
    }

    void Start()
    {
        if (galaxy == null) galaxy = FindFirstObjectByType<GalaxyManager>();
        if (galaxy == null)
        {
            Debug.LogError("FleetManager: no GalaxyManager found.", this);
            enabled = false;
            return;
        }

        galaxy.SystemLoaded += OnSystemLoaded;
        if (galaxy.CurrentSystem != null) OnSystemLoaded(galaxy.CurrentSystem);   // GalaxyManager.Start ran first
    }

    void OnDestroy()
    {
        if (galaxy != null) galaxy.SystemLoaded -= OnSystemLoaded;
        foreach (var d in createdDefaults) if (d != null) Destroy(d);
    }

    void OnSystemLoaded(SolarSystemData data)
    {
        if (galaxy.Galaxy != lastGalaxy)    // first system ever, or the galaxy was regenerated (G key)
        {
            ClearFleet();
            lastGalaxy = galaxy.Galaxy;
            SpawnStartingFleet(data);
        }
        RebuildViews();
    }

    void SpawnStartingFleet(SolarSystemData sys)
    {
        if (startingFleet == null) return;
        int total = 0;
        foreach (var e in startingFleet) total += e.count;

        float x = Mathf.Max(minWarpDistanceFromStar + 20f, sys.radius * 0.5f);
        int k = 0;
        foreach (var e in startingFleet)
            for (int i = 0; i < e.count; i++, k++)
                SpawnShip(e.shipClass, sys.id, new Vector2(x, (k - (total - 1) * 0.5f) * 4f), 0f);
    }

    // ------------------------------------------------------------------ simulation

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = Ships.Count - 1; i >= 0; i--)
        {
            var s = Ships[i];
            if (s.hp <= 0f) { RemoveShip(s); continue; }
            Tick(s, dt);
        }

        if (viewRoot != null)
        {
            bool open = MapOpen;                       // views hide while the galaxy map is up
            if (viewRoot.activeSelf == open) viewRoot.SetActive(!open);
        }
    }

    void Tick(ShipData s, float dt)
    {
        var c = ClassOf(s.shipClass);

        switch (s.state)
        {
            case ShipState.Spooling:
                Brake(s, c, dt, 3f);
                s.warpTimer -= dt;
                if (s.warpTimer <= 0f) BeginTransit(s, c);
                return;
            case ShipState.InTransit:
                s.warpTimer -= dt;
                if (s.warpTimer <= 0f) Arrive(s);
                return;
        }

        RefuelNearby(s, c, dt);
        Vector2? target = NextMoveTarget(s);
        if (s.state != ShipState.Normal) return;       // a warp spool-up started this tick
        Steer(s, c, target, dt);
    }

    /// <summary>Works through the order queue; returns where to fly (or null to just brake).</summary>
    Vector2? NextMoveTarget(ShipData s)
    {
        while (s.orders.Count > 0)
        {
            var o = s.orders[0];

            if (o.type == OrderType.Move)
            {
                if ((o.position - s.position).sqrMagnitude <= ArriveRadius * ArriveRadius)
                {
                    s.orders.RemoveAt(0);
                    continue;
                }
                return o.position;
            }

            // Warp order
            string why = WarpBlockReason(s, o.systemId);
            if (why != null)
            {
                RaiseAlert(s, "warp cancelled: " + why);
                s.orders.RemoveAt(0);
                continue;
            }

            if (s.position.magnitude >= minWarpDistanceFromStar)
            {
                BeginSpool(s, o.systemId);
                s.orders.RemoveAt(0);
                return null;
            }

            // too close to the star: fly straight out first
            Vector2 dir = s.position.sqrMagnitude > 1e-4f ? s.position.normalized : Vector2.right;
            return dir * (minWarpDistanceFromStar + 2f);
        }
        return null;
    }

    void Steer(ShipData s, ShipClassData c, Vector2? target, float dt)
    {
        bool hasFuel = s.fuel > 0f;
        float desired = 0f;

        if (target.HasValue && hasFuel)
        {
            Vector2 to = target.Value - s.position;
            float dist = to.magnitude;
            float want = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg - 90f;
            s.rotation = Mathf.MoveTowardsAngle(s.rotation, want, c.turnRate * dt);

            float align = Mathf.Max(0f, Mathf.Cos(Mathf.DeltaAngle(s.rotation, want) * Mathf.Deg2Rad));
            float brakeDist = s.speed * s.speed / (2f * c.acceleration);
            desired = dist - ArriveRadius * 0.5f <= brakeDist ? 0f : c.maxSpeed * align;
        }

        float rate = hasFuel ? c.acceleration : c.acceleration * DriftDrag;   // no fuel = drifter
        s.speed = Mathf.MoveTowards(s.speed, desired, rate * dt);
        Integrate(s, dt);

        if (hasFuel && s.speed > 0.05f)
        {
            s.fuel = Mathf.Max(0f, s.fuel - c.fuelBurnPerSec * dt);
            CheckFuelAlerts(s, c);
        }
    }

    void Brake(ShipData s, ShipClassData c, float dt, float strength)
    {
        s.speed = Mathf.MoveTowards(s.speed, 0f, c.acceleration * strength * dt);
        Integrate(s, dt);
    }

    static void Integrate(ShipData s, float dt)
    {
        if (s.speed <= 0f) return;
        float r = s.rotation * Mathf.Deg2Rad;
        s.position += new Vector2(-Mathf.Sin(r), Mathf.Cos(r)) * s.speed * dt;
    }

    // ------------------------------------------------------------------ fuel

    void RefuelNearby(ShipData s, ShipClassData c, float dt)
    {
        if (s.fuel >= c.fuelCapacity || s.speed > 3f) return;
        float rSq = refuelRadius * refuelRadius;
        foreach (var rp in RefuelPoints)
        {
            if (rp.systemId != s.systemId || (rp.position - s.position).sqrMagnitude > rSq) continue;
            s.fuel = Mathf.Min(c.fuelCapacity, s.fuel + refuelRate * dt);
            ResetFuelFlags(s, c);
            return;
        }
    }

    void CheckFuelAlerts(ShipData s, ShipClassData c)
    {
        if (s.fuel <= 0f)
        {
            if (s.emptyAlerted) return;
            s.emptyAlerted = true;
            RaiseAlert(s, "out of fuel - drifting");
        }
        else if (s.fuel <= c.fuelCapacity * lowFuelFraction && !s.lowFuelAlerted)
        {
            s.lowFuelAlerted = true;
            RaiseAlert(s, "low on fuel");
        }
    }

    void ResetFuelFlags(ShipData s, ShipClassData c)
    {
        if (s.fuel > 0f) s.emptyAlerted = false;
        if (s.fuel > c.fuelCapacity * lowFuelFraction) s.lowFuelAlerted = false;
    }

    public void Refuel(ShipData s)
    {
        var c = ClassOf(s.shipClass);
        s.fuel = c.fuelCapacity;
        ResetFuelFlags(s, c);
    }

    /// <summary>Straight-line distance the ship can still cover at full speed with its current fuel.</summary>
    public float MoveRange(ShipData s)
    {
        var c = ClassOf(s.shipClass);
        return c.fuelBurnPerSec > 0f ? s.fuel / c.fuelBurnPerSec * c.maxSpeed : 0f;
    }

    public bool TryGetNearestRefuel(ShipData s, out Vector2 pos)
    {
        pos = default;
        bool found = false;
        float best = float.MaxValue;
        foreach (var rp in RefuelPoints)
        {
            if (rp.systemId != s.systemId) continue;
            float d = (rp.position - s.position).sqrMagnitude;
            if (d < best) { best = d; pos = rp.position; found = true; }
        }
        return found;
    }

    public RefuelPoint AddRefuelPoint(int systemId, Vector2 pos)
    {
        var rp = new RefuelPoint { systemId = systemId, position = pos };
        RefuelPoints.Add(rp);
        if (systemId == ViewedSystemId) CreateMarker(rp);
        return rp;
    }

    // ------------------------------------------------------------------ warp

    public float JumpDistance(int a, int b) => Vector2.Distance(Systems[a].position, Systems[b].position);

    public int WarpFuelCost(ShipData s, int dest) =>
        Mathf.CeilToInt(JumpDistance(s.systemId, dest) * ClassOf(s.shipClass).warpFuelPerUnit);

    /// <summary>Null if the ship can warp to dest right now, otherwise a short reason.</summary>
    public string WarpBlockReason(ShipData s, int dest)
    {
        if (galaxy == null || galaxy.Galaxy == null) return "no galaxy";
        if (s.state == ShipState.InTransit) return "already in warp";
        if (s.systemId < 0) return "not in a system";
        if (dest < 0 || dest >= Systems.Count) return "invalid destination";
        if (dest == s.systemId) return "already in that system";

        var c = ClassOf(s.shipClass);
        float d = JumpDistance(s.systemId, dest);
        if (d > c.jumpRange) return $"out of range ({d:0}/{c.jumpRange:0})";
        int cost = WarpFuelCost(s, dest);
        if (s.fuel < cost) return $"not enough fuel ({Mathf.FloorToInt(s.fuel)}/{cost})";
        return null;
    }

    void BeginSpool(ShipData s, int dest)
    {
        s.state = ShipState.Spooling;
        s.warpFrom = s.systemId;
        s.warpDest = dest;
        s.warpTimer = ClassOf(s.shipClass).warpSpoolTime;
    }

    void BeginTransit(ShipData s, ShipClassData c)
    {
        string why = WarpBlockReason(s, s.warpDest);    // fuel may have changed while spooling
        if (why != null)
        {
            s.state = ShipState.Normal;
            RaiseAlert(s, "warp aborted: " + why);
            return;
        }

        s.fuel = Mathf.Max(0f, s.fuel - WarpFuelCost(s, s.warpDest));
        CheckFuelAlerts(s, c);

        s.warpDuration = Mathf.Max(MinTransitTime, JumpDistance(s.systemId, s.warpDest) / c.warpSpeed);
        s.warpTimer = s.warpDuration;
        s.speed = 0f;

        int from = s.systemId;
        s.state = ShipState.InTransit;
        s.systemId = -1;
        if (from == ViewedSystemId) DestroyView(s);
    }

    void Arrive(ShipData s)
    {
        int from = s.warpFrom, dest = s.warpDest;

        // appear on the edge of the destination, on the side facing where we came from, nose to the star
        Vector2 dir = Systems[from].position - Systems[dest].position;
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right;
        s.position = dir * Mathf.Max(SystemRadius(dest) - arrivalMargin, 0f);
        s.rotation = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg - 90f;
        s.speed = 0f;
        s.systemId = dest;
        s.state = ShipState.Normal;

        bool follow = s.followCamera && cameraFollowsWarp && ViewedSystemId == from;
        s.followCamera = false;

        if (dest == ViewedSystemId) CreateView(s);
        else if (follow) galaxy.LoadSystem(dest);       // SystemLoaded rebuilds views, including this ship
    }

    float SystemRadius(int id)
    {
        if (galaxy.CurrentSystem != null && galaxy.CurrentSystem.id == id) return galaxy.CurrentSystem.radius;
        if (radiusCache.TryGetValue(id, out float r)) return r;
        var info = Systems[id];                         // pure data - cheap and deterministic
        r = SolarSystemGenerator.Generate(info.seed, info.id, info.position).radius;
        radiusCache[id] = r;
        return r;
    }

    // ------------------------------------------------------------------ orders / spawning

    public ShipClassData ClassOf(ShipClass c) => classLookup[(int)c];

    public bool TryGetShip(int id, out ShipData s) => byId.TryGetValue(id, out s);

    public ShipData SpawnShip(ShipClass cls, int systemId, Vector2 pos, float rotation = 0f, int owner = PlayerOwner)
    {
        var c = ClassOf(cls);
        var s = new ShipData
        {
            id = nextId++,
            shipClass = cls,
            owner = owner,
            systemId = systemId,
            position = pos,
            rotation = rotation,
            hp = c.maxHealth,
            fuel = c.fuelCapacity
        };
        s.name = cls + " " + s.id;
        Ships.Add(s);
        byId[s.id] = s;
        if (systemId == ViewedSystemId) CreateView(s);
        return s;
    }

    public void OrderMove(ShipData s, Vector2 localPos, bool queue)
    {
        if (!PrepareOrder(s, queue)) return;
        s.orders.Add(ShipOrder.Move(localPos));
    }

    /// <summary>Returns null on success, or why the order was refused.</summary>
    public string OrderWarp(ShipData s, int dest, bool queue, bool followCamera)
    {
        string why = WarpBlockReason(s, dest);
        if (why != null) { RaiseAlert(s, "can't warp: " + why); return why; }
        if (!PrepareOrder(s, queue)) return "in warp";

        s.orders.Add(ShipOrder.Warp(dest));
        s.followCamera = followCamera;
        return null;
    }

    bool PrepareOrder(ShipData s, bool queue)
    {
        if (s.state == ShipState.InTransit) return false;
        if (!queue)
        {
            s.orders.Clear();
            if (s.state == ShipState.Spooling) s.state = ShipState.Normal;   // cancel spool-up
        }
        return true;
    }

    void RemoveShip(ShipData s)
    {
        Ships.Remove(s);
        byId.Remove(s.id);
        DestroyView(s);
        ShipRemoved?.Invoke(s);
    }

    void ClearFleet()
    {
        for (int i = Ships.Count - 1; i >= 0; i--) RemoveShip(Ships[i]);
        RefuelPoints.Clear();
        radiusCache.Clear();
    }

    void RaiseAlert(ShipData s, string msg)
    {
        Debug.Log($"[{s.name}] {msg}");
        Alert?.Invoke(s, msg);
    }

    // ------------------------------------------------------------------ views (the only GameObjects)

    public ShipView GetView(int shipId) => views.TryGetValue(shipId, out var v) ? v : null;

    void RebuildViews()
    {
        foreach (var v in views.Values) if (v != null) Destroy(v.gameObject);
        views.Clear();
        foreach (var m in markers) if (m != null) Destroy(m);
        markers.Clear();

        int id = ViewedSystemId;
        if (id < 0) return;
        foreach (var s in Ships) if (s.systemId == id) CreateView(s);
        foreach (var rp in RefuelPoints) if (rp.systemId == id) CreateMarker(rp);
    }

    void CreateView(ShipData s)
    {
        if (views.ContainsKey(s.id)) return;
        var v = ShipView.Create(this, s, ClassOf(s.shipClass), viewRoot.transform);
        views[s.id] = v;
        ViewCreated?.Invoke(v);
    }

    void DestroyView(ShipData s)
    {
        if (!views.TryGetValue(s.id, out var v)) return;
        if (v != null) Destroy(v.gameObject);
        views.Remove(s.id);
    }

    void CreateMarker(RefuelPoint rp)
    {
        var go = new GameObject("RefuelPoint");
        go.transform.SetParent(viewRoot.transform, false);
        go.transform.position = galaxy.SystemOrigin + (Vector3)rp.position;
        var col = new Color(0.3f, 1f, 0.5f, 0.6f);
        LineShapes.Circle(go.transform, "Range", refuelRadius, col, 0.25f, 5);
        LineShapes.Circle(go.transform, "Core", 0.8f, col, 0.25f, 5, 24);
        markers.Add(go);
    }
}
