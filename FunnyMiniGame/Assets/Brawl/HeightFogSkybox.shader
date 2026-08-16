Shader "Brawl/Skybox Cubemap Height Fog"
{
    Properties
    {
        [NoScaleOffset] _Tex ("Cubemap (HDR)", Cube) = "grey" {}
        _Tint ("Sky Tint", Color) = (1, 1, 1, 1)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0

        _FogColor ("Fog Color", Color) = (0.55, 0.62, 0.7, 1)
        _FogDensity ("Fog Density", Range(0, 0.25)) = 0.025
        _FogBaseHeight ("Fog Base Height", Float) = 0
        _FogHeightFalloff ("Height Falloff", Range(0.001, 2)) = 0.18
        _FogDistance ("Fog Distance", Range(1, 500)) = 60
        _FogMaxOpacity ("Maximum Fog Opacity", Range(0, 1)) = 0.95
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            samplerCUBE _Tex;
            half4 _Tex_HDR;
            half4 _Tint;
            half _Exposure;
            half _Rotation;

            half4 _FogColor;
            half _FogDensity;
            float _FogBaseHeight;
            half _FogHeightFalloff;
            half _FogDistance;
            half _FogMaxOpacity;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 skyDirection : TEXCOORD0;
            };

            float3 RotateAroundY(float3 direction, float degrees)
            {
                float radiansValue = degrees * UNITY_PI / 180.0;
                float sineValue;
                float cosineValue;
                sincos(radiansValue, sineValue, cosineValue);

                float2 rotated = float2(
                    direction.x * cosineValue - direction.z * sineValue,
                    direction.x * sineValue + direction.z * cosineValue);
                return float3(rotated.x, direction.y, rotated.y);
            }

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.skyDirection = input.vertex.xyz;
                return output;
            }

            float CalculateHeightFog(float3 viewDirection)
            {
                float heightFalloff = max((float)_FogHeightFalloff, 0.001);
                float rayLength = max((float)_FogDistance, 0.001);

                // Density(y) = exp(-(y - baseHeight) * falloff).
                // Integrating this density along the sky ray creates dense horizon/lower fog
                // and naturally clears the sky above the fog layer.
                float cameraHeight = _WorldSpaceCameraPos.y - _FogBaseHeight;
                float densityAtCamera = exp(clamp(-cameraHeight * heightFalloff, -20.0, 20.0));
                float verticalRay = viewDirection.y;
                float opticalDepth;

                if (abs(verticalRay) < 0.001)
                {
                    opticalDepth = densityAtCamera * rayLength;
                }
                else
                {
                    float exponentValue = clamp(-heightFalloff * verticalRay * rayLength, -20.0, 20.0);
                    opticalDepth = densityAtCamera
                        * (1.0 - exp(exponentValue))
                        / (heightFalloff * verticalRay);
                }

                opticalDepth = max(opticalDepth, 0.0);
                float fogAmount = 1.0 - exp(-(float)_FogDensity * opticalDepth);
                return min(saturate(fogAmount), (float)_FogMaxOpacity);
            }

            half4 frag(v2f input) : SV_Target
            {
                float3 viewDirection = normalize(input.skyDirection);
                float3 cubemapDirection = RotateAroundY(viewDirection, _Rotation);
                half4 encodedSky = texCUBE(_Tex, cubemapDirection);
                half3 skyColor = DecodeHDR(encodedSky, _Tex_HDR) * _Tint.rgb * _Exposure;

                half fogAmount = (half)CalculateHeightFog(viewDirection);
                half3 finalColor = lerp(skyColor, _FogColor.rgb, fogAmount);
                return half4(finalColor, 1.0h);
            }
            ENDCG
        }
    }

    FallBack Off
}
