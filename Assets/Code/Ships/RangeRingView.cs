using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fuel range overlay for selected ships (GDD 4.1), drawn from ShipData only.
///   Dim circle      : how far the ship can fly with its current fuel.
///   Bright ellipse  : the point of no return - every point it can reach AND still get back from to the
///                     nearest refuel point (foci = ship and refuel point, total path = range).
///                     With no refuel point in the system, the return point is the ship itself, so this
///                     becomes a circle of half the range.
/// </summary>
public class RangeRingView : MonoBehaviour
{
    [SerializeField] FleetManager fleet;
    [SerializeField] SelectionManager selection;
    [SerializeField] int maxShips = 8;
    [SerializeField] Color rangeColor = new Color(0.5f, 0.8f, 1f, 0.25f);
    [SerializeField] Color pointOfNoReturnColor = new Color(1f, 0.7f, 0.2f, 0.9f);

    readonly List<LineRenderer> rangeLines = new List<LineRenderer>();
    readonly List<LineRenderer> returnLines = new List<LineRenderer>();
    Camera cam;

    void Start()
    {
        if (fleet == null) fleet = FindFirstObjectByType<FleetManager>();
        if (selection == null) selection = FindFirstObjectByType<SelectionManager>();
        cam = Camera.main;
        if (fleet == null || selection == null)
        {
            Debug.LogError("RangeRingView: needs a FleetManager and a SelectionManager.", this);
            enabled = false;
        }
    }

    void LateUpdate()
    {
        int used = 0;
        if (!fleet.MapOpen)
        {
            int viewed = fleet.ViewedSystemId;
            foreach (var s in selection.Selected)
            {
                if (used >= maxShips) break;
                if (s.systemId != viewed || s.state == ShipState.InTransit) continue;
                Draw(used++, s);
            }
        }

        for (int i = used; i < rangeLines.Count; i++)
        {
            rangeLines[i].enabled = false;
            returnLines[i].enabled = false;
        }
    }

    void Draw(int i, ShipData s)
    {
        while (rangeLines.Count <= i)
        {
            rangeLines.Add(LineShapes.Create(transform, "Range", rangeColor, 0.1f, 5, 96, true));
            returnLines.Add(LineShapes.Create(transform, "PointOfNoReturn", pointOfNoReturnColor, 0.1f, 6, 96, true));
        }

        var range = rangeLines[i];
        var back = returnLines[i];
        if (cam == null) cam = Camera.main;
        float width = Mathf.Max(0.1f, cam != null ? cam.orthographicSize * 0.004f : 0.1f);
        range.widthMultiplier = back.widthMultiplier = width;

        float L = fleet.MoveRange(s);
        if (L < 0.01f)
        {
            range.enabled = back.enabled = false;
            return;
        }

        Vector3 origin = fleet.SystemOrigin;
        Vector2 S = s.position;

        range.enabled = true;
        LineShapes.SetEllipse(range, origin + (Vector3)S, L, L, 0f);

        Vector2 R = fleet.TryGetNearestRefuel(s, out Vector2 depot) ? depot : S;
        Vector2 SR = R - S;
        float d = SR.magnitude;

        if (L <= d)       // can't even make it back to the depot
        {
            back.enabled = false;
            return;
        }

        float a = L * 0.5f;
        float c = d * 0.5f;
        float b = Mathf.Sqrt(a * a - c * c);
        back.enabled = true;
        LineShapes.SetEllipse(back, origin + (Vector3)((S + R) * 0.5f), a, b, Mathf.Atan2(SR.y, SR.x));
    }
}
