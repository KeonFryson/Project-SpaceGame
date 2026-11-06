using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Draws every system in the galaxy as a coloured dot using ONE mesh (one draw call), so it scales
/// to very large galaxies. Click a dot to raise SystemClicked(id).
/// IMPORTANT: put this on its own child object (not on the GalaxyManager object) - Hide() deactivates it.
/// </summary>
public class GalaxyMapView : MonoBehaviour
{
    [SerializeField] float starSize = 5f;
    [SerializeField] float markerRadius = 7f;

    public System.Action<int> SystemClicked;
    public System.Action<int> SystemRightClicked;
    public bool IsOpen => gameObject.activeSelf;

    GalaxyData galaxy;
    Mesh mesh;
    Material dotMat, lineMat;
    Texture2D dot;
    LineRenderer marker;
    Camera cam;

    public void Show(GalaxyData g, int currentSystemId)
    {
        gameObject.SetActive(true);
        cam = Camera.main;
        if (galaxy != g) { galaxy = g; Build(g); }

        if (marker != null)
        {
            bool has = currentSystemId >= 0 && currentSystemId < g.systems.Count;
            marker.gameObject.SetActive(has);
            if (has) marker.transform.localPosition = g.systems[currentSystemId].position;
        }
    }

    public void Hide() => gameObject.SetActive(false);



    void Update()
    {
        if (galaxy == null || Mouse.current == null) return;
        bool left = Mouse.current.leftButton.wasPressedThisFrame;
        bool right = Mouse.current.rightButton.wasPressedThisFrame;
        if (!left && !right) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 w = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        int id = galaxy.FindNearest(w, starSize * 1.5f + cam.orthographicSize * 0.03f);
        if (id < 0) return;
        if (left)
        { Debug.Log($"GalaxyMapView: left-click system {id}");
            SystemClicked?.Invoke(id); }
        else
        { Debug.Log($"GalaxyMapView: right-click system {id}");
            SystemRightClicked?.Invoke(id); } 
    }

    void Build(GalaxyData g)
    {
        ReleaseMesh();

        if (dot == null) dot = MakeDot();
        if (dotMat == null)
        {
            dotMat = new Material(Shader.Find("Sprites/Default"));
            dotMat.mainTexture = dot;
        }

        int n = g.systems.Count;
        var verts = new Vector3[n * 4];
        var uvs = new Vector2[n * 4];
        var cols = new Color32[n * 4];
        var tris = new int[n * 6];
        float h = starSize * 0.5f;

        for (int i = 0; i < n; i++)
        {
            var s = g.systems[i];
            Vector2 p = s.position;
            int v = i * 4;
            verts[v] = new Vector3(p.x - h, p.y - h);
            verts[v + 1] = new Vector3(p.x + h, p.y - h);
            verts[v + 2] = new Vector3(p.x + h, p.y + h);
            verts[v + 3] = new Vector3(p.x - h, p.y + h);
            uvs[v] = new Vector2(0, 0);
            uvs[v + 1] = new Vector2(1, 0);
            uvs[v + 2] = new Vector2(1, 1);
            uvs[v + 3] = new Vector2(0, 1);

            Color32 c = StarColor(s.starClass);
            cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;

            int t = i * 6;
            tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }

        mesh = new Mesh
        {
            name = "GalaxyMap",
            indexFormat = n * 4 > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                        : UnityEngine.Rendering.IndexFormat.UInt16
        };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors32 = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        var mf = GetComponent<MeshFilter>();
        if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = dotMat;
        mr.sortingOrder = 100;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (marker == null) CreateMarker();
    }

    void CreateMarker()
    {
        lineMat = new Material(Shader.Find("Sprites/Default"));
        var go = new GameObject("CurrentSystemMarker");
        go.transform.SetParent(transform, false);
        marker = go.AddComponent<LineRenderer>();
        marker.sharedMaterial = lineMat;
        marker.useWorldSpace = false;
        marker.loop = true;
        marker.widthMultiplier = 1f;
        marker.startColor = marker.endColor = new Color(0.3f, 1f, 0.6f, 0.9f);
        marker.sortingOrder = 101;
        const int N = 48;
        marker.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            marker.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * markerRadius);
        }
    }

    static Texture2D MakeDot()
    {
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S * 0.5f, S * 0.5f)) / (S * 0.5f);
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, 1f - d));
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Color32 StarColor(StarClass c) => c switch
    {
        StarClass.RedDwarf => new Color32(255, 90, 65, 255),
        StarClass.Orange => new Color32(255, 165, 77, 255),
        StarClass.Yellow => new Color32(255, 242, 140, 255),
        StarClass.White => new Color32(230, 242, 255, 255),
        _ => new Color32(140, 180, 255, 255)
    };

    void ReleaseMesh()
    {
        if (mesh != null) Destroy(mesh);
        mesh = null;
    }

    void OnDestroy()
    {
        ReleaseMesh();
        if (dotMat != null) Destroy(dotMat);
        if (lineMat != null) Destroy(lineMat);
        if (dot != null) Destroy(dot);
    }
}
