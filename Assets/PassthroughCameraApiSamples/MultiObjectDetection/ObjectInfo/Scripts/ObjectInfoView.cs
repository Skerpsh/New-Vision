// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Visuals of the object info lookup: the reticle in the middle of the view (dot + filling ring)
    /// and the answer panel next to the looked-at object. Everything is built in code.
    /// </summary>
    public class ObjectInfoView : MonoBehaviour
    {
        // Reticle: angular diameter of the ring and the dot, in degrees
        private const float RingAngle = 2.6f;
        private const float DotRatio = 0.28f;
        private const float ReticleUnits = 100f;
        private const float SpinSpeed = 360f;

        // Panel: 1 canvas unit = 1 mm at 1 m distance
        private const float PanelWidth = 440f;
        private const float PanelMaxDistance = 1.1f;
        private const float PanelMinDistance = 0.5f;
        private const float PanelSideAngle = 3.5f;

        private Transform m_head;
        private RectTransform m_reticle;
        private Image m_dot;
        private Image m_ringTrack;
        private Image m_ring;
        private float m_reticleDistance = 2f;

        private RectTransform m_panel;
        private Text m_title;
        private Text m_body;

        public bool PanelVisible => m_panel.gameObject.activeSelf;
        public Vector3 PanelPosition => m_panel.position;

        public void Init(Transform head)
        {
            m_head = head;
            BuildReticle();
            BuildPanel();
            SetReticleVisible(false);
            HidePanel();
        }

        public void SetReticleVisible(bool visible)
        {
            if (m_reticle.gameObject.activeSelf != visible)
            {
                m_reticle.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Places the reticle along the view direction at the distance of what is looked at (so both eyes focus on it
        /// together with the object), with the same apparent size at any distance.
        /// </summary>
        /// <param name="fill">0..1 dwell progress of the ring.</param>
        /// <param name="busy">Waiting for an answer: the ring spins.</param>
        public void UpdateReticle(float targetDistance, float fill, bool busy)
        {
            var t = 1f - Mathf.Exp(-12f * Time.deltaTime);
            m_reticleDistance = Mathf.Lerp(m_reticleDistance, Mathf.Clamp(targetDistance, 0.4f, 6f), t);
            var forward = m_head.forward;
            m_reticle.SetPositionAndRotation(m_head.position + forward * m_reticleDistance, Quaternion.LookRotation(forward, m_head.up));
            var worldSize = 2f * m_reticleDistance * Mathf.Tan(RingAngle * 0.5f * Mathf.Deg2Rad);
            m_reticle.localScale = Vector3.one * (worldSize / ReticleUnits);

            if (busy)
            {
                m_ring.fillAmount = 0.25f;
                m_ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Time.time * SpinSpeed);
            }
            else
            {
                m_ring.fillAmount = fill;
                m_ring.rectTransform.localRotation = Quaternion.identity;
            }
            m_ringTrack.enabled = busy || fill > 0f;
        }

        /// <summary>Shows the panel just to the right of the object, at most ~1 m away so the text stays readable.</summary>
        public void ShowPanel(Vector3 objectPosition)
        {
            var toObject = objectPosition - m_head.position;
            var direction = toObject.normalized;
            var distance = Mathf.Clamp(toObject.magnitude, PanelMinDistance, PanelMaxDistance);
            var right = Vector3.Cross(Vector3.up, direction);
            right = right.sqrMagnitude < 1e-6f ? m_head.right : right.normalized;
            var position = m_head.position + direction * distance + right * (distance * Mathf.Tan(PanelSideAngle * Mathf.Deg2Rad));
            m_panel.localScale = Vector3.one * (0.001f * distance);
            m_panel.SetPositionAndRotation(position, Quaternion.LookRotation(position - m_head.position, Vector3.up));
            m_panel.gameObject.SetActive(true);
        }

        public void HidePanel() => m_panel.gameObject.SetActive(false);

        /// <summary>True if the ray goes through the visible panel (with a small margin around it).</summary>
        public bool IsLookedAt(Ray ray)
        {
            if (!PanelVisible || !new Plane(m_panel.forward, m_panel.position).Raycast(ray, out var enter))
            {
                return false;
            }
            Vector2 local = m_panel.InverseTransformPoint(ray.GetPoint(enter));
            var rect = m_panel.rect;
            const float margin = 60f;
            return local.x > rect.xMin - margin && local.x < rect.xMax + margin &&
                   local.y > rect.yMin - margin && local.y < rect.yMax + margin;
        }

        public void SetText(string title, string body)
        {
            m_title.text = title;
            m_body.text = body;
            m_body.gameObject.SetActive(!string.IsNullOrEmpty(body));
        }

        private void LateUpdate()
        {
            // The panel stays where it appeared, but always turns to face the head and stays level
            if (m_head != null && m_panel != null && m_panel.gameObject.activeSelf)
            {
                var forward = m_panel.position - m_head.position;
                if (Vector3.Cross(forward, Vector3.up).sqrMagnitude > 1e-6f)
                {
                    m_panel.rotation = Quaternion.LookRotation(forward, Vector3.up);
                }
            }
        }

        private void BuildReticle()
        {
            var canvasGo = new GameObject("InfoReticle", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 90;
            m_reticle = (RectTransform)canvasGo.transform;
            m_reticle.sizeDelta = new Vector2(ReticleUnits, ReticleUnits);

            var ringSprite = CircleSprite(128, 0.78f);
            m_ringTrack = CreateImage("RingTrack", m_reticle, ringSprite, new Color(1f, 1f, 1f, 0.18f), ReticleUnits);
            m_ring = CreateImage("Ring", m_reticle, ringSprite, new Color(1f, 1f, 1f, 0.75f), ReticleUnits);
            m_ring.type = Image.Type.Filled;
            m_ring.fillMethod = Image.FillMethod.Radial360;
            m_ring.fillOrigin = (int)Image.Origin360.Top;
            m_ring.fillClockwise = true;
            m_dot = CreateImage("Dot", m_reticle, CircleSprite(64, 0f), new Color(1f, 1f, 1f, 0.55f), ReticleUnits * DotRatio);
        }

        private void BuildPanel()
        {
            var canvasGo = new GameObject("InfoPanel", typeof(RectTransform), typeof(Canvas), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 95;
            m_panel = (RectTransform)canvasGo.transform;
            m_panel.pivot = new Vector2(0f, 0.5f); // grows to the right of the object
            m_panel.sizeDelta = new Vector2(PanelWidth, 100f);

            var background = canvasGo.GetComponent<Image>();
            background.color = new Color(0.05f, 0.07f, 0.09f, 0.78f);
            background.raycastTarget = false;

            var layout = canvasGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 18, 20);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            canvasGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_title = CreateText("Title", m_panel, 34, FontStyle.Bold, new Color(0.55f, 1f, 0.6f));
            m_body = CreateText("Body", m_panel, 26, FontStyle.Normal, Color.white);
        }

        private static Text CreateText(string name, Transform parent, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.lineSpacing = 1.05f;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Anti-aliased white disc, or a ring when innerRadius (0..1 of the outer radius) is above 0.</summary>
        private static Sprite CircleSprite(int size, float innerRadius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            var radius = size * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    var alpha = Mathf.Clamp01(radius - 1f - d);
                    if (innerRadius > 0f)
                    {
                        alpha = Mathf.Min(alpha, Mathf.Clamp01(d - innerRadius * radius));
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
