using UnityEngine;

/// <summary>Shared LineRenderer helpers (rings, ellipses). One material for everything.</summary>
public static class LineShapes
{
    static Material mat;

    static Material Mat
    {
        get
        {
            if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
            return mat;
        }
    }

    public static LineRenderer Create(Transform parent, string name, Color color, float width,
                                      int sortingOrder, int segments, bool worldSpace)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = Mat;
        lr.useWorldSpace = worldSpace;
        lr.loop = true;
        lr.widthMultiplier = width;
        lr.startColor = lr.endColor = color;
        lr.sortingOrder = sortingOrder;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = segments;
        return lr;
    }

    public static LineRenderer Circle(Transform parent, string name, float radius, Color color, float width,
                                      int sortingOrder, int segments = 64)
    {
        var lr = Create(parent, name, color, width, sortingOrder, segments, false);
        SetEllipse(lr, Vector3.zero, radius, radius, 0f);
        return lr;
    }

    /// <summary>Writes an ellipse (semi-axes a, b, rotated by angleRad) around center, in the line's own space.</summary>
    public static void SetEllipse(LineRenderer lr, Vector3 center, float a, float b, float angleRad)
    {
        int n = lr.positionCount;
        float cs = Mathf.Cos(angleRad), sn = Mathf.Sin(angleRad);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n * Mathf.PI * 2f;
            float x = Mathf.Cos(t) * a, y = Mathf.Sin(t) * b;
            lr.SetPosition(i, center + new Vector3(x * cs - y * sn, x * sn + y * cs, 0f));
        }
    }
}

/// <summary>
/// The GameObject for one ShipData - it exists only while that ship's system is on screen. It owns no
/// game state: every frame it copies position/rotation from the data. Damage goes straight to the data.
/// If the class has no prefab, a tinted triangle is generated (and kept a minimum size on screen so
/// ships stay visible when zoomed far out).
/// </summary>
public class ShipView : MonoBehaviour, IDamageable
{
    const float MinScreenFraction = 0.025f;   // of the camera's half-height
    static Sprite triangle;

    public ShipData Data { get; private set; }

    FleetManager fleet;
    ShipClassData cls;
    Camera cam;
    Transform visual;
    LineRenderer ring;
    ForceFieldController forceField;

    /// <summary>World size to draw/pick a ship at, never smaller than a few pixels on screen.</summary>
    public static float EffectiveSize(float size, Camera cam) =>
        cam != null && cam.orthographic ? Mathf.Max(size, cam.orthographicSize * MinScreenFraction) : size;

    public static ShipView Create(FleetManager fleet, ShipData data, ShipClassData cls, Transform parent)
    {
        bool placeholder = cls.prefab == null;
        GameObject go;
        if (placeholder)
        {
            go = new GameObject();
            go.transform.SetParent(parent, false);
        }
        else go = Instantiate(cls.prefab, parent);

        go.name = data.name;
        var v = go.GetComponent<ShipView>();
        if (v == null) v = go.AddComponent<ShipView>();
        v.Init(fleet, data, cls, placeholder);
        return v;
    }

    void Init(FleetManager fleet, ShipData data, ShipClassData cls, bool placeholder)
    {
        this.fleet = fleet;
        Data = data;
        this.cls = cls;
        cam = Camera.main;

        if (placeholder) BuildPlaceholder();
        forceField = GetComponentInChildren<ForceFieldController>(true);

        // so projectiles can hit it
        if (GetComponent<Rigidbody2D>() == null)
            gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        if (GetComponent<Collider2D>() == null)
        {
            var col = gameObject.AddComponent<PolygonCollider2D>();
            col.isTrigger = true;
        }

        ring = LineShapes.Create(transform, "SelectionRing", new Color(0.3f, 1f, 0.5f, 0.9f), 0.1f, 20, 48, false);
        ring.enabled = false;

        Sync();
    }

    void BuildPlaceholder()
    {
        var vgo = new GameObject("Visual");
        vgo.transform.SetParent(transform, false);
        visual = vgo.transform;

        var sr = vgo.AddComponent<SpriteRenderer>();
        sr.sprite = Triangle();
        sr.sortingOrder = 10;   // planets sit around -1000
        sr.color = Data.owner == FleetManager.PlayerOwner ? new Color(0.45f, 0.9f, 1f) : new Color(1f, 0.4f, 0.35f);
    }

    static Sprite Triangle()
    {
        if (triangle != null) return triangle;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float v = y / (float)(S - 1);                       // 0 = tail, 1 = nose
                float dx = Mathf.Abs((x + 0.5f) / S - 0.5f);
                px[y * S + x] = dx <= (1f - v) * 0.5f ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        triangle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);   // 1 world unit
        return triangle;
    }

    void LateUpdate() => Sync();

    void Sync()
    {
        if (Data == null) return;

        transform.SetPositionAndRotation(fleet.SystemOrigin + (Vector3)Data.position,
                                         Quaternion.Euler(0f, 0f, Data.rotation));

        if (cam == null) cam = Camera.main;
        float size = EffectiveSize(cls.visualSize, cam);
        if (visual != null) visual.localScale = Vector3.one * size;

        if (ring != null && ring.enabled)
        {
            ring.widthMultiplier = Mathf.Max(0.08f, cam != null ? cam.orthographicSize * 0.003f : 0.1f);
            LineShapes.SetEllipse(ring, Vector3.zero, size * 0.8f, size * 0.8f, 0f);
        }
    }

    public void SetSelected(bool on)
    {
        if (ring != null) ring.enabled = on;
    }

    // IDamageable - the data is the truth; FleetManager removes the ship when hp <= 0
    public void TakeDamage(int damage)
    {
        if (Data != null) Data.hp -= damage;
    }

    /// <summary>Call from Projectile so an assigned ForceFieldController ripples at the impact point.</summary>
    public void OnProjectileHit(Vector2 worldPoint)
    {
        if (forceField != null && forceField.isActiveAndEnabled) forceField.Hit(worldPoint, 1f);
    }
}
