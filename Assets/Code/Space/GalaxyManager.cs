using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Entry point. Generates a galaxy (data only), then opens one solar system at a time.
/// Debug keys (toggle off in the inspector): R = random system, M = toggle galaxy map, G = brand new galaxy.
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

    [Header("Debug")]
    [SerializeField] bool debugKeys = true;

    public GalaxyData Galaxy { get; private set; }
    public SolarSystemData CurrentSystem { get; private set; }
    public event System.Action<SolarSystemData> SystemLoaded;

    void Start()
    {
        CelestialBody.OrbitTimeScale = 0;

        if (mapView != null) mapView.SystemClicked += LoadSystem;

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

    public void LoadSystem(int id)
    {
        if (Galaxy == null || id < 0 || id >= Galaxy.systems.Count) return;

        var info = Galaxy.systems[id];
        CurrentSystem = SolarSystemGenerator.Generate(info.seed, info.id, info.position);

        if (mapView != null) mapView.Hide();
        spawner.Spawn(CurrentSystem);
        FrameSystem();

        Debug.Log($"Loaded {CurrentSystem.name} ({CurrentSystem.starClass}), {CurrentSystem.bodies.Count} bodies");
        SystemLoaded?.Invoke(CurrentSystem);
    }

    public void LoadRandomSystem() => LoadSystem(Random.Range(0, Galaxy.systems.Count));

    public void ToggleMap()
    {
        if (Galaxy == null || mapView == null) return;

        if (mapView.IsOpen)
        {
            mapView.Hide();
            spawner.SetVisible(true);
            if (cameraController != null && CurrentSystem != null)
                cameraController.SetmaxOrthoSize(CurrentSystem.radius * 1.1f);
            FrameSystem();
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
        cameraController.FocusOn(Vector3.zero);
    }

    void Update()
    {
        if (Keyboard.current.mKey.wasPressedThisFrame) ToggleMap();


        if (!debugKeys || Keyboard.current == null || Galaxy == null) return;

        if (Keyboard.current.rKey.wasPressedThisFrame) LoadRandomSystem();
        

        if (Keyboard.current.gKey.wasPressedThisFrame)   // G = brand new galaxy
        {
            GenerateGalaxy(Random.Range(1, int.MaxValue));
            if (GameState.Instance != null) LoadSystem(GameState.Instance.InitNewGame(Galaxy));
            else LoadRandomSystem();
        }
    }
}
