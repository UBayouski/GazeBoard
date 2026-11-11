// The sea and the waterline of the calm room. Unlit and cheap: one colour texture plus a few
// reads of a small tiling wave texture, moved by time. No lighting.
//
// Sea (_Shore = 0): UV runs from the waterline (v = 0) to the horizon (v = 1). Swell rolls
// toward the beach, a path of sun glitter lies under the sun, and near the beach the swell
// gathers into waves: broken lines of foam that travel in. No two are alike: each has its
// own strength along its length, and they bend and bunch as they come.
// Shore (_Shore = 1): a strip lying on the sand, UV from the dry sand (v = 0) to the shallows
// (v = 1), blended over the beach. Each wave that arrives washes up the sand in a sheet of
// foam and drains back, in step with the waves on the sea, so both materials must be given
// the same wave settings. The strip's mesh is flat; this shader tilts it onto the slope.
Shader "GazeBoard/CalmSea"
{
    Properties
    {
        _BaseMap ("Colour (alpha: how solid)", 2D) = "white" {}
        _WaveMap ("Waves (tiling, linear)", 2D) = "gray" {}
        [Toggle] _Shore ("Shore strip", Float) = 0
        _SizeMetres ("Size in metres (x, z)", Vector) = (1600, 800, 0, 0)
        _ShoreDistance ("Metres from the viewer to the waterline", Float) = 24
        _SwellStrength ("Swell strength", Range(0, 0.5)) = 0.20
        _SwellSpeed ("Swell speed", Range(0, 0.2)) = 0.035
        _CrestColor ("Foam colour", Color) = (0.95, 0.98, 0.97, 1)
        _CrestStrength ("Small crests in the shallows", Range(0, 1)) = 0.2
        _GlitterColor ("Glitter colour", Color) = (1.0, 0.95, 0.82, 1)
        _GlitterStrength ("Glitter strength", Range(0, 1)) = 0.35
        _SunSlope ("Sun path: metres sideways per metre out", Float) = 0.375
        _WaveLength ("Waves: metres between them", Range(4, 40)) = 15
        _WavePeriod ("Waves: seconds between them", Range(2, 20)) = 8
        _WaveFoam ("Waves: foam strength", Range(0, 1)) = 0.8
        _WaveReach ("Waves: metres out where they start to show", Range(10, 200)) = 50
        _RunUp ("Shore: metres a wave runs up the sand", Range(0, 2)) = 1.3
        _SeaEdge ("Shore: where the sea plane starts (v)", Range(0, 1)) = 0.375
        _ShoreLift ("Shore: metres the sand edge is raised", Range(0, 0.5)) = 0.16
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_WaveMap); SAMPLER(sampler_WaveMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _WaveMap_ST;
            float4 _SizeMetres;
            float4 _CrestColor;
            float4 _GlitterColor;
            float _Shore;
            float _ShoreDistance;
            float _SwellStrength;
            float _SwellSpeed;
            float _CrestStrength;
            float _GlitterStrength;
            float _SunSlope;
            float _WaveLength;
            float _WavePeriod;
            float _WaveFoam;
            float _WaveReach;
            float _RunUp;
            float _SeaEdge;
            float _ShoreLift;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 position = input.positionOS.xyz;
            // The shore strip is built flat at sea level, where the sand would hide its upper
            // half. Raise the top face's sand edge so the strip lies just above the slope.
            if (_Shore > 0.5 && input.normalOS.y > 0.5) position.y += _ShoreLift * (1.0 - input.uv.y);
            output.positionCS = TransformObjectToHClip(position);
            output.uv = input.uv;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Sea"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            // The wave train. Its whole part counts the waves; its fraction is 0 at a wave's
            // front and rises seaward of it. Fronts wander along the beach, and bend and bunch
            // over the sea bed, so they are neither straight nor evenly spaced.
            float WaveTrain(float sideways, float seaward, float time)
            {
                half3 along = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(sideways * 0.004, 0.61)).rgb;
                half bed = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(sideways * 0.012 + 0.3, seaward * 0.004)).g;
                return seaward / _WaveLength + time / _WavePeriod + along.r * 1.5 + along.b * 0.3 + bed * 0.5;
            }

            // How strong one wave is at this point along the beach, 0 (none) to 1. Every wave
            // gets its own pattern, so some come as long lines and some only in stretches.
            half WaveStrength(float sideways, float wave)
            {
                half n = SAMPLE_TEXTURE2D_LOD(_WaveMap, sampler_WaveMap, float2(sideways * 0.009 + wave * 0.37, wave * 0.61), 0).r;
                return smoothstep(0.28, 0.62, n);
            }

            // Waves near the beach, drawn over the water colour. seaward is metres out from
            // the waterline. Each wave is a front of foam that travels in, with a brighter
            // face behind it and a darker trough ahead.
            half3 Waves(half3 color, float sideways, float seaward, float time)
            {
                float train = WaveTrain(sideways, seaward, time);
                float f = frac(train);
                half strong = WaveStrength(sideways, floor(train));
                half reach = (1.0 - smoothstep(_WaveReach * 0.3, _WaveReach, seaward)) * strong;

                half face = (1.0 - smoothstep(0.0, 0.5, f)) - smoothstep(0.75, 1.0, f) * 0.7;
                color *= 1.0 + face * 0.13 * reach;

                // Foam: a soft leading edge, thick just behind the front, thinning seaward, and
                // broken into patches. It thickens as the wave nears the beach.
                half patches = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(sideways * 0.07, seaward * 0.10)).g;
                half closeIn = 1.0 - saturate(seaward / (_WaveLength * 1.6));
                half tail = lerp(0.10, 0.34, closeIn);
                half foam = smoothstep(0.0, 0.03, f) * (1.0 - smoothstep(0.03, tail, f));
                foam *= smoothstep(lerp(0.50, 0.25, closeIn), 0.68, patches);
                foam *= reach * lerp(0.35, 1.0, closeIn);
                return lerp(color, _CrestColor.rgb, saturate(foam * _WaveFoam));
            }

            half4 Shore(float2 uv, float time)
            {
                float sideways = (uv.x - 0.5) * _SizeMetres.x;
                float depth = max(_SizeMetres.y, 0.01);

                // p is how long ago the last wave arrived here, as a share of the time between
                // waves. The water runs up the sand quickly and drains back slowly; a stronger
                // wave runs further.
                float train = WaveTrain(sideways, 0.0, time);
                float p = frac(train);
                half strong = WaveStrength(sideways, floor(train));
                float up = p < 0.22 ? sin(p * 7.14) : cos((p - 0.22) * 2.0138);
                float shift = (up * lerp(0.35, 1.0, strong) - 0.4) * _RunUp;   // metres toward the sand

                float vt = uv.y + shift / depth;
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2(uv.x, vt));
                half3 color = tex.rgb;
                half alpha = tex.a;

                // The wash: a sheet of foam behind the water's edge when the wave lands, which
                // breaks into patches and thins out as the water drains back.
                half patches = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(sideways * 0.09, uv.y * depth * 0.25 + shift * 0.2)).g;
                half fresh = smoothstep(0.0, 0.06, p) * (1.0 - smoothstep(0.10, 0.75, p));
                half sheet = smoothstep(0.27, 0.33, vt) * (1.0 - smoothstep(0.45, 0.45 + 0.45 * fresh, vt));
                sheet *= lerp(smoothstep(0.35, 0.65, patches), 1.0, fresh * fresh) * lerp(0.5, 1.0, strong) * fresh;
                color = lerp(color, _CrestColor.rgb, saturate(sheet));
                alpha = max(alpha, sheet);

                // The last stretch of each incoming wave crosses this strip before it lands.
                float seaward = (uv.y - _SeaEdge) * depth;
                half3 withWaves = Waves(color, sideways, max(seaward, 0.0), time);
                color = lerp(color, withWaves, smoothstep(0.0, 0.4, seaward));

                // Fade out toward the dry sand and at the strip's far ends.
                alpha *= smoothstep(0.0, 0.10, uv.y);
                return half4(color, alpha);
            }

            half4 Sea(float2 uv, float time)
            {
                half3 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;

                float sideways = (uv.x - 0.5) * _SizeMetres.x;         // metres left or right of the viewer
                float seaward = uv.y * _SizeMetres.y;                   // metres out from the waterline
                float away = seaward + _ShoreDistance;                  // metres from the viewer

                // Seen from a room the sea is a thin band, so the swell is laid out the way it
                // is seen rather than in even metres: rows bunch toward the horizon, and widen
                // sideways with distance. Rows roll toward the beach.
                float rows = pow(max(uv.y, 0.0), 0.35) * 38.0;
                float across = sideways / (10.0 + away * 0.22);
                float roll = frac(time * _SwellSpeed);
                half swellA = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(across * 0.35, rows * 0.11 + roll)).r;
                half swellB = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(across * 0.60 + 0.43, rows * 0.23 + roll * 2.0)).g;
                half swell = swellA * 0.6 + swellB * 0.4;

                // Calm at the very edge, where the shore strip takes over, and toward the haze.
                half nearShore = smoothstep(0.002, 0.012, uv.y);
                half strength = _SwellStrength * nearShore * lerp(1.0, 0.35, sqrt(saturate(uv.y)));
                half3 color = base * (1.0 + (swell - 0.5) * 2.0 * strength);

                // A few small crests further out, beyond the breaking waves.
                half shallows = nearShore * (1.0 - smoothstep(0.03, 0.10, uv.y));
                half crest = smoothstep(0.66, 0.80, swell) * shallows * _CrestStrength;
                color = lerp(color, _CrestColor.rgb, crest);

                // Glitter where the water lies under the sun, flickering as two patterns cross.
                float path = (sideways - away * _SunSlope) / (5.0 + away * 0.16);
                half underSun = exp(-path * path);
                float drift = frac(time * 0.05);
                half sparkA = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(across * 5.0 + drift, rows * 0.12 + roll)).b;
                half sparkB = SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, float2(across * 3.7 - drift * 2.0, rows * 0.17 + 0.2)).b;
                half sparkle = smoothstep(0.30, 0.46, sparkA * sparkB) * underSun * nearShore;
                color += _GlitterColor.rgb * (sparkle * _GlitterStrength);

                // The waves that break on the beach. They fade out by themselves further out.
                return half4(Waves(color, sideways, seaward, time), 1.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Wrapped every so often so the numbers stay small enough to be exact. The wrap
                // is a whole number of waves and of swell cycles only if the settings divide
                // it, so it is long enough that a rare skip in the pattern goes unnoticed.
                float time = fmod(_Time.y, 3600.0);
                if (_Shore > 0.5) return Shore(input.uv, time);
                return Sea(input.uv, time);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            half4 FragDepth(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
