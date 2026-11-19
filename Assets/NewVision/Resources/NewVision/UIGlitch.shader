// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only
// UI/Default with a "torn screen" glitch: horizontal bands of the UI shift sideways and pick up a colour fringe.
// Driven per canvas by NvGlitch (_Amount 0 = off). Works on text and images; single-pass instanced stereo.
Shader "NewVision/UIGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Amount ("Torn band share 0..1", Float) = 0
        _Shift ("Max shift (canvas units)", Float) = 0
        _Band ("Band height (canvas units)", Float) = 8
        _Seed ("Seed", Float) = 0
        _TearColorA ("Tear colour A", Color) = (0, 0.9, 1, 1)
        _TearColorB ("Tear colour B", Color) = (1, 0, 0.78, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _MainTex_ST;
            float _Amount;
            float _Shift;
            float _Band;
            float _Seed;
            fixed4 _TearColorA;
            fixed4 _TearColorB;
            int _UIVertexColorAlwaysGammaSpace;

            float hash(float n)
            {
                return frac(sin(n * 12.9898 + 78.233) * 43758.5453);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Bands of the canvas (in its own units) are torn sideways while the glitch is on
                float band = floor(v.vertex.y / max(_Band, 0.001));
                float r = hash(band + _Seed * 1.618);
                float torn = step(1.0 - _Amount, r) * step(0.0001, _Amount);
                float side = hash(band * 1.37 + _Seed * 0.73) - 0.5;
                float4 position = v.vertex;
                position.x += torn * side * 2.0 * _Shift;

                OUT.vertex = UnityObjectToClipPos(position);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);

                if (_UIVertexColorAlwaysGammaSpace)
                {
                    if (!IsGammaSpace())
                    {
                        v.color.rgb = UIGammaToLinear(v.color.rgb);
                    }
                }
                fixed4 color = v.color * _Color;
                // A torn band picks up a cyan or magenta fringe
                fixed4 tear = side > 0 ? _TearColorA : _TearColorB;
                color.rgb = lerp(color.rgb, tear.rgb, torn * 0.55);
                OUT.color = color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = IN.color * (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
