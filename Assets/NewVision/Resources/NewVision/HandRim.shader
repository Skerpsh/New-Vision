// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only
// Hands as a faint glowing edge, like the Quest system hands: almost see-through in the middle, the silhouette lit
// in the theme's accent. A depth pre-pass keeps fingers behind fingers from adding up. Single-pass instanced stereo.
Shader "NewVision/HandRim"
{
    Properties
    {
        _RimColor ("Rim colour", Color) = (1, 0.61, 0, 1)
        _RimPower ("Rim sharpness", Float) = 3.5
        _RimAlpha ("Rim strength", Float) = 0.7
        _FillAlpha ("Fill (inside)", Float) = 0.04
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
            UNITY_VERTEX_OUTPUT_STEREO
        };

        fixed4 _RimColor;
        float _RimPower;
        float _RimAlpha;
        float _FillAlpha;

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.viewDir = WorldSpaceViewDir(v.vertex);
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
            fixed4 fragDepth(v2f i) : SV_Target { return 0; }
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
                return fixed4(_RimColor.rgb, saturate(_FillAlpha + rim * _RimAlpha));
            }
            ENDCG
        }
    }
}
