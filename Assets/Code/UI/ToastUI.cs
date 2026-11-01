using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Short messages under the top bar. ToastUI.Show("text") from anywhere. Also announces finished
/// buildings and ships. Lives under RTSUI's canvas. Created by RTSBootstrap after RTSUI.
/// </summary>
public class ToastUI : MonoBehaviour
{
    const float Duration = 3f;

    static ToastUI instance;
    GameObject box;
    TMP_Text text;
    float timer;

    public static void Show(string message)
    {
        if (instance != null) instance.Display(message);
    }

    void Awake()
    {
        var safe = transform.Find("RTSCanvas/SafeArea");
        if (safe == null)
        {
            Debug.LogError("ToastUI: RTSUI canvas not found. Add ToastUI AFTER RTSUI.", this);
            enabled = false;
            return;
        }
        instance = this;
        Build(safe);
        EconomySystem.BuildingCompleted += OnBuilt;     // static events: removed in OnDestroy
        EconomySystem.ShipCompleted += OnShip;
    }

    void OnDestroy()
    {
        EconomySystem.BuildingCompleted -= OnBuilt;
        EconomySystem.ShipCompleted -= OnShip;
        if (instance == this) instance = null;
    }

    void OnBuilt(BodyState st, BuildingType t) => Display(Buildings.Def(t).name + " complete");
    void OnShip(BodyState st, ShipClass c) => Display(ShipDefs.Get(c).name + " built");

    void Display(string message)
    {
        text.text = message;
        box.SetActive(true);
        timer = Duration;
    }

    void Update()
    {
        if (timer <= 0f) return;
        timer -= Time.unscaledDeltaTime;
        text.alpha = Mathf.Clamp01(timer);       // fades over the last second
        if (timer <= 0f) box.SetActive(false);
    }

    void Build(Transform parent)
    {
        var go = new GameObject("Toast", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -54f);
        rt.sizeDelta = new Vector2(640f, 36f);
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.65f);
        img.raycastTarget = false;
        box = go;

        var tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(go.transform, false);
        var trt = (RectTransform)tgo.transform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 2f);
        trt.offsetMax = new Vector2(-10f, -2f);
        text = tgo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 18;
        text.alignment = TextAlignmentOptions.Midline;
        text.color = Color.white;
        text.richText = true;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMax = 18;
        text.fontSizeMin = 10;

        box.SetActive(false);
    }
}
