// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Generic;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Turns the solid virtual hands (Meta's hand tracking building blocks) into a faint glowing outline in the theme's
    /// accent colour, like the Quest's own hands. OVRMeshRenderer puts its original material back every frame, so the
    /// shader of that material itself is swapped (the hand meshes appear a moment after tracking starts: checked twice a second).
    /// </summary>
    public class HandOutline : MonoBehaviour
    {
        private static readonly int s_rimColor = Shader.PropertyToID("_RimColor");

        private Shader m_shader;
        private readonly HashSet<Material> m_materials = new();
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
                Debug.Log($"[NewVision] hand outline on {meshRenderer.name}");
            }
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
