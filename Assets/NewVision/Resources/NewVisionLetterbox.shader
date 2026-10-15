// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

// Fits the camera image into the square network input without stretching (letterbox),
// filling the empty bands with gray (114, 114, 114) like YOLO was trained with.
Shader "Hidden/NewVision/Letterbox"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            // xy: offset of the image inside the output (0..1), zw: size of the image inside the output (0..1)
            float4 _Content;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = (i.uv - _Content.xy) / _Content.zw;
                if (any(uv < 0.0) || any(uv > 1.0))
                {
                    // 114/255 in sRGB, written in linear space (the project uses linear color space)
                    return fixed4(0.168, 0.168, 0.168, 1.0);
                }
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}
