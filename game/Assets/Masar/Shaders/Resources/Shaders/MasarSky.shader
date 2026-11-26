// MASAR procedural sky for sunset and night (M3-3). The day sky is a photographed HDRI with the
// sun baked into it, so it cannot follow a different sun position; this sky is drawn from the
// same sun (or moon) direction the scene is lit from. Horizon-to-zenith gradient, a glow around
// the sun, a sun or moon disc, a darker band below the horizon, and faint stars at night.
Shader "Masar/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.10, 0.18, 0.40, 1)
        _Horizon ("Horizon", Color) = (0.90, 0.55, 0.32, 1)
        _Ground ("Below horizon", Color) = (0.12, 0.08, 0.06, 1)
        _SunColor ("Sun glow", Color) = (1.0, 0.55, 0.25, 1)
        _SunDir ("Direction to the sun or moon", Vector) = (0, 0.1, -1, 0)
        _SunSize ("Disc size (cos)", Float) = 0.9995
        _DiscIntensity ("Disc intensity", Float) = 30
        _GlowPower ("Glow tightness", Float) = 8
        _GlowIntensity ("Glow intensity", Float) = 1.5
        _HorizonPower ("Horizon falloff", Float) = 2.5
        _Stars ("Stars", Float) = 0
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _Ground, _SunColor;
                float4 _SunDir;
                float _SunSize, _DiscIntensity, _GlowPower, _GlowIntensity, _HorizonPower, _Stars, _Exposure;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 s = normalize(_SunDir.xyz);
                float up = d.y;
                float t = pow(saturate(1.0 - max(up, 0.0)), _HorizonPower);
                float3 col = lerp(_Zenith.rgb, _Horizon.rgb, t);
                // below the horizon: fade to the ground colour (hidden by the city most of the time)
                col = lerp(col, _Ground.rgb, saturate(-up * 6.0));
                float c = dot(d, s);
                // glow around the sun, stronger near the horizon (longer path through the haze)
                float glow = pow(saturate(c), _GlowPower) * _GlowIntensity * (0.5 + 0.5 * t);
                col += _SunColor.rgb * glow;
                // disc with a soft edge
                float disc = smoothstep(_SunSize - 0.00025, _SunSize + 0.0001, c);
                col += _SunColor.rgb * disc * _DiscIntensity * saturate(up * 20.0 + 0.6);
                // stars: sparse cells, only well above the horizon
                if (_Stars > 0.0 && up > 0.05)
                {
                    // one possible star per cell, a small point at a random spot inside it
                    float3 cell = floor(d * 120.0);
                    float h = Hash(cell);
                    if (h > 0.97)
                    {
                        float3 jitter = float3(Hash(cell + 17.1), Hash(cell + 31.7), Hash(cell + 53.3)) * 0.7 + 0.15;
                        float3 c3 = normalize((cell + jitter) / 120.0);
                        float dist = length(d - c3);
                        float star = smoothstep(0.0011, 0.0, dist) * (h - 0.97) / 0.03;
                        col += star * _Stars * 2.0 * saturate((up - 0.05) * 4.0);
                    }
                }
                return half4(col * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
