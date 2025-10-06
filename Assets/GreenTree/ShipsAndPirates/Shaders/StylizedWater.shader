Shader "GreenTree/StylizedWater"
{
    Properties
    {
        _ShallowColor("Shallow Water Color", Color) = (0.5, 0.5, 0.5, 0.5)
        _DeepColor("Deep Water Color", Color) = (0.5, 0.5, 0.5, 0.5)
        _MaxDepth("Maximum Depth Distance", Float) = 1
        _FoamTint("Foam Color", Color) = (1, 1, 1, 1)
        _WaveNoise("Wave Noise Texture", 2D) = "white" {}
        _WaveScroll("Wave Scroll Speed", Vector) = (0.01, 0.01, 0, 0)
        _WaveThreshold("Wave Noise Threshold", Range(0, 1)) = 0.8
        _DistortionMap("Distortion Texture", 2D) = "white" {}
        _DistortionIntensity("Distortion Intensity", Range(0, 1)) = 0.3
        _FoamMaxDist("Max Foam Distance", Float) = 0.5
        _FoamMinDist("Min Foam Distance", Float) = 0.05
        _RippleTexture("Ripple Texture", 2D) = "white" {}
        _RippleSpeed("Ripple Speed", Range(0, 10)) = 1.0
        _RippleAlpha("Ripple Alpha", Range(0, 10)) = 1.0
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            float4 _ShallowColor;
            float4 _DeepColor;
            float4 _FoamTint;
            float _MaxDepth;
            float _FoamMaxDist;
            float _FoamMinDist;
            float _WaveThreshold;
            float _DistortionIntensity;
            float2 _WaveScroll;
            float _RippleSpeed;
            float _RippleAlpha;

            sampler2D _WaveNoise;
            float4 _WaveNoise_ST;
            sampler2D _DistortionMap;
            float4 _DistortionMap_ST;
            sampler2D _RippleTexture;
            float4 _RippleTexture_ST;
            sampler2D _CameraDepthTexture;
            sampler2D _CameraNormalsTexture;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 waveUV : TEXCOORD0;
                float2 distortionUV : TEXCOORD1;
                float2 rippleUV : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 viewNormal : TEXCOORD4;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                o.distortionUV = TRANSFORM_TEX(v.uv, _DistortionMap);
                o.waveUV = TRANSFORM_TEX(v.uv, _WaveNoise);
                o.rippleUV = TRANSFORM_TEX(v.uv, _RippleTexture);
                o.viewNormal = v.normal;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float depth = tex2Dproj(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)).r;
                float linearDepth = LinearEyeDepth(depth);
                float depthDiff = linearDepth - i.screenPos.w;

                float depthFactor = saturate(depthDiff / _MaxDepth);
                float4 waterColor = lerp(_ShallowColor, _DeepColor, depthFactor);

                float3 backNormal = tex2Dproj(_CameraNormalsTexture, UNITY_PROJ_COORD(i.screenPos)).rgb;
                backNormal = backNormal * 2.0 - 1.0;  // Decode normal
                float3 normalFactor = saturate(dot(backNormal, i.viewNormal));
                float foamFactor = lerp(_FoamMaxDist, _FoamMinDist, normalFactor);
                float foamDepthFactor = saturate(depthDiff / foamFactor);

                float waveThreshold = foamDepthFactor * _WaveThreshold;
                float2 distortionSample = (tex2D(_DistortionMap, i.distortionUV).xy * 2 - 1) * _DistortionIntensity;

                float2 noiseUV = float2((i.waveUV.x + _Time.y * _WaveScroll.x) + distortionSample.x,
                                        (i.waveUV.y + _Time.y * _WaveScroll.y) + distortionSample.y);
                float waveSample = tex2D(_WaveNoise, noiseUV).r;

                float waveIntensity = smoothstep(waveThreshold - 0.01, waveThreshold + 0.01, waveSample);
                float4 foamColor = _FoamTint;
                foamColor.a *= waveIntensity;

                float2 rippleUV = i.rippleUV + float2(_Time.y * _RippleSpeed * 0.1, _Time.y * _RippleSpeed * 0.1); // Scale speed
                float4 rippleSample = tex2D(_RippleTexture, rippleUV);
                rippleSample.a *= _RippleAlpha * 0.1;  // Scale alpha

                waterColor = waterColor + rippleSample * rippleSample.a;

                return lerp(waterColor, foamColor, foamColor.a);
            }
            ENDCG
        }
    }
}