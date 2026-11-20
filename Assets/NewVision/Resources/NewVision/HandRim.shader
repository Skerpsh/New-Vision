// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only
// Hands as a faint glowing edge, like the Quest system hands: almost see-through in the middle, the silhouette lit
// in the theme's accent. A depth pre-pass keeps fingers behind fingers from adding up. Single-pass instanced stereo.
// Towards the forearm the hand dissolves softly instead of ending in a hard cut (HandOutline feeds the wrist per hand),
// and the whole hand fades with tracking confidence (_Fade).
Shader "NewVision/HandRim"
{
    Properties
    {
        _RimColor ("Rim colour", Color) = (1, 0.61, 0, 1)
        _RimPower ("Rim sharpness", Float) = 3.5
        _RimAlpha ("Rim strength", Float) = 0.7
        _FillAlpha ("Fill (inside)", Float) = 0.04
        _FadeStart ("Arm fade start, m past the wrist", Float) = -0.01
        _FadeEnd ("Arm fade end, m past the wrist", Float) = 0.035
        _Fade ("Confidence fade", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        CGINCLUDE
        #include "UnityCG.cginc"

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float3 worldNormal : TEXCOORD0;
            float3 viewDir : TEXCOORD1;
            float3 worldPos : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        fixed4 _RimColor;
        float _RimPower;
        float _RimAlpha;
        float _FillAlpha;
        float _FadeStart;
        float _FadeEnd;
        // Per hand (MaterialPropertyBlock): wrist joint and the direction from it towards the elbow, both world space
        float4 _WristPos;
        float4 _ArmDir;
        float _Fade;

        // 1 on the hand, smoothly down to 0 along the forearm
        float ArmFade(float3 worldPos)
        {
            float along = dot(worldPos - _WristPos.xyz, _ArmDir.xyz);
            float t = saturate((along - _FadeStart) / max(1e-4, _FadeEnd - _FadeStart));
            return 1.0 - t * t * (3.0 - 2.0 * t);
        }

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.viewDir = WorldSpaceViewDir(v.vertex);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            return o;
        }
        ENDCG

        // Depth only: the nearest surface wins, so overlapping fingers don't stack their glow
        Pass
        {
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            fixed4 fragDepth(v2f i) : SV_Target
            {
                // The dissolved forearm must not hide anything behind it
                clip(ArmFade(i.worldPos) * _Fade - 0.3);
                return 0;
            }
            ENDCG
        }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float facing = saturate(dot(normalize(i.worldNormal), normalize(i.viewDir)));
                float rim = pow(1.0 - facing, _RimPower);
                float alpha = saturate(_FillAlpha + rim * _RimAlpha) * ArmFade(i.worldPos) * _Fade;
                return fixed4(_RimColor.rgb, alpha);
            }
            ENDCG
        }
    }
}
