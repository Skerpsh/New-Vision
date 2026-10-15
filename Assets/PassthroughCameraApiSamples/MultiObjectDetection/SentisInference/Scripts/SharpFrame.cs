// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Draws a crisp rectangular outline with square corners (no sprite, no rounding) inside its RectTransform.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class SharpFrame : MaskableGraphic
    {
        [SerializeField] private float m_thickness = 0.005f;

        /// <summary>Line thickness in the RectTransform's local units (meters for world-space boxes).</summary>
        public float Thickness
        {
            get => m_thickness;
            set
            {
                // Avoid rebuilding the mesh every frame for tiny changes
                if (Mathf.Abs(value - m_thickness) > m_thickness * 0.02f)
                {
                    m_thickness = value;
                    SetVerticesDirty();
                }
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

            // Top and bottom span the full width, left and right fill the gap between them
            AddQuad(vh, new Vector2(r.xMin, r.yMax - t), new Vector2(r.xMax, r.yMax));
            AddQuad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin + t));
            AddQuad(vh, new Vector2(r.xMin, r.yMin + t), new Vector2(r.xMin + t, r.yMax - t));
            AddQuad(vh, new Vector2(r.xMax - t, r.yMin + t), new Vector2(r.xMax, r.yMax - t));
        }

        private void AddQuad(VertexHelper vh, Vector2 min, Vector2 max)
        {
            var i = vh.currentVertCount;
            vh.AddVert(new Vector3(min.x, min.y), color, Vector4.zero);
            vh.AddVert(new Vector3(min.x, max.y), color, Vector4.zero);
            vh.AddVert(new Vector3(max.x, max.y), color, Vector4.zero);
            vh.AddVert(new Vector3(max.x, min.y), color, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
