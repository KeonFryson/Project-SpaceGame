using System;
using System.Collections;
using UnityEngine;

/// Put on any GameObject with a SpriteRenderer using the "Custom/Sprite ForceField" material.
/// Call Hit(worldPoint) for ripples and PlayAnimation() for the appear/disappear animation.
[RequireComponent(typeof(SpriteRenderer))]
public class ForceFieldController : MonoBehaviour
{
    // ---- fixed rules -------------------------------------------------------
    const int MaxRipples = 8;                       // must match the array size in the shader
    const float DefaultRippleStrength = 1f;
    const float AnimationIntact = 0f;               // shader _Animation value: shield fully present
    const float AnimationBroken = 1f;               // shader _Animation value: shield fully gone
    const float MinAnimationDuration = 0.01f;       // guards against divide-by-zero
    const float FullCircleRadians = Mathf.PI * 2f;
    const float DefaultSpriteHalfExtent = 0.5f;     // used when there is no sprite assigned

    [Header("Look (overrides the material when assigned)")]
    [ColorUsage(true, true)] public Color fieldColor = new Color(0.2f, 0.8f, 2f, 1f);
    [ColorUsage(true, true)] public Color outlineColor = new Color(0.5f, 1f, 3f, 1f);

    public Texture2D patternTexture;          // set Wrap Mode = Repeat in the import settings
    public bool overrideColors = true;

    [Header("Hits")]
    public float minStrength = 0.4f;
    public float maxStrength = 1.5f;
    public float velocityForMaxStrength = 10f;
    [Tooltip("Strength of the ripple when triggered by a trigger collider.")]
    public float triggerRippleStrength = DefaultRippleStrength;
    [Tooltip("Also ripple from this object's own collision/trigger callbacks. " +
             "Leave off if projectiles already call Hit(), or you'll get double ripples.")]
    public bool autoRippleOnContact = false;

    [Header("Animation")]
    public float animationDuration = 0.6f;
    public float animationScalePunch = 0f;
    [Tooltip("Where the ripple burst sits, as a fraction of the sprite's half-size.")]
    [Range(0f, 1f)] public float burstRadiusFraction = 0.8f;

    SpriteRenderer sr;
    MaterialPropertyBlock mpb;
    readonly Vector4[] ripples = new Vector4[MaxRipples];
    int next;

    float animationProgress;
    Vector3 baseScale = Vector3.one;
    Coroutine animationRoutine;

    static readonly int RipplesId = Shader.PropertyToID("_Ripples");
    static readonly int TimeId = Shader.PropertyToID("_FFTime");
    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int OutlineId = Shader.PropertyToID("_OutlineColor");
    static readonly int PatternId = Shader.PropertyToID("_PatternTex");
    static readonly int AnimationId = Shader.PropertyToID("_Animation");

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
        baseScale = transform.localScale;
        animationProgress = AnimationIntact;
    }

    void LateUpdate()
    {
        sr.GetPropertyBlock(mpb);
        mpb.SetFloat(TimeId, Time.time);
        mpb.SetVectorArray(RipplesId, ripples);
        mpb.SetFloat(AnimationId, animationProgress);
        if (overrideColors)
        {
            mpb.SetColor(TintId, fieldColor);
            mpb.SetColor(OutlineId, outlineColor);
        }
        if (patternTexture != null) mpb.SetTexture(PatternId, patternTexture);
        sr.SetPropertyBlock(mpb);
    }

    /// Spawn a ripple at a world-space position.
    public void Hit(Vector2 worldPoint, float strength = DefaultRippleStrength)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        ripples[next] = new Vector4(local.x, local.y, Time.time, strength);
        next = (next + 1) % MaxRipples;
    }

    /// Plays the shield animation, then calls onComplete.
    /// reverse = false: shield breaks / disappears
    /// reverse = true:  shield materialises
    public void PlayAnimation(Action onComplete = null, bool reverse = false)
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationProgress = reverse ? AnimationBroken : AnimationIntact;
        animationRoutine = StartCoroutine(AnimationRoutine(onComplete, reverse));
    }

    /// Resets the shield to its normal look.
    public void Restore()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null;
        animationProgress = AnimationIntact;
        transform.localScale = baseScale;
        Array.Clear(ripples, 0, MaxRipples);
    }

    IEnumerator AnimationRoutine(Action onComplete, bool reverse)
    {
        // Burst of ripples around the edge
        Vector3 ext = sr.sprite != null
            ? sr.sprite.bounds.extents
            : Vector3.one * DefaultSpriteHalfExtent;
        float radius = Mathf.Max(ext.x, ext.y) * burstRadiusFraction;
        for (int i = 0; i < MaxRipples; i++)
        {
            float ang = i / (float)MaxRipples * FullCircleRadians;
            Vector3 local = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * radius;
            Hit(transform.TransformPoint(local), maxStrength);
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(animationDuration, MinAnimationDuration);
            float p = Mathf.Clamp01(t);
            animationProgress = reverse
                ? Mathf.Lerp(AnimationBroken, AnimationIntact, p)
                : Mathf.Lerp(AnimationIntact, AnimationBroken, p);
            transform.localScale = baseScale * (1f + animationScalePunch * Mathf.Sin(p * Mathf.PI));
            yield return null;
        }

        transform.localScale = baseScale;
        animationRoutine = null;
        onComplete?.Invoke();
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (!autoRippleOnContact) return;
        float s = Mathf.Lerp(minStrength, maxStrength,
                  Mathf.Clamp01(c.relativeVelocity.magnitude / velocityForMaxStrength));
        Hit(c.GetContact(0).point, s);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!autoRippleOnContact) return;
        var col = GetComponent<Collider2D>();
        Vector2 p = col != null ? col.ClosestPoint(other.transform.position)
                                : (Vector2)other.transform.position;
        Hit(p, triggerRippleStrength);
    }
}