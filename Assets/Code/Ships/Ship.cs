using System.Text;
using UnityEngine;

/// <summary>
/// The in-world view of a ShipData: a placeholder sprite coloured by role, kinematic movement on
/// game time (so pause and speed apply), fuel burn by distance, docking, and automatic routes
/// (see ShipAutomation). ISelectable so SelectionManager can pick it and show its info.
/// Spawned by ShipManager.
/// </summary>
public class Ship : MonoBehaviour, ISelectable
{
    public ShipData data;
    public ShipDef def;
    public ShipAutomation auto;

    ShipManager mgr;
    SpriteRenderer sr;
    Camera cam;
    static Sprite triangle;

    public static float DockRadius(float bodyDiameter) => bodyDiameter * 0.5f + 1.2f;

    public void Init(ShipManager manager, ShipData d)
    {
        mgr = manager;
        data = d;
        def = ShipDefs.Get(d.cls);
        gameObject.name = d.name;
        data.EnsureCargo();
        auto = new ShipAutomation(this, mgr);

        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = Triangle();
        sr.color = RoleColor(def.role);
        sr.sortingOrder = 50;                      // above planets, below the selection ring

        var col = gameObject.AddComponent<CircleCollider2D>();   // so SelectionManager can click it
        col.radius = 0.6f;
        col.isTrigger = true;

        transform.position = mgr.Origin + (Vector3)data.pos;
        ApplyScale();
    }

    // ------------------------------------------------------------------ ISelectable

    public string SelectionTitle => data.name;
    public float SelectionRadius => transform.localScale.x * 0.9f;

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine(def.role + "  -  " + def.sizeName);
        sb.AppendLine("Status: " + StatusText());
        sb.AppendLine("Fuel: " + data.fuel.ToString("0") + " / " + def.fuelTank.ToString("0") +
                      "   <color=#888888>range " + (def.fuelPerUnit > 0f ? data.fuel / def.fuelPerUnit : 0f).ToString("0") + "</color>");
        sb.AppendLine("Cargo: " + data.CargoTotal().ToString("0") + " / " + def.cargo.ToString("0"));
        foreach (var t in ResourceInfo.Physical)
            if (data.cargo[(int)t] > 0.5f)
                sb.AppendLine("  " + ResourceInfo.Coloured(t) + "  " + data.cargo[(int)t].ToString("N0"));

        if (data.autoOn)
        {
            sb.AppendLine("\n<color=#7dff9b>Auto route</color>");
            sb.AppendLine("A: " + mgr.BodyName(data.autoA) + "   B: " + mgr.BodyName(data.autoB));
            sb.AppendLine("<color=#888888>" + auto.Status + "</color>");
        }

        sb.AppendLine("\n<color=#888888>Right-click: move, or dock at a body\nShift+click or drag a box to select several</color>");
        return sb.ToString();
    }

    public string StatusText()
    {
        switch (data.mode)
        {
            case ShipData.Docked: return "Docked at " + mgr.BodyName(data.dockedBody);
            case ShipData.Moving:
                return data.targetBody >= 0 ? "Heading to " + mgr.BodyName(data.targetBody) : "Moving";
            default: return "Idle in space";
        }
    }

    // ------------------------------------------------------------------ orders (manual: these cancel a route)

    /// <summary>Turns the route off. Returns true if one was running.</summary>
    public bool CancelAuto()
    {
        if (!data.autoOn) return false;
        data.autoOn = false;
        auto.Reset();
        return true;
    }

    public bool OrderMove(Vector2 worldPoint)
    {
        CancelAuto();
        return BeginMove(-1, worldPoint - (Vector2)mgr.Origin);
    }

    public bool OrderDock(CelestialBody body)
    {
        CancelAuto();
        return BeginMove(body.index, Vector2.zero);
    }

    public void OrderStop()
    {
        if (data.mode != ShipData.Moving) return;
        CancelAuto();
        data.mode = ShipData.Idle;
        data.targetBody = -1;
    }

    // ------------------------------------------------------------------ orders (used by ShipAutomation)

    /// <summary>Quiet dock order for automation. False if the body is missing or the fuel won't reach.</summary>
    public bool AutoDock(int bodyIndex) => BeginMove(bodyIndex, Vector2.zero, true);

    /// <summary>Fuel needed to fly from here and dock at the body (infinite if it isn't loaded).</summary>
    public float FuelToReach(int bodyIndex)
    {
        var body = mgr.FindBody(bodyIndex);
        if (body == null) return float.MaxValue;
        float stop = DockRadius(body.data.diameter);
        return Mathf.Max(0f, Vector2.Distance(data.pos, BodyLocal(body)) - stop) * def.fuelPerUnit;
    }

    public float DistanceTo(int bodyIndex)
    {
        var body = mgr.FindBody(bodyIndex);
        return body == null ? float.MaxValue : Vector2.Distance(data.pos, BodyLocal(body));
    }

    bool BeginMove(int bodyIndex, Vector2 point, bool quiet = false)
    {
        Vector2 target = point;
        float stop = 0f;
        if (bodyIndex >= 0)
        {
            var body = mgr.FindBody(bodyIndex);
            if (body == null) return false;
            target = BodyLocal(body);
            stop = DockRadius(body.data.diameter);
        }

        float need = Mathf.Max(0f, Vector2.Distance(data.pos, target) - stop) * def.fuelPerUnit;
        if (need > data.fuel + 0.001f)
        {
            if (!quiet)
                ToastUI.Show(data.name + ": not enough fuel (needs " + need.ToString("0") + ", has " + data.fuel.ToString("0") + ")");
            return false;
        }

        data.mode = ShipData.Moving;
        data.dockedBody = -1;
        data.targetBody = bodyIndex;
        data.targetPos = point;
        return true;
    }

    // ------------------------------------------------------------------ simulation

    void Update()
    {
        var clock = GameClock.Instance;
        float dt = clock != null ? clock.Dt : 0f;

        if (data.mode == ShipData.Docked)
        {
            var b = mgr.FindBody(data.dockedBody);       // follows the body if it moves
            if (b != null) data.pos = BodyLocal(b) + data.dockOffset;
        }
        else if (data.mode == ShipData.Moving && dt > 0f)
        {
            Move(dt);
        }

        if (dt > 0f) auto.Tick(dt);

        transform.position = mgr.Origin + (Vector3)data.pos;
    }

    void Move(float dt)
    {
        CelestialBody body = null;
        Vector2 target = data.targetPos;
        float stop = 0f;
        if (data.targetBody >= 0)
        {
            body = mgr.FindBody(data.targetBody);
            if (body == null) { data.mode = ShipData.Idle; return; }
            target = BodyLocal(body);
            stop = DockRadius(body.data.diameter);
        }

        Vector2 to = target - data.pos;
        float travel = Mathf.Max(0f, to.magnitude - stop);
        float step = Mathf.Min(def.speed * dt, travel);

        if (def.fuelPerUnit > 0f)
            step = Mathf.Min(step, data.fuel / def.fuelPerUnit);       // can't go further than the fuel allows

        if (step > 0f)
        {
            Vector2 dir = to.normalized;
            data.pos += dir * step;
            data.fuel = Mathf.Max(0f, data.fuel - step * def.fuelPerUnit);
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        }

        if (travel - step <= 0.01f)                 // arrived
        {
            if (body != null) DockAt(body);
            else data.mode = ShipData.Idle;
        }
        else if (data.fuel <= 0.0001f)              // ran dry
        {
            data.mode = ShipData.Idle;
            data.targetBody = -1;
            ToastUI.Show(data.name + " has run out of fuel");
        }
    }

    void DockAt(CelestialBody body)
    {
        data.mode = ShipData.Docked;
        data.dockedBody = body.index;
        data.targetBody = -1;

        // fan arrivals out a little so ships docking together don't sit on top of each other
        Vector2 off = data.pos - BodyLocal(body);
        float fan = ((data.id % 7) - 3) * 0.22f * Mathf.Rad2Deg;
        data.dockOffset = (Vector2)(Quaternion.Euler(0f, 0f, fan) * (Vector3)off);

        if (!data.autoOn) ToastUI.Show(data.name + " docked at " + body.data.name);   // routes would spam this
    }

    Vector2 BodyLocal(CelestialBody b) => (Vector2)(b.transform.position - mgr.Origin);

    // ------------------------------------------------------------------ look

    void LateUpdate() => ApplyScale();

    void ApplyScale()
    {
        if (cam == null) cam = Camera.main;
        float ortho = cam != null ? cam.orthographicSize : 10f;
        // keeps ships readable when zoomed far out
        transform.localScale = Vector3.one * Mathf.Max(0.6f, ortho * 0.03f) * def.scale;
    }

    static Color RoleColor(ShipRole r)
    {
        switch (r)
        {
            case ShipRole.Combat: return new Color(0.95f, 0.30f, 0.30f);
            case ShipRole.Capital: return new Color(1f, 0.80f, 0.25f);
            default: return new Color(0.25f, 0.85f, 0.78f);
        }
    }

    static Sprite Triangle()
    {
        if (triangle != null) return triangle;
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color32[S * S];
        float cx = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
        {
            float half = (1f - y / (float)(S - 1)) * S * 0.45f;     // wide at the bottom, point at the top
            for (int x = 0; x < S; x++)
                px[y * S + x] = y >= 2 && Mathf.Abs(x - cx) <= half
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(0, 0, 0, 0);
        }
        tex.SetPixels32(px);
        tex.Apply();
        triangle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return triangle;
    }
}
