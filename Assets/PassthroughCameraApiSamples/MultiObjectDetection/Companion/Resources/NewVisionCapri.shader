// Required Notice: Copyright (c) 2026 mochiiwuff (https://github.com/mochiiwuff/capri-desktop)
// Ported to Unity (HLSL, stereo instancing, linear color space) from capri-desktop's src/renderer/js/sprite.js.
// This file is under the PolyForm Noncommercial License 1.0.0 (https://polyformproject.org/licenses/noncommercial/1.0.0),
// not under the AGPL of the rest of New Vision: no commercial use.
//
// One texture, one quad: every bit of life (head tilt, mane sway and flare, both arms, sitting with
// dangling legs, smear frames, afterimages, line boil, speckles, the violet misregistration, glitch
// slices, teleport collapse) is done in the fragment shader. All coordinates are sprite pixels (y down).
Shader "NewVision/Capri"
{
    Properties
    {
        _MainTex ("Cut-out (loaded as linear: raw sRGB values)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 uSize;   // 240x420
            float uPad;     // empty room around the drawing, for arms, smears and afterimages
            float uSeed;
            float uBoil;
            float uHair;
            float uHead;
            float uMane;
            float uArmL;
            float uArmR;
            float uLongL;
            float uLongR;
            float uSit;
            float uKickL;
            float uKickR;
            float2 uSmear;
            float2 uEcho;
            float uGlitch;
            float uGlitchSeed;
            float uSplit;
            float uCollapse;
            float uDissolve;
            float uSparkle;
            float uGrade;
            float4 uSil;
            float2 uMisreg;
            float3 uRim;
            float uAlpha;

            static float uPx; // sprite pixels per screen pixel, from the derivatives

            // landmarks, in sprite pixels (capri-full.png is 240x420)
            static const float2 NECK = float2(118.0, 152.0);   // the head tilts around this
            static const float2 SHOULDER_L = float2(98.0, 168.0);
            static const float MIRROR = 234.0;                 // the right arm is the left one flipped around x = 117
            static const float2 SHOULDER_R = float2(136.0, 168.0);
            static const float2 MANE = float2(116.0, 92.0);    // middle of the mane, it flares out from here
            static const float2 FACE = float2(124.0, 132.0);   // her mask, kept rigid when the mane flares
            static const float AXIS = 118.0;
            static const float HIP = 232.0;                    // bottom of the torso
            static const float KNEE = 305.0;                   // both knees sit around here
            static const float KNEE_LX = 106.0;
            static const float KNEE_RX = 142.0;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 px : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.px = float2(v.uv.x, 1.0 - v.uv.y) * (uSize + 2.0 * uPad) - uPad;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1.0, 0.0)), f.x),
                            lerp(hash(i + float2(0.0, 1.0)), hash(i + float2(1.0, 1.0)), f.x), f.y);
            }

            float2 rot(float2 p, float2 c, float a)
            {
                float s = sin(a), k = cos(a);
                p -= c;
                return c + float2(k * p.x - s * p.y, s * p.x + k * p.y);
            }

            // no mips: she is always magnified, and the warps below would confuse the derivatives
            float4 tex(float2 p)
            {
                if (p.x < 0.0 || p.y < 0.0 || p.x > uSize.x || p.y > uSize.y) return 0;
                return tex2Dlod(_MainTex, float4(p.x / uSize.x, 1.0 - p.y / uSize.y, 0, 0));
            }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            // The cut-out is darker and duller than the show; pull every pink/crimson toward the
            // on-screen hot red and the mask toward neon green. "red" says how much of the mane it is.
            float3 grade(float3 c, out float red)
            {
                float3 h = rgb2hsv(c);
                float hx = h.x < 0.5 ? h.x + 1.0 : h.x;               // unwrap around red
                float lit = smoothstep(0.1, 0.3, h.z);
                red = smoothstep(0.78, 0.85, hx) * (1.0 - smoothstep(1.06, 1.1, hx)) * lit * smoothstep(0.06, 0.2, h.y);
                float green = smoothstep(0.22, 0.27, h.x) * (1.0 - smoothstep(0.47, 0.52, h.x)) * lit * smoothstep(0.15, 0.3, h.y);
                hx = lerp(hx, 0.968, red * 0.6 * uGrade);
                h.x = frac(hx);
                h.y = lerp(h.y, max(h.y, 0.86), red * uGrade);
                h.y = lerp(h.y, min(1.0, h.y * 1.1 + 0.06), green * uGrade);
                h.z = lerp(h.z, min(1.0, h.z * 1.5 + 0.1), (red + green) * uGrade);
                return hsv2rgb(h);
            }

            // Graded, speckled, premultiplied sample of the figure.
            float4 cel(float2 s)
            {
                float4 c = tex(s);
                if (c.a <= 0.0) return 0;
                float red;
                c.rgb = grade(c.rgb, red);
                // speckles on the red parts: a fixed star field plus a few that twinkle per drawing.
                // One speck is about one screen pixel whatever her size, and sticks to the body.
                if (red > 0.0 && uSparkle > 0.0)
                {
                    float2 cell = floor(s / max(0.8, uPx * 1.2));
                    float star = step(0.976, hash(cell + 3.1)) * 0.85;
                    float tw = step(0.993, hash(cell + uSeed * 1.37));
                    c.rgb = lerp(c.rgb, float3(1.0, 0.95, 0.97), saturate((star + tw) * red * uSparkle));
                }
                return float4(c.rgb * c.a, c.a);
            }

            // Lower legs, in source pixels; the line between them leans with the stance.
            float legSep(float y) { return 117.0 + max(0.0, y - 300.0) * 0.15; }
            float legL(float2 s) { return step(KNEE - 6.0, s.y) * (1.0 - step(legSep(s.y), s.x)); }
            float legR(float2 s) { return step(KNEE - 6.0, s.y) * step(legSep(s.y), s.x); }

            // Sitting, seen from the front: the thighs come toward us so they shrink,
            // and everything below them rides up.
            float thighScale() { return lerp(1.0, 0.3, uSit); }

            float2 unsit(float2 q)
            {
                if (uSit <= 0.0 || q.y <= HIP) return q;
                float c = thighScale();
                if (q.y <= HIP + (KNEE - HIP) * c) q.y = HIP + (q.y - HIP) / c;
                else q.y += (KNEE - HIP) * (1.0 - c);
                return q;
            }

            // The reaching arm, on the left of the cut-out, between the hair spike above it and the hip
            // below. The right arm is this same drawing mirrored (the original hanging one was removed).
            float armL(float2 s)
            {
                if (s.x > 101.0 || s.y > 234.0) return 0.0;
                float top = 159.0 + (98.0 - s.x) * 0.5;
                float bot = s.x < 50.0 ? 234.0 : 178.0 + (98.0 - s.x) * 0.62;
                return step(top, s.y) * step(s.y, bot);
            }

            float2 mirrorArm(float2 s) { return float2(MIRROR - s.x, s.y); }

            // An arm drawn longer than it is (anime exaggeration, so a reach reads past the mane):
            // stretched along its length only, from the shoulder, so it stays as thin. In the left arm's
            // rest frame, which is where both arms are looked up.
            static const float2 ARM_DIR = float2(-0.85954, 0.51108); // shoulder to hand at rest
            float2 stretchArm(float2 s, float k)
            {
                if (k == 1.0) return s;
                float2 v = s - SHOULDER_L;
                float along = dot(v, ARM_DIR);
                return SHOULDER_L + ARM_DIR * (along / k) + (v - ARM_DIR * along);
            }
            float2 armLeft(float2 q) { return stretchArm(rot(q, SHOULDER_L, -uArmL), uLongL); }
            float2 armRight(float2 q) { return stretchArm(mirrorArm(rot(q, SHOULDER_R, uArmR)), uLongR); }

            bool legsRigged() { return uSit > 0.0 || uKickL != 0.0 || uKickR != 0.0; }

            // Everything but the arms, with each lower leg swung on its knee (outward for positive kicks).
            float4 torso(float2 q)
            {
                if (!legsRigged()) return armL(q) > 0.0 ? 0 : cel(q);
                float kneeY = HIP + (KNEE - HIP) * thighScale();
                float2 qB = unsit(q);
                float2 qL = unsit(rot(q, float2(KNEE_LX, kneeY), -uKickL));
                float2 qR = unsit(rot(q, float2(KNEE_RX, kneeY), uKickR));
                float4 b = legL(qB) + legR(qB) + armL(qB) > 0.0 ? 0 : cel(qB);
                float4 l = legL(qL) > 0.0 ? cel(qL) : 0;
                float4 r = legR(qR) > 0.0 ? cel(qR) : 0;
                float4 c = l + b * (1.0 - l.a);
                return r + c * (1.0 - r.a);
            }

            // Both arms turned on their shoulders (positive raises them). They pass in front of the body
            // and the mane (a hand reaching up stays visible), except right at the shoulders, which cover the joints.
            float armAlpha(float2 q)
            {
                float2 qL = armLeft(q);
                float2 qR = armRight(q);
                return max(armL(qL) * tex(qL).a, armL(qR) * tex(qR).a);
            }

            // The head and mane acting, on the body only: an arm raised up into the mane stays straight.
            float2 bend(float2 q)
            {
                // head tilt: everything above the collar bends around the neck, the body stays put
                q = rot(q, NECK, -uHead * smoothstep(162.0, 138.0, q.y));

                // mane flare: the shards bristle outward, unevenly, around a rigid face
                if (uMane != 0.0)
                {
                    float2 d = q - MANE;
                    float spikes = 0.55 + 0.9 * vnoise(float2(atan2(d.y, d.x) * 2.6, 4.0));
                    float w = smoothstep(182.0, 60.0, q.y) * smoothstep(16.0, 30.0, distance(q, FACE));
                    q = MANE + d / (1.0 + uMane * w * 0.32 * spikes);
                }

                // mane sway, strongest at the tips
                float hw = smoothstep(175.0, 5.0, q.y);
                q.x -= uHair * hw * (0.65 + 0.35 * sin(q.y * 0.055));
                q.y -= uHair * 0.22 * hw * sin(q.x * 0.045);
                return q;
            }

            float4 rig(float2 q)
            {
                float2 qL = armLeft(q);
                float2 qR = armRight(q);
                float4 l = armL(qL) > 0.0 ? cel(qL) : 0;
                float4 r = armL(qR) > 0.0 ? cel(qR) : 0;
                float4 arms = l + r * (1.0 - l.a);
                float4 b = torso(bend(q));
                bool joint = distance(q, SHOULDER_L) < 18.0 || distance(q, SHOULDER_R) < 18.0;
                if (joint) return b + arms * (1.0 - b.a);
                // where an arm crosses the body or the mane, ink a violet line around it so it reads
                if (arms.a < 0.5 && b.a > 0.5 && (uArmL != 0.0 || uArmR != 0.0))
                {
                    float edge = max(max(armAlpha(q + float2(1.8, 0.0)), armAlpha(q - float2(1.8, 0.0))),
                                     max(armAlpha(q + float2(0.0, 1.8)), armAlpha(q - float2(0.0, 1.8))));
                    if (edge > 0.5) return float4(uRim, 1.0);
                }
                return arms + b * (1.0 - arms.a);
            }

            float rigAlpha(float2 q) { return rig(q).a; }

            // What happens to the whole drawing: the teleport collapse, glitch slices, line boil.
            float2 settle(float2 p)
            {
                float2 q = p;

                // teleport: squeeze into a vertical line of light
                q.x = AXIS + (q.x - AXIS) / max(1.0 - uCollapse, 0.025);
                q.y = 240.0 + (q.y - 240.0) / (1.0 + uCollapse * 0.4);

                // relic-style slices
                if (uGlitch > 0.0)
                {
                    float bh = lerp(5.0, 26.0, hash(float2(uGlitchSeed, 7.0)));
                    float band = floor(q.y / bh);
                    float on = step(1.0 - uGlitch * 0.6, hash(float2(band, uGlitchSeed)));
                    q.x += on * (hash(float2(band + 3.0, uGlitchSeed)) - 0.5) * 72.0 * uGlitch;
                }

                // boil: the line wobble you get when every drawing is redone by hand
                q += (float2(vnoise(q * 0.075 + uSeed * 13.7), vnoise(q * 0.075 + uSeed * 7.3 + 31.0)) - 0.5) * 2.0 * uBoil;
                return q;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.px;
                uPx = max(fwidth(p.x), fwidth(p.y));

                if (uDissolve > 0.0)
                {
                    float2 g = rot(p, float2(110.0, 210.0), 0.55);
                    float2 cell = floor(g / float2(16.0, 9.0));
                    if (hash(cell + floor(uGlitchSeed)) < uDissolve) discard;
                }

                float2 q = settle(p);
                float4 c = rig(q);
                float3 col = c.rgb;
                float a = c.a;

                // chromatic split: red and blue channels pulled apart
                if (uSplit > 0.0)
                {
                    float4 cr = rig(q + float2(uSplit, 0.0));
                    float4 cb = rig(q - float2(uSplit, uSplit * 0.3));
                    col = float3(cr.r, col.g, cb.b);
                    a = max(a, max(cr.a, cb.a));
                }

                // smear frame: two multiples of the head trailing where it came from
                if (any(uSmear != 0.0))
                {
                    float hw = smoothstep(178.0, 140.0, q.y);
                    [unroll] for (int k = 1; k <= 2; k++)
                    {
                        float4 m = rig(q - uSmear * hw * k * 0.5);
                        col += m.rgb * (1.0 - a);
                        a += m.a * (1.0 - a);
                    }
                }

                // impact frame: flat silhouette
                col = lerp(col, uSil.rgb * a, uSil.a);

                // afterimages: the other minds in her, a green and a red copy pulled apart
                if (any(uEcho != 0.0))
                {
                    float e1 = rigAlpha(q - uEcho) * (1.0 - a);
                    col += float3(0.21, 1.0, 0.48) * e1;
                    a += e1;
                    float e2 = rigAlpha(q + uEcho) * (1.0 - a);
                    col += float3(1.0, 0.13, 0.32) * e2;
                    a += e2;
                }

                // violet misregistration behind the figure
                float rim = rigAlpha(q - uMisreg) * (1.0 - a);
                col += uRim * rim;
                a += rim;

                // everything above is in sRGB values, as on the web; the eye buffer is linear
                if (a > 0.0)
                {
                    col = GammaToLinearSpace(saturate(col / a)) * a;
                }
                return float4(col, a) * uAlpha;
            }
            ENDCG
        }
    }
}
