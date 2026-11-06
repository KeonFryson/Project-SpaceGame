using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// RTS input. Selection works on ShipData (not colliders), so it needs no GameObjects.
///   Left click / drag box : select (Shift = add)
///   Right click           : move selected ships there (Shift = queue)
///   Galaxy map open (M)   : right-click a system to warp the selected ships
///   Debug: F = refuel selected, X = drop a refuel point at the cursor
/// Selection is kept by ship id, so it survives warping and switching systems.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    [SerializeField] FleetManager fleet;
    [SerializeField] Camera cam;
    [SerializeField] float dragThreshold = 8f;
    [SerializeField] float formationSpacing = 2f;
    [SerializeField] bool debugKeys = true;

    readonly List<ShipData> selected = new List<ShipData>();
    readonly HashSet<int> ids = new HashSet<int>();
    readonly List<ShipData> scratch = new List<ShipData>();

    public IReadOnlyList<ShipData> Selected => selected;
    public bool IsSelected(int shipId) => ids.Contains(shipId);
    public event Action Changed;

    bool pressing, dragging;
    Vector2 pressPos;

    void Start()
    {
        if (fleet == null) fleet = FindFirstObjectByType<FleetManager>();
        if (cam == null) cam = Camera.main;
        if (fleet == null)
        {
            Debug.LogError("SelectionManager: no FleetManager found.", this);
            enabled = false;
            return;
        }

        fleet.ViewCreated += OnViewCreated;
        fleet.ShipRemoved += OnShipRemoved;
        if (fleet.Manager != null && fleet.Manager.MapView != null)
            fleet.Manager.MapView.SystemRightClicked += OnMapRightClick;
        else Debug.LogWarning("SelectionManager: no GalaxyMapView found. Map right-clicks will not work.", this);
    }

    void OnDestroy()
    {
        if (fleet == null) return;
        fleet.ViewCreated -= OnViewCreated;
        fleet.ShipRemoved -= OnShipRemoved;
        if (fleet.Manager != null && fleet.Manager.MapView != null)
            fleet.Manager.MapView.SystemRightClicked -= OnMapRightClick;
    }

    void Update()
    {
        if (Mouse.current == null || fleet == null) return;
        if (fleet.MapOpen) { pressing = dragging = false; return; }   // the map handles its own clicks
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        HandleSelect();
        HandleMoveOrder();
        if (debugKeys) HandleDebug();
    }

    // ------------------------------------------------------------------ selecting

    void HandleSelect()
    {
        var mouse = Mouse.current;
        Vector2 mp = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressing = !PointerOverUI();
            dragging = false;
            pressPos = mp;
        }

        if (pressing && !dragging && mouse.leftButton.isPressed &&
            (mp - pressPos).sqrMagnitude > dragThreshold * dragThreshold)
            dragging = true;

        if (pressing && mouse.leftButton.wasReleasedThisFrame)
        {
            bool add = ShiftHeld();
            if (dragging) BoxSelect(pressPos, mp, add);
            else ClickSelect(mp, add);
            pressing = dragging = false;
        }
    }

    void ClickSelect(Vector2 screen, bool add)
    {
        Vector2 w = World(screen);
        int viewed = fleet.ViewedSystemId;
        ShipData best = null;
        float bestSq = float.MaxValue;

        foreach (var s in fleet.Ships)
        {
            if (s.owner != FleetManager.PlayerOwner || s.systemId != viewed) continue;
            float r = ShipView.EffectiveSize(fleet.ClassOf(s.shipClass).visualSize, cam) * 0.9f;
            float sq = (ShipWorld(s) - w).sqrMagnitude;
            if (sq <= r * r && sq < bestSq) { bestSq = sq; best = s; }
        }

        if (best == null)
        {
            if (!add && selected.Count > 0) { ClearInternal(); NotifyChanged(); }
            return;
        }

        if (!add) ClearInternal();
        if (add && ids.Contains(best.id)) Remove(best); else Add(best);
        NotifyChanged();
    }

    void BoxSelect(Vector2 a, Vector2 b, bool add)
    {
        Vector2 wa = World(a), wb = World(b);
        var rect = Rect.MinMaxRect(Mathf.Min(wa.x, wb.x), Mathf.Min(wa.y, wb.y),
                                   Mathf.Max(wa.x, wb.x), Mathf.Max(wa.y, wb.y));
        int viewed = fleet.ViewedSystemId;

        if (!add) ClearInternal();
        foreach (var s in fleet.Ships)
            if (s.owner == FleetManager.PlayerOwner && s.systemId == viewed && rect.Contains(ShipWorld(s)))
                Add(s);
        NotifyChanged();
    }

    // ------------------------------------------------------------------ orders

    void HandleMoveOrder()
    {
        if (!Mouse.current.rightButton.wasPressedThisFrame || selected.Count == 0 || PointerOverUI()) return;

        int viewed = fleet.ViewedSystemId;
        scratch.Clear();
        foreach (var s in selected)
            if (s.systemId == viewed && s.state != ShipState.InTransit) scratch.Add(s);
        if (scratch.Count == 0) return;

        Vector2 local = World(Mouse.current.position.ReadValue()) - (Vector2)fleet.SystemOrigin;
        bool queue = ShiftHeld();
        int n = scratch.Count;
        float radius = n > 1 ? formationSpacing * Mathf.Sqrt(n) * 0.5f : 0f;

        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            fleet.OrderMove(scratch[i], local + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, queue);
        }
    }

    void OnMapRightClick(int destSystem)
    {
        bool queue = ShiftHeld();
        scratch.Clear();
        scratch.AddRange(selected);
        foreach (var s in scratch) fleet.OrderWarp(s, destSystem, queue, true);
    }

    void HandleDebug()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.fKey.wasPressedThisFrame)
            foreach (var s in selected) fleet.Refuel(s);

        if (kb.xKey.wasPressedThisFrame && fleet.ViewedSystemId >= 0)
            fleet.AddRefuelPoint(fleet.ViewedSystemId,
                World(Mouse.current.position.ReadValue()) - (Vector2)fleet.SystemOrigin);
    }

    // ------------------------------------------------------------------ bookkeeping

    void Add(ShipData s)
    {
        if (ids.Add(s.id)) selected.Add(s);
    }

    void Remove(ShipData s)
    {
        if (ids.Remove(s.id)) selected.Remove(s);
    }

    void ClearInternal()
    {
        selected.Clear();
        ids.Clear();
    }

    void NotifyChanged()
    {
        int viewed = fleet.ViewedSystemId;
        foreach (var s in fleet.Ships)
        {
            if (s.systemId != viewed) continue;
            var v = fleet.GetView(s.id);
            if (v != null) v.SetSelected(ids.Contains(s.id));
        }
        Changed?.Invoke();
    }

    void OnViewCreated(ShipView v) => v.SetSelected(ids.Contains(v.Data.id));

    void OnShipRemoved(ShipData s)
    {
        if (!ids.Contains(s.id)) return;
        Remove(s);
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ helpers

    Vector2 World(Vector2 screen) => cam.ScreenToWorldPoint(screen);

    Vector2 ShipWorld(ShipData s) => (Vector2)fleet.SystemOrigin + s.position;

    static bool ShiftHeld() =>
        Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

    static bool PointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    // drag-box rectangle
    void OnGUI()
    {
        if (!dragging || Mouse.current == null) return;

        Vector2 m = Mouse.current.position.ReadValue();
        var r = Rect.MinMaxRect(Mathf.Min(pressPos.x, m.x), Screen.height - Mathf.Max(pressPos.y, m.y),
                                Mathf.Max(pressPos.x, m.x), Screen.height - Mathf.Min(pressPos.y, m.y));

        var prev = GUI.color;
        GUI.color = new Color(0.3f, 1f, 0.5f, 0.15f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.3f, 1f, 0.5f, 0.9f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }
}
