using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the whole 1920x1080 reference layout visible on ANY screen shape. Whichever axis is the
/// limiting one (width on 4:3 / 16:10 screens, height on ultrawide) drives the scale, so nothing
/// is ever cropped. Re-evaluates when the window is resized or the device is rotated.
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
public class UIFit : MonoBehaviour
{
    public Vector2 reference = new Vector2(1920f, 1080f);

    CanvasScaler scaler;
    int lastW = -1, lastH = -1;

    void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        Apply();
    }

    void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH) Apply();
    }

    void Apply()
    {
        lastW = Screen.width;
        lastH = Screen.height;
        if (lastW <= 0 || lastH <= 0) return;

        scaler.referenceResolution = reference;
        float sw = lastW / reference.x, sh = lastH / reference.y;
        scaler.matchWidthOrHeight = sw < sh ? 0f : 1f;   // follow the smaller scale = always fits
    }
}

/// <summary>Insets this rect to Screen.safeArea (notches, rounded corners, console TV overscan).</summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    Rect last;
    int lastW = -1, lastH = -1;

    void Update()
    {
        var sa = Screen.safeArea;
        if (sa == last && Screen.width == lastW && Screen.height == lastH) return;
        last = sa;
        lastW = Screen.width;
        lastH = Screen.height;
        if (lastW <= 0 || lastH <= 0) return;

        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(sa.xMin / lastW, sa.yMin / lastH);
        rt.anchorMax = new Vector2(sa.xMax / lastW, sa.yMax / lastH);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
