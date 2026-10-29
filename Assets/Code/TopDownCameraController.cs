using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// RTS/DK-style top-down camera controller. Sits on the camera rig (an empty parent of the
/// actual Camera, or the Camera itself if you don't need to tilt it). Pans on the XZ plane
/// (or XY if useXYPlane is set for a 2D-style top-down game) and zooms an orthographic camera.
///
/// Movement inputs, in priority order — the first one active each frame wins so they don't fight:
///   1. Middle-mouse drag pan
///   2. WASD / arrow keys
///   3. Edge-of-screen pan (optional, off by default so it doesn't fire in the editor when the
///      mouse is just resting near a panel)
///
/// Zoom is mouse-scroll-wheel driven and (for an orthographic camera) changes orthographic
/// size directly; for a perspective camera it dollies the rig forward/back along its own
/// look direction instead.
///
/// Optionally clamps the pan position to a world-space rectangle, which you can wire up to
/// ExcavationManager's grid bounds so the camera can't wander off into the void.
/// </summary>
[DisallowMultipleComponent]
public class TopDownCameraController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Camera being driven. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera cam;

    [Header("Plane")]
    [Tooltip("If true, pans on the XY plane (2D top-down). If false, pans on the XZ plane (3D top-down, Y is up).")]
    [SerializeField] private bool useXYPlane = false;

    [Header("Pan — Keyboard")]
    [SerializeField] private bool enableKeyboardPan = true;
    [SerializeField] private float panSpeed = 20f;
    [Tooltip("Multiplies panSpeed while Shift is held.")]
    [SerializeField] private float sprintMultiplier = 2f;

    [Header("Pan — Middle-Mouse Drag")]
    [SerializeField] private bool enableDragPan = true;
    [Tooltip("World units the camera moves per pixel of mouse drag, per unit of ortho size / zoom distance.")]
    [SerializeField] private float dragPanSensitivity = 0.05f;

    [Header("Pan — Screen Edge")]
    [Tooltip("Off by default — enable for a classic RTS edge-scroll feel.")]
    [SerializeField] private bool enableEdgePan = false;
    [SerializeField] private float edgePanBorderPixels = 12f;
    [SerializeField] private float edgePanSpeed = 20f;

    [Header("Zoom")]
    [SerializeField] private bool enableZoom = true;
    [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float minOrthoSize = 4f;
    [SerializeField] private float maxOrthoSize = 30f;
    [Tooltip("For a perspective camera, min/max distance dollied along the rig's forward axis instead of ortho size.")]
    [SerializeField] private float minPerspectiveDistance = 5f;
    [SerializeField] private float maxPerspectiveDistance = 60f;
    [SerializeField] private float zoomSmoothTime = 0.1f;

    [Header("Bounds (optional)")]
    [Tooltip("If true, clamps the rig's pan position to boundsMin/boundsMax (world space, on the pan plane).")]
    [SerializeField] private bool clampToBounds = false;
    [SerializeField] private Vector2 boundsMin = new Vector2(-50f, -50f);
    [SerializeField] private Vector2 boundsMax = new Vector2(50f, 50f);

    // zoom state
    private float targetOrthoSize;
    private float currentOrthoSizeVelocity;
    private float targetPerspectiveDistance;
    private float currentPerspectiveDistance;
    private float currentPerspectiveDistanceVelocity;

    // drag-pan state
    private bool isDragPanning;
    private Vector2 lastMouseScreenPos;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("TopDownCameraController: no Camera assigned and no Camera.main found.", this);
            enabled = false;
            return;
        }

        targetOrthoSize = cam.orthographicSize;
        targetPerspectiveDistance = cam.transform.localPosition.magnitude;
        currentPerspectiveDistance = targetPerspectiveDistance;
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        HandleZoom();

        // Priority: drag pan > keyboard pan > edge pan. Only one moves the rig per frame.
        if (enableDragPan && HandleDragPan()) { /* consumed */ }
        else if (enableKeyboardPan && HandleKeyboardPan()) { /* consumed */ }
        else if (enableEdgePan) { HandleEdgePan(); }

        if (clampToBounds) ClampPosition();
    }

    // ---------------------------------------------------------------
    // Keyboard pan
    // ---------------------------------------------------------------

    private bool HandleKeyboardPan()
    {
        if (Keyboard.current == null) return false;

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;

        if (input.sqrMagnitude < 0.0001f) return false;

        input.Normalize();
        float speed = panSpeed;
        bool sprinting = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
        if (sprinting) speed *= sprintMultiplier;

        Move(input * speed * Time.deltaTime);
        return true;
    }

    // ---------------------------------------------------------------
    // Middle-mouse drag pan
    // ---------------------------------------------------------------

    private bool HandleDragPan()
    {
        if (Mouse.current.middleButton.wasPressedThisFrame)
        {
            isDragPanning = true;
            lastMouseScreenPos = Mouse.current.position.ReadValue();
        }
        else if (Mouse.current.middleButton.wasReleasedThisFrame)
        {
            isDragPanning = false;
        }

        if (!isDragPanning) return false;

        Vector2 currentMouseScreenPos = Mouse.current.position.ReadValue();
        Vector2 delta = currentMouseScreenPos - lastMouseScreenPos;
        lastMouseScreenPos = currentMouseScreenPos;

        if (delta.sqrMagnitude < 0.0001f) return true; // still "consumed" — we own the frame, just no movement

        // Scale drag distance by current zoom so it feels consistent whether zoomed in or out.
        float zoomScale = cam.orthographic ? cam.orthographicSize : targetPerspectiveDistance;
        Vector2 worldDelta = -delta * dragPanSensitivity * (zoomScale / Mathf.Max(minOrthoSize, 1f));

        Move(worldDelta);
        return true;
    }

    // ---------------------------------------------------------------
    // Screen-edge pan
    // ---------------------------------------------------------------

    private void HandleEdgePan()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector2 input = Vector2.zero;

        if (mousePos.x <= edgePanBorderPixels) input.x -= 1f;
        else if (mousePos.x >= Screen.width - edgePanBorderPixels) input.x += 1f;

        if (mousePos.y <= edgePanBorderPixels) input.y -= 1f;
        else if (mousePos.y >= Screen.height - edgePanBorderPixels) input.y += 1f;

        if (input.sqrMagnitude < 0.0001f) return;

        input.Normalize();
        Move(input * edgePanSpeed * Time.deltaTime);
    }

    // ---------------------------------------------------------------
    // Shared pan-plane movement
    // ---------------------------------------------------------------

    private void Move(Vector2 planeDelta)
    {
        Vector3 delta = useXYPlane
            ? new Vector3(planeDelta.x, planeDelta.y, 0f)
            : new Vector3(planeDelta.x, 0f, planeDelta.y);

        transform.position += delta;
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;
        if (useXYPlane)
        {
            pos.x = Mathf.Clamp(pos.x, boundsMin.x, boundsMax.x);
            pos.y = Mathf.Clamp(pos.y, boundsMin.y, boundsMax.y);
        }
        else
        {
            pos.x = Mathf.Clamp(pos.x, boundsMin.x, boundsMax.x);
            pos.z = Mathf.Clamp(pos.z, boundsMin.y, boundsMax.y);
        }
        transform.position = pos;
    }

    // ---------------------------------------------------------------
    // Zoom
    // ---------------------------------------------------------------

    private void HandleZoom()
    {
        if (!enableZoom || Mouse.current == null) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            if (cam.orthographic)
            {
                targetOrthoSize = Mathf.Clamp(targetOrthoSize - scroll * zoomSpeed * 0.01f, minOrthoSize, maxOrthoSize);
            }
            else
            {
                targetPerspectiveDistance = Mathf.Clamp(
                    targetPerspectiveDistance - scroll * zoomSpeed * 0.01f,
                    minPerspectiveDistance, maxPerspectiveDistance);
            }
        }

        if (cam.orthographic)
        {
            cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, targetOrthoSize,
                ref currentOrthoSizeVelocity, zoomSmoothTime);
        }
        else
        {
            // Dolly the camera along its own local offset direction (from the rig) to simulate zoom,
            // preserving whatever downward tilt angle it was set up with.
            currentPerspectiveDistance = Mathf.SmoothDamp(currentPerspectiveDistance, targetPerspectiveDistance,
                ref currentPerspectiveDistanceVelocity, zoomSmoothTime);

            Vector3 direction = cam.transform.localPosition.sqrMagnitude > 0.0001f
                ? cam.transform.localPosition.normalized
                : Vector3.back;
            cam.transform.localPosition = direction * currentPerspectiveDistance;
        }
    }

    /// <summary>
    /// Sets the pan bounds from a world-space min/max rect and enables clamping. Handy to call
    /// from ExcavationManager once the starter layout is seeded, e.g.
    /// cameraController.SetBounds(min, max) using GridToWorld on the grid's corner cells.
    /// </summary>
    public void SetBounds(Vector2 min, Vector2 max)
    {
        boundsMin = min;
        boundsMax = max;
        clampToBounds = true;
    }

    public void FocusOn(Vector3 worldPosition)
    {
        Vector3 pos = transform.position;
        if (useXYPlane)
        {
            pos.x = worldPosition.x;
            pos.y = worldPosition.y;
        }
        else
        {
            pos.x = worldPosition.x;
            pos.z = worldPosition.z;
        }
        transform.position = pos;
    }
}
