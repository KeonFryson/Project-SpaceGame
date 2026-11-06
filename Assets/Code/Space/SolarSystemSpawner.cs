using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a SolarSystemData into live GameObjects: star at the centre, planets orbiting it, moons
/// orbiting planets, optional orbit lines and click colliders. Only one system is alive at a time;
/// Spawn() destroys the previous one (including its materials and textures).
/// </summary>
public class SolarSystemSpawner : MonoBehaviour
{
    [SerializeField] PlanetMaterialLibrary library;

    [Header("Look")]
    [Tooltip("Pixel density of planets. Higher = finer pixels.")]
    [SerializeField] float pixelsPerUnit = 16f;
    [Tooltip("Planets render behind ships at this sorting order (each body uses 4 slots).")]
    [SerializeField] int sortingOrderBase = -1000;
    [SerializeField] bool drawOrbits = true;
    [SerializeField] float orbitLineWidth = 0.25f;
    [SerializeField] Color orbitLineColor = new Color(1f, 1f, 1f, 0.15f);

    [Header("Gameplay")]
    [Tooltip("Adds a trigger CircleCollider2D to every body so you can click / select them.")]
    [SerializeField] bool addColliders = true;

    Transform root;
    Material lineMat;

    public SolarSystemData Current { get; private set; }
    public readonly List<CelestialBody> Bodies = new List<CelestialBody>();

    public Transform Spawn(SolarSystemData data)
    {
        if (library == null)
        {
            Debug.LogError("SolarSystemSpawner: assign a PlanetMaterialLibrary.", this);
            return null;
        }

        Clear();
        Current = data;

        root = new GameObject("System_" + data.name).transform;
        root.SetParent(transform, false);

        var tfs = new Transform[data.bodies.Count];
        for (int i = 0; i < data.bodies.Count; i++)
        {
            var b = data.bodies[i];
            var parent = b.parentIndex >= 0 ? tfs[b.parentIndex] : root;

            var go = new GameObject(b.name);
            go.transform.SetParent(parent, false);
            tfs[i] = go.transform;

            var cb = go.AddComponent<CelestialBody>();
            cb.Init(b, i);
            Bodies.Add(cb);

            bool isStar = b.type == PlanetType.Star;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.AddComponent<PlanetRenderer>().Build(
                library, b.type, b.seed, b.diameter, pixelsPerUnit, sortingOrderBase + i * 4,
                isStar ? null : tfs[0],                       // lit by the star
                isStar ? StarHue(data.starClass) : 0f);

            if (addColliders)
            {
                var col = go.AddComponent<CircleCollider2D>();
                col.radius = b.diameter * 0.5f;
                col.isTrigger = true;
            }

            if (drawOrbits && !b.isBelt && b.orbitRadius > 0f)
                AddOrbitLine(parent, b.orbitRadius);
        }
        return root;
    }

    public void SetVisible(bool visible)
    {
        if (root != null) root.gameObject.SetActive(visible);
    }

    public void Clear()
    {
        if (root != null) Destroy(root.gameObject);
        root = null;
        Bodies.Clear();
        Current = null;
        if (lineMat != null) Destroy(lineMat);
        lineMat = null;
    }

    void AddOrbitLine(Transform parent, float radius)
    {
        if (lineMat == null) lineMat = new Material(Shader.Find("Sprites/Default"));

        var go = new GameObject("OrbitLine");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = lineMat;
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.widthMultiplier = orbitLineWidth;
        lr.startColor = orbitLineColor;
        lr.endColor = orbitLineColor;
        lr.sortingOrder = sortingOrderBase - 1;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        const int N = 128;
        lr.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
    }

    // The default star palette is teal (hue ~0.47); shift it to the spectral class colour.
    static float StarHue(StarClass c)
    {
        float target = c switch
        {
            StarClass.RedDwarf => 0.00f,
            StarClass.Orange => 0.06f,
            StarClass.Yellow => 0.12f,
            StarClass.White => 0.50f,
            _ => 0.60f
        };
        return target - 0.47f;
    }

    void OnDestroy() => Clear();
}
