using System;
using System.Collections;
using UnityEngine;

/// Put on any GameObject with a SpriteRenderer using the "Custom/Sprite ForceField" material.
/// Call Hit(worldPoint) for ripples and Break() for the destroy animation.
[RequireComponent(typeof(SpriteRenderer))]
public class ForceFieldController : MonoBehaviour
{
    const int MaxRipples = 8;

    [Header("Look (overrides the material when assigned)")]
    [ColorUsage(true, true)] public Color fieldColor = new Color(0.2f, 0.8f, 2f, 1f);
    [ColorUsage(true, true)] public Color outlineColor = new Color(0.5f, 1f, 3f, 1f);

    public Texture2D patternTexture;          // set Wrap Mode = Repeat in the import settings
    public bool overrideColors = true;

    [Header("Hits")]
    public float minStrength = 0.4f;
    public float maxStrength = 1.5f;
    public float velocityForMaxStrength = 10f;
    [Tooltip("Also ripple from this object's own collision/trigger callbacks. " +
             "Leave off if projectiles already call Hit(), or you'll get double ripples.")]
    public bool autoRippleOnContact = false;

    [Header("Animation")]
    public float animationDuration = 0.6f;
    public float animationScalePunch = 0;

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
    public void Hit(Vector2 worldPoint, float strength = 1f)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        ripples[next] = new Vector4(local.x, local.y, Time.time, strength);
        next = (next + 1) % MaxRipples;
    }

    /// Plays the shield-destroyed animation, then calls onComplete.
    public void PlayAnimation(Action onComplete = null)
    {
        
        animationProgress = 0f;
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = StartCoroutine(AnimationRoutine(onComplete));
    }
 
    /// Resets the shield to its normal look. Call when the shield comes back.
    public void Restore()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null;
        animationProgress = 0f;
        transform.localScale = baseScale;
        for (int i = 0; i < MaxRipples; i++) ripples[i] = Vector4.zero;
    }

    IEnumerator AnimationRoutine(Action onComplete)
    {
        Debug.Log("ForceFieldController: AnimationRoutine started");
        // Burst of ripples around the edge
        Vector3 ext = sr.sprite != null ? sr.sprite.bounds.extents : Vector3.one * 0.5f;
        float radius = Mathf.Max(ext.x, ext.y) * 0.8f;
        for (int i = 0; i < MaxRipples; i++)
        {
            float ang = i / (float)MaxRipples * Mathf.PI * 2f;
            Vector3 local = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * radius;
            Hit(transform.TransformPoint(local), maxStrength);
        }

        float t = 0f;
        while (t < 1f)
        {
          
            t += Time.deltaTime / Mathf.Max(animationDuration, 0.01f);
            animationProgress = Mathf.Clamp01(t);
            Debug.Log($"ForceFieldController: AnimationRoutine progress {animationProgress}");
            transform.localScale = baseScale * (1f + animationScalePunch * Mathf.Sin(animationProgress * Mathf.PI));
            yield return null;
           
        }
        Debug.Log("ForceFieldController: AnimationRoutine progress completed");
        transform.localScale = baseScale;
        animationRoutine = null;
        Debug.Log("ForceFieldController: AnimationRoutine completed");
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
        Hit(p, 1f);
    }
}