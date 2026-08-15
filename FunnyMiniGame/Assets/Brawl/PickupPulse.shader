Shader "Brawl/Pickup Pulse Standard"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        [HDR] _PulseColor ("Pickup Glow Color", Color) = (0.15,1.4,2.6,1)
        _PulseSpeed ("Pulse Speed", Range(0,12)) = 5.5
        _PulseMin ("Minimum Glow", Range(0,3)) = 0.08
        _PulseMax ("Maximum Glow", Range(0,5)) = 1.6
        _RimPower ("Edge Focus", Range(0.5,8)) = 2.2
        [Toggle] _PickupPulseEnabled ("Pickup Pulse Enabled", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; float3 viewDir; };
        fixed4 _Color;
        half _Metallic;
        half _Glossiness;
        half4 _PulseColor;
        half _PulseSpeed;
        half _PulseMin;
        half _PulseMax;
        half _RimPower;
        half _PickupPulseEnabled;

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = albedo.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = albedo.a;

            half pulse01 = 0.5h + 0.5h * sin(_Time.y * _PulseSpeed);
            half pulse = lerp(_PulseMin, _PulseMax, pulse01) * saturate(_PickupPulseEnabled);
            half rim = pow(1.0h - saturate(dot(normalize(IN.viewDir), half3(0, 0, 1))), _RimPower);
            o.Emission = (_PulseColor.rgb * (0.25h + rim * 1.35h) + albedo.rgb * 0.18h) * pulse;
        }
        ENDCG
    }

    FallBack "Standard"
}
