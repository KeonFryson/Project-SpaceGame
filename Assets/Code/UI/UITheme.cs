using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One place for the whole look of the game's UI: palette, procedural sprites (no art assets needed),
/// and helpers to build holographic panels, buttons, text and bars. Every UI script should call these
/// instead of keeping its own NewRect / NewText / MakeButton copies.
/// Optional: put a TMP font asset at Resources/UIFont (e.g. Orbitron or Rajdhani) and it is used everywhere.
/// </summary>
public static class UITheme
{
    // ------------------------------------------------------------------ palette
    public static readonly Color Bg        = new Color(0.02f, 0.05f, 0.09f, 0.88f);
    public static readonly Color BgSolid   = new Color(0.02f, 0.05f, 0.09f, 0.97f);
    public static readonly Color Accent    = new Color(0.30f, 0.85f, 1.00f, 1.00f);
    public static readonly Color AccentDim = new Color(0.30f, 0.85f, 1.00f, 0.40f);
    public static readonly Color Good      = new Color(0.49f, 1.00f, 0.61f, 1f);
    public static readonly Color Warn      = new Color(1.00f, 0.80f, 0.33f, 1f);
    public static readonly Color Danger    = new Color(1.00f, 0.42f, 0.42f, 1f);
    public static readonly Color TextMain  = new Color(0.88f, 0.96f, 1.00f, 1f);
    public static readonly Color TextDim   = new Color(0.50f, 0.62f, 0.70f, 1f);

    static readonly Color BtnIdle  = new Color(0.05f, 0.15f, 0.22f, 0.92f);
    static readonly Color BtnHover = new Color(0.09f, 0.32f, 0.44f, 1.00f);
    static readonly Color BtnPress = new Color(0.30f, 0.85f, 1.00f, 0.85f);
    static readonly Color BtnOff   = new Color(0.04f, 0.07f, 0.09f, 0.60f);

    public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    /// <summary>Wraps text in a rich-text colour tag.</summary>
    public static string Tint(string s, Color c) => "<color=#" + Hex(c) + ">" + s + "</color>";
    public static string Dim(string s) => Tint(s, TextDim);

    // opening tags for building strings: UITheme.DimTag + "text" + UITheme.End
    public const string End = "</color>";
    public static readonly string DimTag    = "<color=#" + Hex(TextDim) + ">";
    public static readonly string AccentTag = "<color=#" + Hex(Accent) + ">";
    public static readonly string GoodTag   = "<color=#" + Hex(Good) + ">";
    public static readonly string WarnTag   = "<color=#" + Hex(Warn) + ">";
    public static readonly string DangerTag = "<color=#" + Hex(Danger) + ">";

    // ------------------------------------------------------------------ procedural sprites
    static Sprite outlineSprite, whiteSprite;
    static TMP_FontAsset font;
    static bool fontLooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { outlineSprite = null; whiteSprite = null; font = null; fontLooked = false; }

    static Sprite MakeSprite(Texture2D tex, Vector4 border)
    {
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, border);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    static Sprite White()
    {
        if (whiteSprite != null) return whiteSprite;
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        return whiteSprite = MakeSprite(tex, Vector4.zero);
    }

    /// <summary>9-sliced frame: faint 1px edge plus bright 2px corner brackets. Tint it with Image.color.</summary>
    static Sprite Outline()
    {
        if (outlineSprite != null) return outlineSprite;
        const int S = 32, B = 10;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int dx = Mathf.Min(x, S - 1 - x), dy = Mathf.Min(y, S - 1 - y);
                bool bracket = (dx <= 1 && dy < B) || (dy <= 1 && dx < B);
                bool edge = dx == 0 || dy == 0;
                byte a = bracket ? (byte)255 : edge ? (byte)90 : (byte)0;
                px[y * S + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        return outlineSprite = MakeSprite(tex, new Vector4(B, B, B, B));
    }

    // ------------------------------------------------------------------ basic building blocks
    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    public static void Height(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = h;
    }

    public static TMP_Text MakeText(Transform parent, string name, float size, TextAlignmentOptions align,
                                    FontStyles style = FontStyles.Normal, float minSize = 0f)
    {
        var rt = Rect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (!fontLooked) { font = Resources.Load<TMP_FontAsset>("UIFont"); fontLooked = true; }
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.fontStyle = style;
        t.color = TextMain;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        if (minSize > 0f)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = minSize;
        }
        return t;
    }

    // ------------------------------------------------------------------ holographic panel
    /// <summary>Adds a decoration child that layout groups ignore.</summary>
    static RectTransform Decor(string name, Transform parent)
    {
        var rt = Rect(name, parent);
        rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return rt;
    }

    /// <summary>Frame with corner brackets. Safe to add to any rect (e.g. rows, cards).</summary>
    public static Image AddFrame(Transform target, Color color, bool pulse = false)
    {
        var rt = Decor("Frame", target);
        Stretch(rt, 0, 0, 0, 0);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Outline();
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        if (pulse) rt.gameObject.AddComponent<HoloPulse>();
        return img;
    }

    /// <summary>Dark translucent panel + bracket frame + glowing top line. Blocks clicks to the game below.</summary>
    public static RectTransform Panel(Transform parent, string name, bool solid = false, bool pulse = true)
    {
        var rt = Rect(name, parent);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = White();
        bg.color = solid ? BgSolid : Bg;

        AddFrame(rt, Accent, pulse);

        var bar = Decor("TopLine", rt);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(-20f, 2f);
        bar.anchoredPosition = Vector2.zero;
        var bi = bar.gameObject.AddComponent<Image>();
        bi.sprite = White();
        bi.color = AccentDim;
        bi.raycastTarget = false;
        return rt;
    }

    // ------------------------------------------------------------------ button
    public static Button MakeButton(Transform parent, string label, float w, float h, UnityAction onClick,
                                    float fontSize, out TMP_Text text)
    {
        var rt = Rect("Btn_" + label, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = White();
        img.color = Color.white;               // the ColorBlock below does the tinting

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };   // keep WASD / Enter out of the UI
        var cb = btn.colors;
        cb.normalColor = BtnIdle;
        cb.highlightedColor = BtnHover;
        cb.selectedColor = BtnIdle;
        cb.pressedColor = BtnPress;
        cb.disabledColor = BtnOff;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() =>
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            onClick();
        });

        AddFrame(rt, AccentDim);

        var le = rt.gameObject.AddComponent<LayoutElement>();
        if (w > 0f) le.minWidth = le.preferredWidth = w;
        if (h > 0f) le.minHeight = le.preferredHeight = h;

        text = MakeText(rt, "Label", fontSize, TextAlignmentOptions.Midline, FontStyles.Bold, Mathf.Max(8f, fontSize * 0.6f));
        Stretch(text.rectTransform, 4f, 0f, 4f, 0f);
        text.text = label;
        return btn;
    }

    public static Button MakeButton(Transform parent, string label, float w, float h, UnityAction onClick, float fontSize = 18f)
        => MakeButton(parent, label, w, h, onClick, fontSize, out _);

    /// <summary>Highlights a button as the active tab / toggle.</summary>
    public static void SetActive(Button b, bool active)
    {
        var cb = b.colors;
        cb.normalColor = active ? BtnPress : BtnIdle;
        cb.selectedColor = cb.normalColor;
        b.colors = cb;
    }

    // ------------------------------------------------------------------ progress bar
    public sealed class Bar
    {
        public RectTransform root;
        Image fill;
        public void Build(RectTransform r, Image f) { root = r; fill = f; }
        public void Set(float v, Color? color = null)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
            if (color.HasValue) fill.color = color.Value;
        }
    }

    /// <summary>Thin glowing bar for fuel, cargo, population, build progress. Call bar.Set(0..1).</summary>
    public static Bar MakeBar(Transform parent, string name, Color color, float height = 8f)
    {
        var rt = Rect(name, parent);
        var back = rt.gameObject.AddComponent<Image>();
        back.sprite = White();
        back.color = new Color(1f, 1f, 1f, 0.08f);
        back.raycastTarget = false;
        Height(rt.gameObject, height);

        var f = Rect("Fill", rt);
        Stretch(f, 0, 0, 0, 0);
        f.anchorMax = new Vector2(0f, 1f);
        var fi = f.gameObject.AddComponent<Image>();
        fi.sprite = White();
        fi.color = color;
        fi.raycastTarget = false;

        var bar = new Bar();
        bar.Build(rt, fi);
        return bar;
    }

    /// <summary>A bar glued to the bottom edge of a row (ignored by layout groups). Good for build progress.</summary>
    public static Bar MakeOverlayBar(Transform parent, string name, Color color, float height = 3f)
    {
        var bar = MakeBar(parent, name, color, height);
        bar.root.GetComponent<LayoutElement>().ignoreLayout = true;
        bar.root.anchorMin = new Vector2(0f, 0f);
        bar.root.anchorMax = new Vector2(1f, 0f);
        bar.root.pivot = new Vector2(0.5f, 0f);
        bar.root.anchoredPosition = Vector2.zero;
        bar.root.sizeDelta = new Vector2(0f, height);
        return bar;
    }
}

/// <summary>Slow breathing of a frame's alpha, so panels feel like projections. Ignores pause (unscaled time).</summary>
[RequireComponent(typeof(Image))]
public class HoloPulse : MonoBehaviour
{
    Image img;
    float baseAlpha;

    void Awake() { img = GetComponent<Image>(); baseAlpha = img.color.a; }

    void Update()
    {
        var c = img.color;
        c.a = baseAlpha * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 1.6f));
        img.color = c;
    }
}
