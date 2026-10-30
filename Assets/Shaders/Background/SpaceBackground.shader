Shader "Custom/SpaceBackground"
{
    Properties
    {
        [Header(Base)]
        _BaseColor ("Void Color", Color) = (0.008, 0.01, 0.025, 1)
        [Toggle] _UseXZ ("Pan on XZ plane (3D top-down)", Float) = 0

        [Header(Nebula)]
        _NebulaColorA ("Nebula Color A", Color) = (0.35, 0.08, 0.55, 1)
        _NebulaColorB ("Nebula Color B", Color) = (0.05, 0.30, 0.65, 1)
        _NebulaCoreColor ("Hot Core Color", Color) = (1.0, 0.45, 0.30, 1)
        _NebulaScale ("Nebula Scale", Float) = 0.06
        _NebulaIntensity ("Nebula Intensity", Range(0, 3)) = 0.9
        _NebulaCoverage ("Nebula Coverage", Range(0, 1)) = 0.45
        _NebulaParallax ("Nebula Parallax Follow", Range(0, 1)) = 0.97
        _NebulaDrift ("Nebula Drift Speed", Float) = 0.01

        [Header(Stars)]
        _StarDensity ("Star Density", Range(0, 1)) = 0.35
        _StarBrightness ("Star Brightness", Range(0, 3)) = 1.2
        _TwinkleSpeed ("Twinkle Speed", Float) = 1.5
        _FarParallax ("Far Layer Parallax Follow", Range(0, 1)) = 0.93
        _MidParallax ("Mid Layer Parallax Follow", Range(0, 1)) = 0.82
        _NearParallax ("Near Layer Parallax Follow", Range(0, 1)) = 0.65
        _Seed ("Seed", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BaseColor, _NebulaColorA, _NebulaColorB, _NebulaCoreColor;
            float _UseXZ;
            float _NebulaScale, _NebulaIntensity, _NebulaCoverage, _NebulaParallax, _NebulaDrift;
            float _StarDensity, _StarBrightness, _TwinkleSpeed;
            float _FarParallax, _MidParallax, _NearParallax;
            float _Seed;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i),                hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    v += valueNoise(p) * a;
                    p = p * 2.02 + 17.3;
                    a *= 0.5;
                }
                return v;
            }

            // One layer of stars on a jittered grid. size is in cell units.
            float3 starLayer(float2 lp, float scale, float size, float density, float seed)
{
    float2 g = lp * scale;
    float px = fwidth(g.x);                    // pixel footprint in cell units
    float2 id = floor(g);
    float2 f = frac(g) - 0.5;

    float h = hash21(id + seed);
    float present = step(h, density);

    float2 off = (float2(hash21(id + seed + 1.7), hash21(id + seed + 9.2)) - 0.5) * 0.5;
    float d = length(f - off);

    // ~1 pixel minimum so stars don't shimmer, but capped so it can't outgrow the cell
    float r = clamp(max(size, px * 0.9), 0.001, 0.09);
    float energy = saturate((size * size) / (r * r) + 0.25);

    float core = pow(saturate(1.0 - d / r), 3.0);
    float glow = exp(-d * d / (r * r * 6.0)) * 0.25;

    // star centers are within 0.25 of the cell middle, so fading to 0 by d = 0.24
    // guarantees nothing ever touches the cell border
    float edgeFade = 1.0 - smoothstep(0.10, 0.24, d);

    float rnd = hash21(id + seed + 4.3);
    float bright = lerp(0.35, 1.0, rnd * rnd);
    float twinkle = 0.78 + 0.22 * sin(_Time.y * _TwinkleSpeed * (0.5 + rnd) + h * 100.0);

    float t = hash21(id + seed + 6.1);
    float3 tint = t < 0.5 ? lerp(float3(1.0, 0.65, 0.45), float3(1, 1, 1), t * 2.0)
                          : lerp(float3(1, 1, 1), float3(0.6, 0.78, 1.0), (t - 0.5) * 2.0);

    return tint * (core + glow * edgeFade) * bright * twinkle * energy * present;
}

            fixed4 frag (v2f i) : SV_Target
            {
                float2 wp  = lerp(i.world.xy, i.world.xz, _UseXZ);
                float3 cam = _WorldSpaceCameraPos;
                float2 cp  = lerp(cam.xy, cam.xz, _UseXZ);

                // layer coord: follow=0 -> locked to world, follow=1 -> locked to screen (infinitely far)
                float2 nebP = wp - cp * _NebulaParallax;
                float2 farP = wp - cp * _FarParallax;
                float2 midP = wp - cp * _MidParallax;
                float2 nearP = wp - cp * _NearParallax;

                // ---------- Nebula ----------
                float2 q = nebP * _NebulaScale + float2(_Time.y * _NebulaDrift, 0) + _Seed * 13.7;
                float2 warp = float2(fbm(q + 3.1), fbm(q + 7.7));
                float n  = fbm(q + warp * 1.8);
                float n2 = fbm(q * 2.3 - warp + 11.0);

                float t = lerp(0.75, 0.30, _NebulaCoverage);
                float cloud = smoothstep(t - 0.10, t + 0.25, n);
                float core  = smoothstep(0.62, 0.90, n) * cloud;
                float dust  = smoothstep(0.50, 0.80, n2) * cloud;

                float3 nebCol = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, saturate(n2 * 1.4));
                nebCol += _NebulaCoreColor.rgb * core * 0.8;
                float3 rgb = _BaseColor.rgb + nebCol * cloud * (1.0 - 0.45 * dust) * _NebulaIntensity;

                // ---------- Stars (far -> near) ----------
                float3 stars = 0;
                stars += starLayer(farP,  3.0, 0.050, _StarDensity,        _Seed + 1.0) * 0.7;
                stars += starLayer(midP,  1.4, 0.070, _StarDensity * 0.6,  _Seed + 2.0) * 1.0;
                stars += starLayer(nearP, 0.6, 0.100, _StarDensity * 0.25, _Seed + 3.0) * 1.6;
                rgb += stars * _StarBrightness;

                return fixed4(rgb, 1);
            }
            ENDCG
        }
    }
}