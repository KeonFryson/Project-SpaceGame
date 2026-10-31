Shader "Custom/Sprite ForceField"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite (shape)", 2D) = "white" {}
        [HDR] _Tint ("Field Color", Color) = (0.2, 0.8, 2, 1)
        _PatternTex ("Pattern Texture (set Wrap = Repeat)", 2D) = "white" {}
        _PatternTiling ("Pattern Tiling (tiles per unit)", Float) = 0.5
        _PatternScroll ("Pattern Scroll (x,y)", Vector) = (0.05, 0.1, 0, 0)
        _PatternStrength ("Pattern Strength", Range(0, 2)) = 0.8
        _BaseAlpha ("Base Fill", Range(0, 1)) = 0.15

        [Header(Outline)]
        [HDR] _OutlineColor ("Outline Color", Color) = (0.5, 1, 3, 1)
        _OutlineWidth ("Outline Width (texels)", Range(0, 8)) = 2
        _OutlineIntensity ("Outline Intensity", Range(0, 4)) = 1.5

        [Header(Ripple)]
        [HDR] _RippleColor ("Ripple Color", Color) = (2, 2, 2, 1)
        _RippleSpeed ("Ripple Speed (units/sec)", Float) = 3
        _RippleWidth ("Ripple Width (units)", Float) = 0.25
        _RippleDuration ("Ripple Duration (sec)", Float) = 1
        _RippleDistort ("Ripple Distortion", Float) = 0.15

        [Header(Animation)]
        _Animation ("Animation Progress", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "DisableBatching" = "True"   // ripples use object-local position
        }

        Cull Off
        ZWrite Off
        Blend One One   // additive glow. Use "Blend SrcAlpha OneMinusSrcAlpha" for normal blending

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _PatternTex;
            float4 _PatternTex_ST;   // the Tiling/Offset fields on the texture slot

            fixed4 _Tint, _OutlineColor, _RippleColor;
            float _PatternTiling, _PatternStrength, _BaseAlpha;
            float4 _PatternScroll;
            float _OutlineWidth, _OutlineIntensity;
            float _RippleSpeed, _RippleWidth, _RippleDuration, _RippleDistort;
            float _Animation;

            // Set from ForceFieldController: xy = local hit pos, z = start time, w = strength (0 = inactive)
            float4 _Ripples[8];
            float _FFTime;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float2 local : TEXCOORD1;
                fixed4 color : COLOR;
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i),                hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.local = v.vertex.xy;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a;

                // ---------- Outline: compare alpha with 8 neighbours (works for any sprite) ----------
                float2 px = _MainTex_TexelSize.xy * _OutlineWidth;
                float2 offs[8] = {
                    float2( 1, 0), float2(-1, 0), float2(0, 1), float2(0,-1),
                    float2( 0.707, 0.707), float2(-0.707, 0.707),
                    float2( 0.707,-0.707), float2(-0.707,-0.707)
                };
                float maxA = a, minA = a;
                [unroll]
                for (int n = 0; n < 8; n++)
                {
                    float na = tex2D(_MainTex, i.uv + offs[n] * px).a;
                    maxA = max(maxA, na);
                    minA = min(minA, na);
                }
                // outer edge (just outside sprite) + inner edge (just inside)
                float edge = saturate(maxA - a) + saturate(a - minA);

                // ---------- Ripples ----------
                float ripple = 0;
                float2 distort = 0;
                [unroll]
                for (int r = 0; r < 8; r++)
                {
                    float4 rp = _Ripples[r];
                    float age = _FFTime - rp.z;
                    float live = step(0.0001, rp.w) * step(0, age) * step(age, _RippleDuration);
                    float2 delta = i.local - rp.xy;
                    float d = length(delta);
                    float x = (d - age * _RippleSpeed) / max(_RippleWidth, 0.0001);
                    float ring = exp(-x * x);
                    float fade = saturate(1 - age / max(_RippleDuration, 0.0001));
                    float k = ring * fade * rp.w * live;
                    ripple += k;
                    distort += normalize(delta + 1e-5) * k * _RippleDistort;
                }

                // ---------- Pattern (scrolls, gets warped by ripples) ----------
                // local-space coords: independent of sprite atlas UVs and sprite size
                float2 puv = i.local * _PatternTiling + _Time.y * _PatternScroll.xy + distort;
                puv = puv * _PatternTex_ST.xy + _PatternTex_ST.zw;   // slot Tiling/Offset also work
                float pat = dot(tex2D(_PatternTex, puv).rgb, float3(0.299, 0.587, 0.114));

                float fill = a * (_BaseAlpha + pat * _PatternStrength);
                float rip = ripple * a;

                // ---------- Break: noise dissolve + burn edge + flash ----------
                float breaking = step(0.001, _Animation);
                float noise = valueNoise(i.local * 6.0) * 0.7 + valueNoise(i.local * 14.0) * 0.3;
                float dissolve = lerp(1.0, smoothstep(_Animation - 0.08, _Animation, noise), breaking);
                float burn = saturate(1 - abs(noise - _Animation) / 0.08) * breaking * a;
                float flash = exp(-_Animation * 6.0) * breaking * a;
                float fadeOut = 1 - _Animation;

                float3 rgb = _Tint.rgb * fill * dissolve
                           + _OutlineColor.rgb * edge * _OutlineIntensity * fadeOut
                           + _RippleColor.rgb * rip * fadeOut
                           + _OutlineColor.rgb * burn * 2.5
                           + _RippleColor.rgb * flash * 0.6;

                rgb *= i.color.rgb * i.color.a;   // SpriteRenderer color/alpha still works
                return fixed4(rgb, 1);
            }
            ENDCG
        }
    }
}
