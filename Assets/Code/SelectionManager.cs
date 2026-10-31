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
/// Click empty space or press Esc to deselect. H = jump to the home planet.
/// A highlight ring follows the selection.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    const float HomeZoom = 20f;

    public Component Selected { get; private set; }
    public event System.Action SelectionChanged;

    GalaxyManager galaxy;
    SolarSystemSpawner spawner;
    TopDownCameraController camCtl;
    Camera cam;

    LineRenderer ring;
    Material ringMat;
    bool hasSelection;
    bool pendingHomeFocus;

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
        Select(null);   // old bodies were destroyed
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
        if (hasSelection && Selected == null) Select(null);  // selected object was destroyed
        if (GameFlow.InputBlocked) return;                    // title / menus / pause
        if (GameFlow.Mode != GameMode.Playing) { Select(null); return; }            // only in RTS mode

        var rts = GameInput.Actions.RTS;
        if (rts.Deselect.WasPressedThisFrame()) Select(null);
        if (rts.ToggleMap.WasPressedThisFrame()) Select(null);
        if (rts.FocusHome.WasPressedThisFrame()) FocusHome();

        if (!rts.Select.WasPressedThisFrame()) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector2 world = cam.ScreenToWorldPoint(rts.Point.ReadValue<Vector2>());
        Select(Pick(world));
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

    public void Select(Component c)
    {
        Selected = c;
        hasSelection = c != null;
        ring.gameObject.SetActive(hasSelection);
        if (hasSelection) SetRingRadius(RadiusOf(c));
        SelectionChanged?.Invoke();
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

    // ------------------------------------------------------------------ highlight ring

    void BuildRing()
    {
        ringMat = new Material(Shader.Find("Sprites/Default"));
        var go = new GameObject("SelectionRing");
        go.transform.SetParent(transform, false);
        ring = go.AddComponent<LineRenderer>();
        ring.sharedMaterial = ringMat;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 64;
        ring.startColor = ring.endColor = new Color(0.35f, 0.9f, 1f, 0.95f);
        ring.sortingOrder = 5000;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        go.SetActive(false);
    }

    void SetRingRadius(float r)
    {
        for (int i = 0; i < ring.positionCount; i++)
        {
            float a = i / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r);
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

        if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
        if (!show) return;

        ring.transform.position = Selected.transform.position;
        if (cam == null) cam = Camera.main;
        float ortho = cam != null ? cam.orthographicSize : 10f;
        ring.widthMultiplier = Mathf.Max(0.08f, ortho * 0.006f);   // constant on-screen thickness
    }

    // ------------------------------------------------------------------ info text

    /// <summary>Title + rich-text body for the info panel. False when nothing is selected.</summary>
    public bool GetInfo(out string title, out string body)
    {
        title = body = null;
        if (!hasSelection || Selected == null) return false;

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

            sb.Append(Heading("Buildings"));
            sb.AppendLine("<color=#888888>None (building arrives in phase 3)</color>");
        }
        return sb.ToString();
    }
}
