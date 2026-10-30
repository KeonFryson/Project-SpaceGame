using System.Collections.Generic;
using UnityEngine;

public enum PlanetType
{
    Terran,       // land masses + clouds
    RiverWorld,   // land with rivers + clouds
    Desert,       // dry terran
    Ice,
    Lava,
    GasGiant,
    RingedGiant,
    Barren,       // no atmosphere, craters (also used for moons)
    Asteroid,
    Star
}

/// <summary>
/// World-space version of the UniPixelPlanet planets. Put it on an empty child object; the object's
/// scale becomes the planet diameter. Each shader layer is drawn on its own quad using a Material
/// instance copied from the PlanetMaterialLibrary. Everything (palette, seed, rotation, clouds) is
/// derived deterministically from the seed, so the same seed always gives the same planet.
/// </summary>
[DisallowMultipleComponent]
public class PlanetRenderer : MonoBehaviour
{
    sealed class Layer
    {
        public Material mat;
        public float timeMul = 1f;
        public float lightMul = 1f;
        public float rotAdd;
        public bool spin;
    }

    static readonly int PixelsId = Shader.PropertyToID("_Pixels");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int RotId = Shader.PropertyToID("_Rotation");
    static readonly int LightId = Shader.PropertyToID("_Light_origin");
    static readonly int TimeId = Shader.PropertyToID("time");
    static readonly int SizeId = Shader.PropertyToID("_Size");
    static readonly int GradId = Shader.PropertyToID("_GradientTex");
    static readonly int ColorSchemeId = Shader.PropertyToID("_ColorScheme");
    static readonly int DarkSchemeId = Shader.PropertyToID("_Dark_ColorScheme");

    static readonly string[] C2 = { "_Color1", "_Color2" };
    static readonly string[] C3 = { "_Color1", "_Color2", "_Color3" };
    static readonly string[] C4 = { "_Color1", "_Color2", "_Color3", "_Color4" };
    static readonly string[] Cloud = { "_Base_color", "_Outline_color", "_Shadow_Base_color", "_Shadow_Outline_color" };
    static readonly string[] GasCloud = { "_Base_color", "_Outline_color", "_Shadow_base_color", "_Shadow_outline_color" };
    static readonly string[] River6 = { "_Color1", "_Color2", "_Color3", "_Color4", "_River_color", "_River_color_dark" };

    [Tooltip("How far from the planet centre the 'light' sits in shader UV space. Bigger = flatter lighting.")]
    [SerializeField] float lightDistance = 0.35f;
    [SerializeField] float timeScale = 1f;

    readonly List<Layer> layers = new List<Layer>();
    readonly List<Texture2D> textures = new List<Texture2D>();
    static Mesh quad;

    Transform lightTarget;
    float buildPixels, rotation, timeOffset, spinSpeed;
    int baseSorting;

    // ------------------------------------------------------------------ public API

    /// <param name="diameter">World-space diameter (sets this object's scale).</param>
    /// <param name="pixelsPerUnit">Pixel density. Keeping this constant makes every planet's pixels the same size on screen.</param>
    /// <param name="lightSource">Usually the star's transform. Null = leave the material's light as is.</param>
    /// <param name="starHueShift">Only used for PlanetType.Star (tints the star by spectral class).</param>
    public void Build(PlanetMaterialLibrary lib, PlanetType type, int seed, float diameter,
                      float pixelsPerUnit, int sortingOrder, Transform lightSource = null, float starHueShift = 0f)
    {
        Clear();

        var rng = new System.Random(seed);
        float F(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        // 50% "natural" palette (tiny shift), 50% alien (up to +-max hue shift)
        float Alien(float max) => rng.NextDouble() < 0.5 ? F(-0.03f, 0.03f) : F(-max, max);

        transform.localScale = Vector3.one * diameter;
        buildPixels = Mathf.Clamp(Mathf.Round(diameter * pixelsPerUnit), 16f, 300f);
        baseSorting = sortingOrder;
        lightTarget = lightSource;
        rotation = F(0f, 6.28f);
        timeOffset = F(0f, 60f);
        spinSpeed = F(-0.5f, 0.5f);
        // The shaders' _Seed property is range(1,10) and is read as an int. A value below 1 truncates to 0,
        // which makes every noise function return 0 -> a flat, single-colour planet. Keep it in 1..10.
        float seedValue = 1f + Mathf.Abs(seed % 1000) / 1000f * 9f;

        switch (type)
        {
            case PlanetType.Terran:
                {
                    float h = Alien(0.4f);
                    Add(lib.planetUnder, C3, new[] { "#92E8C0", "#4FA4B8", "#2C354D" }, h);
                    Add(lib.planetLandMass, C4, new[] { "#C8D45D", "#63AB3F", "#2F5753", "#283540" }, h);
                    var c = Add(lib.clouds, Cloud, new[] { "#DFE0E8", "#A3A7C2", "#686F99", "#404973" }, 0f, 0.5f);
                    SetF(c, "_Cloud_cover", F(0.35f, 0.6f));
                    break;
                }
            case PlanetType.RiverWorld:
                {
                    float h = Alien(0.4f);
                    Add(lib.landRivers, River6, new[] { "#63AB3F", "#3B7D4F", "#2F5753", "#283540", "#4FA4B8", "#404973" }, h, 0.5f);
                    var c = Add(lib.cloudsRiver, Cloud, new[] { "#FFFFFF", "#DFE0E8", "#686F99", "#404973" }, 0f, 0.25f);
                    SetF(c, "_Cloud_cover", F(0.35f, 0.6f));
                    break;
                }
            case PlanetType.Desert:
                {
                    float h = Alien(0.5f);
                    var l = Add(lib.dryTerran, null, null, 0f);
                    if (l != null)
                        l.mat.SetTexture(GradId, MakeGradient(
                            new[] { "#ff8933", "#e64539", "#ad2f45", "#52333f", "#3d2936" },
                            new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f }, h));
                    break;
                }
            case PlanetType.Ice:
                {
                    float h = Alien(0.25f);
                    Add(lib.icePlanetUnder, C3, new[] { "#faffff", "#c7d4e1", "#928fb8" }, h);
                    Add(lib.lakes, C3, new[] { "#4fa4b8", "#4c6885", "#3a3f5e" }, h);
                    Add(lib.iceClouds, Cloud, new[] { "#e1f2ff", "#c0e3ff", "#5e70a5", "#404973" }, 0f, 0.5f);
                    break;
                }
            case PlanetType.Lava:
                {
                    float h = F(-0.06f, 0.06f);
                    Add(lib.lavaPlanetUnder, C3, new[] { "#8f4d57", "#52333f", "#3d2936" }, h, 0.5f);
                    Add(lib.lavaCraters, C2, new[] { "#52333f", "#3d2936" }, h, 0.5f);
                    Add(lib.lavaRivers, C3, new[] { "#ff8933", "#e64539", "#ad2f45" }, h, 0.5f);
                    break;
                }
            case PlanetType.GasGiant:
                {
                    float h = F(-0.5f, 0.5f);
                    Add(lib.gasPlanet, GasCloud, new[] { "#3b2027", "#3b2027", "#21181b", "#21181b" }, h, 0.5f);
                    var c = Add(lib.gasPlanet2, GasCloud, new[] { "#f0b541", "#cf752b", "#ab5130", "#7d3833" }, h, 0.5f);
                    SetF(c, "_Cloud_cover", F(0.28f, 0.5f));
                    break;
                }
            case PlanetType.RingedGiant:
                {
                    float h = F(-0.5f, 0.5f);
                    var times = new[] { 0f, 0.5f, 1f };
                    var lightTex = MakeGradient(new[] { "#eec39a", "#d9a066", "#8f563b" }, times, h);
                    var darkTex = MakeGradient(new[] { "#663931", "#45283c", "#222034" }, times, h);
                    var gas = Add(lib.gasLayers, null, null, 0f, 0.5f, 1f, 1.3f);
                    var ring = Add(lib.ring, null, null, 0f, -1.5f, 3f, 1.3f);   // ring quad is 3x the planet quad
                    if (ring != null) ring.rotAdd = 0.7f;
                    foreach (var l in new[] { gas, ring })
                    {
                        if (l == null) continue;
                        l.mat.SetTexture(ColorSchemeId, lightTex);
                        l.mat.SetTexture(DarkSchemeId, darkTex);
                    }
                    break;
                }
            case PlanetType.Barren:
                {
                    float h = Alien(0.5f);
                    Add(lib.noAtmosphere, C3, new[] { "#A3A7C2", "#4C6885", "#3A3F5E" }, h, 0.5f);
                    Add(lib.craters, C2, new[] { "#4C6885", "#3A3F5E" }, h, 0.5f);
                    break;
                }
            case PlanetType.Asteroid:
                {
                    var l = Add(lib.asteroid, C3, new[] { "#a3a7c2", "#4c6885", "#3a3f5e" }, Alien(0.3f), 0f);
                    if (l != null) l.spin = true;
                    break;
                }
            case PlanetType.Star:
                {
                    float h = starHueShift;
                    Add(lib.starBlobs, new[] { "_Color1" }, new[] { "#ffffe4" }, h, 1f, 2f);
                    var body = Add(lib.star, null, null, 0f, 0.1f);
                    var flares = Add(lib.starFlares, null, null, 0f, 1f, 2f);
                    if (body != null)
                        body.mat.SetTexture(GradId, MakeGradient(
                            new[] { "#f5ffe8", "#77d6c1", "#1c92a7", "#033e5e" }, new[] { 0f, 0.33f, 0.66f, 1f }, h));
                    if (flares != null)
                        flares.mat.SetTexture(GradId, MakeGradient(
                            new[] { "#77d6c1", "#ffffe4" }, new[] { 0f, 1f }, h));
                    break;
                }
        }

        foreach (var l in layers)
        {
            l.mat.SetFloat(SeedId, seedValue);
            l.mat.SetFloat(RotId, rotation + l.rotAdd);
            // +-10% noise scale per layer gives extra variety on top of the (coarse) seed value
            if (type != PlanetType.Star && l.mat.HasProperty(SizeId))
                l.mat.SetFloat(SizeId, l.mat.GetFloat(SizeId) * F(0.9f, 1.1f));
        }

        Refresh();
    }

    public void SetSortingLayer(int sortingLayerId)
    {
        foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.sortingLayerID = sortingLayerId;
    }

    // ------------------------------------------------------------------ internals

    Layer Add(Material template, string[] props, string[] hex, float hue,
              float timeMul = 1f, float pixelMul = 1f, float lightMul = 1f)
    {
        if (template == null)
        {
            Debug.LogError("PlanetRenderer: a template material is missing in the PlanetMaterialLibrary.", this);
            return null;
        }

        var m = new Material(template) { name = template.name + " (planet)" };
        m.renderQueue = 3000 + layers.Count;   // explicit order: the shaders are alpha-blended with ZWrite off

        if (props != null)
            for (int i = 0; i < props.Length; i++)
                m.SetColor(props[i], Shift(Hex(hex[i]), hue));

        m.SetFloat(PixelsId, Mathf.Min(buildPixels * pixelMul, 600f));

        var layer = new Layer { mat = m, timeMul = timeMul, lightMul = lightMul };

        var go = new GameObject("Layer" + layers.Count);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * pixelMul;   // pixels*N layers are N times larger, like in the original
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.sortingOrder = baseSorting + layers.Count;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        layers.Add(layer);
        return layer;
    }

    static void SetF(Layer l, string prop, float v)
    {
        if (l != null) l.mat.SetFloat(prop, v);
    }

    Texture2D MakeGradient(string[] hex, float[] times, float hue)
    {
        var keys = new GradientColorKey[hex.Length];
        for (int i = 0; i < hex.Length; i++)
            keys[i] = new GradientColorKey(Shift(Hex(hex[i]), hue), times[i]);

        var g = new Gradient();
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        var tex = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[128];
        for (int x = 0; x < 128; x++) px[x] = g.Evaluate(x / 128f);
        tex.SetPixels(px);
        tex.Apply();
        textures.Add(tex);
        return tex;
    }

    static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString(h, out var c);
        return c;
    }

    static Color Shift(Color c, float hue)
    {
        if (Mathf.Approximately(hue, 0f)) return c;
        Color.RGBToHSV(c, out float H, out float S, out float V);
        var o = Color.HSVToRGB(Mathf.Repeat(H + hue, 1f), S, V);
        o.a = c.a;
        return o;
    }

    static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "PlanetQuad" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
            new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f)
        };
        quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.RecalculateBounds();
        return quad;
    }

    void Update() => Refresh();

    void Refresh()
    {
        float t = Time.time * timeScale + timeOffset;

        bool lit = lightTarget != null;
        Vector2 lightPos = Vector2.zero;
        if (lit)
        {
            Vector2 dir = lightTarget.position - transform.position;
            if (dir.sqrMagnitude > 1e-4f) dir.Normalize();
            lightPos = new Vector2(0.5f, 0.5f) + dir * lightDistance;
        }

        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            l.mat.SetFloat(TimeId, t * l.timeMul);
            if (lit) l.mat.SetVector(LightId, lightPos * l.lightMul);
            if (l.spin) l.mat.SetFloat(RotId, Mathf.Repeat(rotation + Time.time * spinSpeed, 6.2831f));
        }
    }

    void Clear()
    {
        ReleaseResources();
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
    }

    void ReleaseResources()
    {
        foreach (var l in layers) if (l.mat != null) Destroy(l.mat);
        foreach (var t in textures) if (t != null) Destroy(t);
        layers.Clear();
        textures.Clear();
    }

    void OnDestroy() => ReleaseResources();
}