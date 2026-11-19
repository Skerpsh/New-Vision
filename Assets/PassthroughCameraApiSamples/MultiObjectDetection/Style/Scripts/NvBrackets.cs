// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// HUD corner marks around the RectTransform (four L shapes), optionally with a faint full outline between them.
    /// Used for detection frames, the reticle and panels.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class NvBrackets : MaskableGraphic
    {
        [SerializeField] private float m_length = 12f;
        [SerializeField] private float m_thickness = 2f;
        [SerializeField] private float m_outlineAlpha;

        /// <summary>Arm length of each corner, in local units; values up to 1 mean a fraction of the shorter side.</summary>
        public float Length
        {
            get => m_length;
            set => SetIfChanged(ref m_length, value);
        }

        /// <summary>Line thickness in local units.</summary>
        public float Thickness
        {
            get => m_thickness;
            set => SetIfChanged(ref m_thickness, value);
        }

        /// <summary>Alpha (relative to the colour) of the thin line joining the corners; 0 = corners only.</summary>
        public float OutlineAlpha
        {
            get => m_outlineAlpha;
            set => SetIfChanged(ref m_outlineAlpha, value);
        }

        private void SetIfChanged(ref float field, float value)
        {
            // Avoid rebuilding the mesh every frame for tiny changes
            if (Mathf.Abs(value - field) > Mathf.Max(1e-6f, Mathf.Abs(field) * 0.02f))
            {
                field = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            var t = Mathf.Min(m_thickness, r.width * 0.5f, r.height * 0.5f);
            if (t <= 0f)
            {
                return;
            }
            var len = m_length <= 1f ? m_length * Mathf.Min(r.width, r.height) : m_length;
            len = Mathf.Clamp(len, t, Mathf.Min(r.width, r.height) * 0.5f);

            var c = color;
            // Corners: horizontal arm + vertical arm each
            Quad(vh, r.xMin, r.yMax - t, r.xMin + len, r.yMax, c);
            Quad(vh, r.xMin, r.yMax - len, r.xMin + t, r.yMax - t, c);
            Quad(vh, r.xMax - len, r.yMax - t, r.xMax, r.yMax, c);
            Quad(vh, r.xMax - t, r.yMax - len, r.xMax, r.yMax - t, c);
            Quad(vh, r.xMin, r.yMin, r.xMin + len, r.yMin + t, c);
            Quad(vh, r.xMin, r.yMin + t, r.xMin + t, r.yMin + len, c);
            Quad(vh, r.xMax - len, r.yMin, r.xMax, r.yMin + t, c);
            Quad(vh, r.xMax - t, r.yMin + t, r.xMax, r.yMin + len, c);

            if (m_outlineAlpha > 0f)
            {
                var faint = new Color(c.r, c.g, c.b, c.a * m_outlineAlpha);
                var thin = t * 0.5f;
                Quad(vh, r.xMin + len, r.yMax - thin, r.xMax - len, r.yMax, faint);
                Quad(vh, r.xMin + len, r.yMin, r.xMax - len, r.yMin + thin, faint);
                Quad(vh, r.xMin, r.yMin + len, r.xMin + thin, r.yMax - len, faint);
                Quad(vh, r.xMax - thin, r.yMin + len, r.xMax, r.yMax - len, faint);
            }
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color c)
        {
            if (x1 <= x0 || y1 <= y0)
            {
                return;
            }
            var i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector4.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector4.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
