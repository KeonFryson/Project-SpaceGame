using UnityEngine;

/// Keeps a quad glued in front of the camera, exactly covering the view.
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class SpaceBackground : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [Tooltip("Distance in front of the camera. Must be less than the camera's far clip plane.")]
    [SerializeField] private float distance = 100f;
    [Tooltip("Slight oversize so edges never show during zoom smoothing.")]
    [SerializeField] private float overscan = 1.05f;
    [SerializeField] private int sortingOrder = -32000;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;

        var col = GetComponent<Collider>();
        if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }

        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingOrder = sortingOrder;   // draw behind all sprites in the 2D renderer
    }

    private void LateUpdate()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        float d = Mathf.Min(distance, cam.farClipPlane * 0.9f);
        Transform ct = cam.transform;
        transform.SetPositionAndRotation(ct.position + ct.forward * d, ct.rotation);

        float h = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        transform.localScale = new Vector3(h * cam.aspect * overscan, h * overscan, 1f);
    }
}