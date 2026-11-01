using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Entry point. Generates a galaxy (data only), then opens one solar system at a time.
/// Debug keys (toggle off in the inspector): R = random system, M = toggle galaxy map, G = brand new game.
/// </summary>
public class GalaxyManager : MonoBehaviour
{
    [Header("Galaxy")]
    [SerializeField] int galaxySeed = 12345;
    [SerializeField] bool randomizeSeedOnStart = true;
    [SerializeField, Min(1)] int systemCount = 400;
    [SerializeField] float galaxyRadius = 1000f;
    [SerializeField] GalaxyShape shape = GalaxyShape.Spiral;
    [SerializeField, Min(1)] int spiralArms = 4;
    [SerializeField] float spiralTwist = 3.5f;
    [SerializeField] float minSystemDistance = 12f;

    [Header("References")]
    [SerializeField] SolarSystemSpawner spawner;
    [Tooltip("Optional. Must be on its own child object.")]
    [SerializeField] GalaxyMapView mapView;
    [Tooltip("Optional. Needs useXYPlane = true for a 2D game.")]
    [SerializeField] TopDownCameraController cameraController;
    [Header("Menu backdrop")]
    [Tooltip("1.0 = last orbit ring touches the screen edge. 1.1 = small margin. Lower = zoomed in more.")]
    [SerializeField, Range(0f, 1.5f)] float overviewZoom = 1.1f;
    [Header("Debug")]
    [SerializeField] bool debugKeys = true;

    public GalaxyData Galaxy { get; private set; }
    public SolarSystemData CurrentSystem { get; private set; }
    public bool MapOpen => mapView != null && mapView.IsOpen;
    public int MapSelectedId { get; private set; } = -1;
    public event System.Action MapSelectionChanged;
    public event System.Action<SolarSystemData> SystemLoaded;
    bool overview;

    void Start()
    {
        CelestialBody.OrbitTimeScale = 0;

        if (mapView != null) mapView.SystemClicked += SetMapSelection;

        // This first game is the backdrop behind the title screen; "New Game" generates a fresh one.
        GenerateGalaxy(randomizeSeedOnStart ? Random.Range(1, int.MaxValue) : galaxySeed);
        LoadSystem(GameState.Instance != null
            ? GameState.Instance.InitNewGame(Galaxy)
            : new System.Random(Galaxy.seed).Next(Galaxy.systems.Count));
    }

    public void GenerateGalaxy(int seed)
    {
        galaxySeed = seed;
        Galaxy = GalaxyGenerator.Generate(seed, systemCount, galaxyRadius, shape,
                                          spiralArms, spiralTwist, minSystemDistance);
        Debug.Log($"Galaxy {seed}: {Galaxy.systems.Count} systems");
    }

    // ------------------------------------------------------------------ new game / save / load

    /// <summary>Title / main menu backdrop: whole solar system centred and fully visible.</summary>
    public void EnterOverview()
    {
        overview = true;
        FrameFullSystem();
    }

    /// <summary>Leaving the menus: restore the normal zoom limit for this system.</summary>
    public void ExitOverview()
    {
        if (!overview) return;
        overview = false;
        if (cameraController != null && CurrentSystem != null)
            cameraController.SetmaxOrthoSize(CurrentSystem.radius * 1.1f);
    }

    void FrameFullSystem()
    {
        if (cameraController == null || CurrentSystem == null) return;

        // Ortho size is half the screen height, so on narrow (non-wide) screens grow it to fit the width too.
        float aspect = Camera.main != null ? Camera.main.aspect : 1.78f;
        float size = OuterOrbit(CurrentSystem) * overviewZoom;

        cameraController.SetmaxOrthoSize(size);
        cameraController.FocusOn(spawner.transform.position);
        // instant (the camera script is paused while menus are open, so it can't smooth the zoom itself)
        cameraController.RestoreView(cameraController.transform.position, size);
    }

    /// <summary>Brand new galaxy + home planet + reset clock. seed 0 = random.</summary>
    public void StartNewGame(int seed = 0)
    {
        CloseMap();
        GenerateGalaxy(seed != 0 ? seed : Random.Range(1, int.MaxValue));
        if (GameClock.Instance != null) GameClock.Instance.Restore(0f, 1);
        LoadSystem(GameState.Instance != null
            ? GameState.Instance.InitNewGame(Galaxy)
            : Random.Range(0, Galaxy.systems.Count));
    }

    public SaveData CaptureSave()
    {
        var d = new SaveData
        {
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ticks = System.DateTime.UtcNow.Ticks,
            galaxySeed = Galaxy.seed,
            systemCount = systemCount,
            galaxyRadius = galaxyRadius,
            shape = (int)shape,
            spiralArms = spiralArms,
            spiralTwist = spiralTwist,
            minSystemDistance = minSystemDistance,
            currentSystemId = CurrentSystem != null ? CurrentSystem.id : 0,
            currentSystemName = CurrentSystem != null ? CurrentSystem.name : ""
        };

        if (GameState.Instance != null) GameState.Instance.ExportSave(d);
        if (GameClock.Instance != null)
        {
            d.gameTime = GameClock.Instance.GameTime;
            d.speedIndex = GameClock.Instance.SpeedIndex;
        }
        if (cameraController != null && !MapOpen)
        {
            var p = cameraController.transform.position;
            d.hasCamera = true;
            d.camX = p.x; d.camY = p.y; d.camZ = p.z;
            d.orthoSize = cameraController.OrthoSize;
        }
        return d;
    }

    public bool LoadGame(SaveData d)
    {
        if (d == null || GameState.Instance == null) return false;

        CloseMap();

        // use the settings the save was made with
        if (d.systemCount > 0) systemCount = d.systemCount;
        if (d.galaxyRadius > 0f) galaxyRadius = d.galaxyRadius;
        shape = (GalaxyShape)d.shape;
        if (d.spiralArms > 0) spiralArms = d.spiralArms;
        spiralTwist = d.spiralTwist;
        if (d.minSystemDistance > 0f) minSystemDistance = d.minSystemDistance;

        GenerateGalaxy(d.galaxySeed);
        GameState.Instance.ImportSave(Galaxy, d);
        if (GameClock.Instance != null) GameClock.Instance.Restore(d.gameTime, d.speedIndex);

        LoadSystem(Mathf.Clamp(d.currentSystemId, 0, Galaxy.systems.Count - 1));

        if (d.hasCamera && cameraController != null)
            cameraController.RestoreView(new Vector3(d.camX, d.camY, d.camZ), d.orthoSize);
        return true;
    }

    // ------------------------------------------------------------------ systems / map

    void SetMapSelection(int id)
    {
        if (MapSelectedId == id) return;
        MapSelectedId = id;
        if (mapView != null) mapView.SetSelected(id);
        MapSelectionChanged?.Invoke();
    }

    /// <summary>The "Go to system" button.</summary>
    public void GoToSelectedSystem()
    {
        int id = MapSelectedId;
        if (id < 0) return;
        if (CurrentSystem != null && CurrentSystem.id == id) { CloseMap(); return; }
        LoadSystem(id);
    }

    public void LoadSystem(int id)
    {
        if (Galaxy == null || id < 0 || id >= Galaxy.systems.Count) return;

        var info = Galaxy.systems[id];
        CurrentSystem = SolarSystemGenerator.Generate(info.seed, info.id, info.position);

        SetMapSelection(-1);
        if (mapView != null) mapView.Hide();
        spawner.Spawn(CurrentSystem);   // builds a fresh, visible system

        // The galaxy map zooms way out; always go back to this system's zoom range.
        if (cameraController != null) cameraController.SetmaxOrthoSize(CurrentSystem.radius * 1.1f);
        FrameSystem();

        Debug.Log($"Loaded {CurrentSystem.name} ({CurrentSystem.starClass}), {CurrentSystem.bodies.Count} bodies");
        SystemLoaded?.Invoke(CurrentSystem);
        if (overview) FrameFullSystem();
    }

    public void LoadRandomSystem() => LoadSystem(Random.Range(0, Galaxy.systems.Count));

    public void ToggleMap()
    {
        if (Galaxy == null || mapView == null) return;

        if (mapView.IsOpen)
        {
            CloseMap();
        }
        else
        {
            spawner.SetVisible(false);
            mapView.Show(Galaxy, CurrentSystem != null ? CurrentSystem.id : -1);
            if (cameraController != null)
                cameraController.SetmaxOrthoSize(Galaxy.radius * 1.1f);
            FrameGalaxy();
        }
    }

    /// <summary>Closes the galaxy map (if open) and returns to the current solar system.</summary>
    public void CloseMap()
    {
        if (mapView == null || !mapView.IsOpen) return;

        SetMapSelection(-1);
        mapView.Hide();
        spawner.SetVisible(true);
        if (cameraController != null && CurrentSystem != null)
            cameraController.SetmaxOrthoSize(CurrentSystem.radius * 1.1f);
        FrameSystem();
    }

    void FrameSystem()
    {
        if (cameraController == null || CurrentSystem == null) return;
        var e = Vector2.one * CurrentSystem.radius;
        cameraController.SetBounds(-e, e);
        cameraController.FocusOn(spawner.transform.position);
    }

    void FrameGalaxy()
    {
        if (cameraController == null) return;
        var e = Vector2.one * Galaxy.radius * 1.1f;
        cameraController.SetBounds(-e, e);

        // Centre on the system we just left, so the green marker is in the middle of the screen.
        Vector3 focus = Vector3.zero;
        if (CurrentSystem != null)
        {
            Vector2 p = CurrentSystem.galaxyPosition;
            focus = mapView != null
                ? mapView.transform.TransformPoint(new Vector3(p.x, p.y, 0f))
                : new Vector3(p.x, p.y, 0f);
        }
        cameraController.FocusOn(focus);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || GameFlow.InputBlocked) return;

        if (kb.mKey.wasPressedThisFrame) ToggleMap();

        if (!debugKeys || Galaxy == null) return;

        if (kb.rKey.wasPressedThisFrame) LoadRandomSystem();
        if (kb.gKey.wasPressedThisFrame) StartNewGame();   // G = brand new game
    }

    /// <summary>Distance from the star to the outermost thing orbiting it (planet, belt rock or moon).</summary>
    static float OuterOrbit(SolarSystemData sd)
    {
        float r = 0f;
        foreach (var b in sd.bodies)
        {
            if (b.parentIndex < 0) continue;   // the star itself

            float dist = b.orbitRadius + b.diameter * 0.5f;
            if (b.parentIndex > 0)             // moon: add its planet's orbit so it isn't clipped
                dist += sd.bodies[b.parentIndex].orbitRadius;

            r = Mathf.Max(r, dist);
        }
        return r;
    }

    private void OnDrawGizmos()
    {
        if (CurrentSystem == null) return;
        Gizmos.color = Color.red;
        float aspect = Camera.main != null ? Camera.main.aspect : 1.78f;
        float size = OuterOrbit(CurrentSystem) * overviewZoom;
        Gizmos.DrawWireSphere(Vector3.zero, size);
    }
}