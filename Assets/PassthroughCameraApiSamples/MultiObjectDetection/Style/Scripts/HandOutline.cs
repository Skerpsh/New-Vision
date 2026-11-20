// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Turns the solid virtual hands (Meta's hand tracking building blocks) into a faint glowing outline in the theme's
    /// accent colour, like the Quest's own hands. OVRMeshRenderer puts its original material back every frame, so the
    /// shader of that material itself is swapped (the hand meshes appear a moment after tracking starts: checked twice a second).
    /// Per hand it also
    /// - dissolves the mesh softly towards the forearm instead of a hard cut at the end of the mesh (wrist fed to the shader),
    /// - fades the hand in and out with tracking confidence instead of popping it on and off,
    /// - smooths the finger joints with a One-Euro filter: held still the millimetre jitter is gone, moving it adds next to no lag.
    /// </summary>
    public class HandOutline : MonoBehaviour
    {
        private static readonly int s_rimColor = Shader.PropertyToID("_RimColor");
        private static readonly int s_wristPos = Shader.PropertyToID("_WristPos");
        private static readonly int s_armDir = Shader.PropertyToID("_ArmDir");
        private static readonly int s_fade = Shader.PropertyToID("_Fade");

        // OVRMeshRenderer hides the hand outright on low confidence; we fade it ourselves instead
        private static readonly FieldInfo s_confidenceBehavior =
            typeof(OVRMeshRenderer).GetField("_confidenceBehavior", BindingFlags.Instance | BindingFlags.NonPublic);

        private const float FadeInPerSecond = 7f;
        private const float FadeOutPerSecond = 9f;
        private const float LowConfidenceAlpha = 0.3f;

        // One-Euro on the finger joint rotations: cutoff = MinCutoff + Beta * angular speed (rad/s), in Hz
        private const float MinCutoff = 3f;
        private const float Beta = 1.5f;
        private const float SpeedCutoff = 1f;

        private class Hand
        {
            public OVRMeshRenderer Mesh;
            public SkinnedMeshRenderer Skin;
            public OVRSkeleton Skeleton;
            public MaterialPropertyBlock Block;
            public float Alpha;
            public Quaternion[] Filtered;
            public float[] Speed;
            public bool Filtering;
        }

        private Shader m_shader;
        private readonly HashSet<Material> m_materials = new();
        private readonly List<Hand> m_hands = new();
        private float m_nextSearch;

        private void Start()
        {
            m_shader = Resources.Load<Shader>("NewVision/HandRim");
            if (m_shader == null)
            {
                Debug.LogWarning("[NewVision] HandRim shader missing, hands keep their look");
                enabled = false;
                return;
            }
            NvStyle.EnsureTheme();
            NvStyle.ThemeChanged += Recolor;
        }

        private void OnDestroy() => NvStyle.ThemeChanged -= Recolor;

        private void Update()
        {
            if (Time.time < m_nextSearch)
            {
                return;
            }
            m_nextSearch = Time.time + 0.5f;
            foreach (var meshRenderer in FindObjectsByType<OVRMeshRenderer>(FindObjectsSortMode.None))
            {
                var skinned = meshRenderer.GetComponent<SkinnedMeshRenderer>();
                var material = skinned != null ? skinned.sharedMaterial : null;
                if (material == null || material.shader == m_shader)
                {
                    continue;
                }
                material.shader = m_shader;
                material.renderQueue = 3000;
                material.SetColor(s_rimColor, NvStyle.Accent);
                skinned.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_materials.Add(material);
                Track(meshRenderer, skinned);
                Debug.Log($"[NewVision] hand outline on {meshRenderer.name}");
            }
        }

        private void Track(OVRMeshRenderer meshRenderer, SkinnedMeshRenderer skinned)
        {
            foreach (var known in m_hands)
            {
                if (known.Mesh == meshRenderer)
                {
                    return;
                }
            }
            s_confidenceBehavior?.SetValue(meshRenderer, OVRMeshRenderer.ConfidenceBehavior.None);
            m_hands.Add(new Hand
            {
                Mesh = meshRenderer,
                Skin = skinned,
                Skeleton = meshRenderer.GetComponent<OVRSkeleton>(),
                Block = new MaterialPropertyBlock(),
            });
        }

        // After OVRSkeleton wrote this frame's raw joints (in its Update), before the hand is skinned and drawn
        private void LateUpdate()
        {
            var dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
            for (var i = m_hands.Count - 1; i >= 0; i--)
            {
                var hand = m_hands[i];
                if (hand.Mesh == null || hand.Skin == null)
                {
                    m_hands.RemoveAt(i);
                    continue;
                }

                var valid = hand.Mesh.IsInitialized && hand.Mesh.IsDataValid;
                var target = !valid ? 0f : hand.Mesh.IsDataHighConfidence ? 1f : LowConfidenceAlpha;
                hand.Alpha = Mathf.MoveTowards(hand.Alpha, target, dt * (target > hand.Alpha ? FadeInPerSecond : FadeOutPerSecond));
                var visible = hand.Alpha > 0.005f && hand.Mesh.IsInitialized;
                if (hand.Skin.enabled != visible)
                {
                    hand.Skin.enabled = visible;
                }
                if (!visible)
                {
                    hand.Filtering = false;   // a hand coming back starts from its fresh pose, not the old one
                    continue;
                }
                if (valid)
                {
                    SmoothFingers(hand, dt);
                }
                Apply(hand);
            }
        }

        private static void SmoothFingers(Hand hand, float dt)
        {
            var skeleton = hand.Skeleton;
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            {
                return;
            }
            var bones = skeleton.Bones;
            if (hand.Filtered == null || hand.Filtered.Length != bones.Count)
            {
                hand.Filtered = new Quaternion[bones.Count];
                hand.Speed = new float[bones.Count];
                hand.Filtering = false;
            }
            for (var b = 0; b < bones.Count; b++)
            {
                var bone = bones[b];
                var t = bone?.Transform;
                // The wrist and palm stay raw: they carry the hand's place in the room and must match the real hand
                if (t == null || bone.Id == OVRSkeleton.BoneId.XRHand_Wrist || bone.Id == OVRSkeleton.BoneId.XRHand_Palm)
                {
                    continue;
                }
                var raw = t.localRotation;
                if (!hand.Filtering)
                {
                    hand.Filtered[b] = raw;
                    hand.Speed[b] = 0f;
                    continue;
                }
                var previous = hand.Filtered[b];
                var speed = Quaternion.Angle(previous, raw) * Mathf.Deg2Rad / dt;
                hand.Speed[b] = Mathf.Lerp(hand.Speed[b], speed, Smoothing(dt, SpeedCutoff));
                var filtered = Quaternion.Slerp(previous, raw, Smoothing(dt, MinCutoff + Beta * hand.Speed[b]));
                hand.Filtered[b] = filtered;
                t.localRotation = filtered;
            }
            hand.Filtering = true;
        }

        private static float Smoothing(float dt, float cutoff)
        {
            var tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }

        private static void Apply(Hand hand)
        {
            var wrist = Vector3.zero;
            var arm = Vector3.zero;
            if (hand.Skeleton != null && hand.Skeleton.IsInitialized && hand.Skeleton.Bones != null)
            {
                Transform wristBone = null;
                Transform middleBone = null;
                foreach (var bone in hand.Skeleton.Bones)
                {
                    if (bone.Id == OVRSkeleton.BoneId.XRHand_Wrist)
                    {
                        wristBone = bone.Transform;
                    }
                    else if (bone.Id == OVRSkeleton.BoneId.XRHand_MiddleProximal)
                    {
                        middleBone = bone.Transform;
                    }
                }
                if (wristBone != null && middleBone != null)
                {
                    wrist = wristBone.position;
                    var toFingers = middleBone.position - wrist;
                    arm = toFingers.sqrMagnitude > 1e-6f ? -toFingers.normalized : Vector3.zero;
                }
            }

            hand.Skin.GetPropertyBlock(hand.Block);
            hand.Block.SetVector(s_wristPos, wrist);
            // No skeleton (old hand skeleton): zero direction = no forearm fade, just the confidence fade
            hand.Block.SetVector(s_armDir, arm);
            hand.Block.SetFloat(s_fade, hand.Alpha);
            hand.Skin.SetPropertyBlock(hand.Block);
        }

        private void Recolor()
        {
            foreach (var material in m_materials)
            {
                if (material != null)
                {
                    material.SetColor(s_rimColor, NvStyle.Accent);
                }
            }
        }
    }
}
