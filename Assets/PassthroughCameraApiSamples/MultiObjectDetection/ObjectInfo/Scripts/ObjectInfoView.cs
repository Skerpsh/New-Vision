// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Visuals of the object info lookup: the reticle in the middle of the view (dot + filling ring)
    /// and the info card that floats in front of the user. Everything is built in code.
    /// </summary>
    public class ObjectInfoView : MonoBehaviour
    {
        // Reticle: angular diameter of the ring and the dot, in degrees
        private const float RingAngle = 2.6f;
        private const float DotRatio = 0.28f;
        private const float ReticleUnits = 100f;
        private const float SpinSpeed = 360f;

        // Card: 1 canvas unit = 1 mm at 1 m distance
        private const float CardWidth = 620f;
        private const float CardDistance = 0.85f;
        private const float CornerRadius = 28f;
        // Lazy follow: the card stays put while the head turns a little, and glides back to the middle after a bigger turn
        private const float FollowStartAngle = 16f;
        private const float FollowStopAngle = 2f;
        private const float FollowSpeed = 2.5f;
        // Gentle floating: amplitude (m) and speed of the drift, tilt in degrees
        private const float BobHeight = 0.006f;
        private const float BobSide = 0.004f;
        private const float BobTilt = 0.6f;
        private const float FadeInSeconds = 0.3f;
        private const float FadeOutSeconds = 0.2f;

        private const float ImageMaxHeight = 300f;
        public const string CloseHint = "MIDDLE PINCH // CLOSE";

        // Hint under the reticle (QR code seen), in reticle canvas units
        private const float ChipOffset = 92f;
        private const int ChipFontSize = 30;

        private Transform m_head;
        private RectTransform m_reticle;
        private NvBrackets m_brackets;
        private Image m_dot;
        private Image m_ringTrack;   // dwell meter under the brackets: track ...
        private Image m_ring;        // ... and fill
        private Image m_chipBackground;
        private Image m_cardBackground;
        private Image m_cardOutline;
        private Image m_cardMark;
        private RawImage m_cardScan;
        private NvGlitch m_cardGlitch;
        private float m_reticleDistance = 2f;
        // Reticle lag: a lightly under-damped spring on the view direction
        private const float ReticleStiffness = 140f;
        private const float ReticleDamping = 19f;
        private const float ReticleMaxLagDeg = 4f;
        private Vector3 m_reticleDirection;
        private Vector3 m_reticleVelocity;

        private RectTransform m_card;
        private CanvasGroup m_cardGroup;
        private Text m_title;
        private Text m_summary;
        private Text m_body;
        private Text m_footer;
        private LayoutElement m_imageSlot;
        private RawImage m_image;
        private RectTransform m_chip;
        private Text m_chipText;
        private bool m_open;
        private float m_fade; // 0 hidden .. 1 fully shown
        private bool m_following;
        private Vector3 m_direction;
        private Vector3 m_center;
        private float m_halfHeight;
        private float m_floatTime;
        private string m_waitingText;

        public bool PanelVisible => m_open;

        public void Init(Transform head)
        {
            m_head = head;
            NvStyle.EnsureTheme();
            BuildReticle();
            BuildCard();
            Recolor();
            NvStyle.ThemeChanged += Recolor;
            SetReticleVisible(false);
            m_card.gameObject.SetActive(false);
        }

        private void OnDestroy() => NvStyle.ThemeChanged -= Recolor;

        private void Recolor()
        {
            m_brackets.color = NvStyle.WithAlpha(NvStyle.Accent, 0.8f);
            m_dot.color = NvStyle.Accent;
            m_ringTrack.color = NvStyle.WithAlpha(NvStyle.Dim, 0.8f);
            m_ring.color = NvStyle.Accent;
            m_chipBackground.color = NvStyle.Accent;
            m_cardBackground.color = NvStyle.WithAlpha(NvStyle.Background, 0.86f);
            m_cardOutline.color = NvStyle.Accent;
            m_cardMark.color = NvStyle.Accent;
            m_cardScan.color = NvStyle.WithAlpha(NvStyle.Scan, 0.9f);
            m_title.color = NvStyle.Accent;
            m_summary.color = NvStyle.Muted;
            m_body.color = NvStyle.Text;
            m_footer.color = NvStyle.Muted;
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

            // The reticle catches up with the view on a soft spring instead of being glued to the middle:
            // it trails a little when the head turns and settles back with a hint of overshoot.
            // (Only the picture lags; what is looked at is still taken from the exact view direction.)
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            var target = m_head.forward;
            if (m_reticleDirection.sqrMagnitude < 0.5f || !m_reticle.gameObject.activeSelf)
            {
                m_reticleDirection = target;
                m_reticleVelocity = Vector3.zero;
            }
            m_reticleVelocity += ((target - m_reticleDirection) * ReticleStiffness - m_reticleVelocity * ReticleDamping) * dt;
            m_reticleDirection = (m_reticleDirection + m_reticleVelocity * dt).normalized;
            // Never more than a few degrees behind, however fast the turn
            if (Vector3.Angle(m_reticleDirection, target) > ReticleMaxLagDeg)
            {
                m_reticleDirection = Vector3.RotateTowards(target, m_reticleDirection, ReticleMaxLagDeg * Mathf.Deg2Rad, 0f);
            }
            var forward = m_reticleDirection;
            m_reticle.SetPositionAndRotation(m_head.position + forward * m_reticleDistance, Quaternion.LookRotation(forward, m_head.up));
            var worldSize = 2f * m_reticleDistance * Mathf.Tan(RingAngle * 0.5f * Mathf.Deg2Rad);
            m_reticle.localScale = Vector3.one * (worldSize / ReticleUnits);

            // Busy: the meter runs like a loading bar; the brackets breathe in
            m_ring.fillAmount = busy ? Time.time * SpinSpeed / 360f % 1f : fill;
            m_ringTrack.enabled = m_ring.enabled = busy || fill > 0f;
            var squeeze = busy ? 0.85f + 0.15f * Mathf.Cos(Time.time * 6f) : 1f - 0.2f * fill;
            m_brackets.rectTransform.localScale = Vector3.one * squeeze;
        }

        /// <summary>Opens the card in the middle of the view; it fades in and then floats there.</summary>
        public void ShowPanel()
        {
            if (!m_open || !m_card.gameObject.activeSelf)
            {
                m_direction = m_head.forward;
                m_center = m_head.position;
                m_halfHeight = 0f;
                m_following = false;
                m_fade = 0f;
            }
            m_open = true;
            m_card.gameObject.SetActive(true);
            m_cardGlitch.Pulse(0.3f);
            Canvas.ForceUpdateCanvases();
            m_halfHeight = HalfHeight();
            UpdateCard(0f);
        }

        /// <summary>Fades the card out.</summary>
        public void HidePanel()
        {
            m_open = false;
            m_waitingText = null;
        }

        public void SetText(string title, string body) => SetContent(title, null, body);

        /// <summary>
        /// Title, an optional one-line summary and the body. The body may contain the rich text made by
        /// <see cref="Heading"/>; everything else must already be cleaned of '&lt;'.
        /// </summary>
        public void SetContent(string title, string summary, string body)
        {
            m_waitingText = null;
            var newTitle = Escape(title)?.ToUpperInvariant();
            if (newTitle != m_title.text && m_card.gameObject.activeSelf)
            {
                m_cardGlitch.Pulse();   // the answer arrives with a short glitch
            }
            m_title.text = newTitle;
            m_summary.text = Escape(summary ?? "");
            m_summary.gameObject.SetActive(!string.IsNullOrEmpty(summary));
            m_body.text = body ?? "";
            m_body.gameObject.SetActive(!string.IsNullOrEmpty(body));
        }

        /// <summary>Shows a status line in the body with animated dots, e.g. "Asking Gemini".</summary>
        public void SetWaiting(string title, string status)
        {
            SetContent(title, null, status);
            m_waitingText = status;
        }

        public static string Heading(string text) => $"<color={NvStyle.Hex(NvStyle.Accent)}>// {Escape(text.ToUpperInvariant())}</color>";

        public static string Warning(string text) => $"<color={NvStyle.Hex(NvStyle.Yellow)}>!  {Escape(text.ToUpperInvariant())}</color>";

        /// <summary>Makes text safe for the rich text body: '&lt;' would start a tag.</summary>
        public static string Escape(string text) => text?.Replace('<', '‹').Replace('>', '›');

        /// <summary>Picture under the title (link preview), null hides it. The card doesn't own the texture.</summary>
        public void SetImage(Texture texture)
        {
            m_image.texture = texture;
            var visible = texture != null && texture.width > 8 && texture.height > 8;
            m_imageSlot.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }
            var maxWidth = CardWidth - 56f;
            var height = Mathf.Min(ImageMaxHeight, maxWidth * texture.height / texture.width);
            var width = height * texture.width / texture.height;
            m_imageSlot.preferredHeight = height;
            m_image.rectTransform.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Small grey line at the bottom: which buttons / pinches do what.</summary>
        public void SetFooter(string text) => m_footer.text = text ?? CloseHint;

        /// <summary>Hint under the reticle, e.g. "Link · example.com"; null hides it.</summary>
        public void SetChip(string text)
        {
            var visible = !string.IsNullOrEmpty(text);
            if (m_chip.gameObject.activeSelf != visible)
            {
                m_chip.gameObject.SetActive(visible);
            }
            if (visible && m_chipText.text != text)
            {
                m_chipText.text = text;
            }
        }

        private void LateUpdate()
        {
            if (m_head == null || m_card == null || !m_card.gameObject.activeSelf)
            {
                return;
            }

            if (m_waitingText != null)
            {
                m_body.text = m_waitingText + new string('.', 1 + (int)(Time.time * 2.5f) % 3);
            }

            m_fade = Mathf.MoveTowards(m_fade, m_open ? 1f : 0f, Time.deltaTime / (m_open ? FadeInSeconds : FadeOutSeconds));
            if (!m_open && m_fade <= 0f)
            {
                m_card.gameObject.SetActive(false);
                return;
            }
            UpdateCard(Time.deltaTime);
        }

        private void UpdateCard(float deltaTime)
        {
            // Lazy follow of the view direction
            var forward = m_head.forward;
            var angle = Vector3.Angle(m_direction, forward);
            if (angle > FollowStartAngle)
            {
                m_following = true;
            }
            else if (angle < FollowStopAngle)
            {
                m_following = false;
            }
            if (m_following)
            {
                m_direction = Vector3.Slerp(m_direction, forward, 1f - Mathf.Exp(-FollowSpeed * deltaTime)).normalized;
            }
            // The card lags a little behind head movement instead of being glued to it
            m_center = Vector3.Lerp(m_center, m_head.position, 1f - Mathf.Exp(-4f * deltaTime));

            // Smooth size changes while the answer streams in, so the card grows evenly up and down
            var scale = 0.001f * CardDistance;
            m_halfHeight = Mathf.Lerp(m_halfHeight, HalfHeight(), 1f - Mathf.Exp(-8f * deltaTime));
            m_cardScan.uvRect = new Rect(0f, 0f, 1f, Mathf.Max(1f, m_card.rect.height / 5f)); // same line spacing at any height

            m_floatTime += deltaTime;
            var t = m_floatTime;
            var position = m_center + m_direction * CardDistance;
            var rotation = Quaternion.LookRotation(position - m_head.position, Vector3.up);
            var up = rotation * Vector3.up;
            var right = rotation * Vector3.right;
            position += up * (BobHeight * Mathf.Sin(t * 1.1f)) + right * (BobSide * Mathf.Sin(t * 0.7f));
            rotation *= Quaternion.Euler(0f, 0f, BobTilt * Mathf.Sin(t * 0.9f));

            // Pivot is the top edge: put it half the card height above the middle of the view
            position += up * (m_halfHeight * scale);

            // Fade-in: slight zoom from 94 % with ease-out
            var eased = 1f - (1f - m_fade) * (1f - m_fade);
            m_cardGroup.alpha = eased;
            m_card.localScale = Vector3.one * (scale * Mathf.Lerp(0.94f, 1f, eased));
            m_card.SetPositionAndRotation(position, rotation);
        }

        private float HalfHeight() => LayoutUtility.GetPreferredHeight(m_card) * 0.5f;

        private void BuildReticle()
        {
            var canvasGo = new GameObject("InfoReticle", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 90;
            m_reticle = (RectTransform)canvasGo.transform;
            m_reticle.sizeDelta = new Vector2(ReticleUnits, ReticleUnits);

            // HUD brackets around the looked-at spot, a square dot, and a dwell meter under them
            m_brackets = NvStyle.Brackets("Brackets", m_reticle, NvStyle.Accent, 0.32f, 5f);
            ((RectTransform)m_brackets.transform).sizeDelta = new Vector2(ReticleUnits, ReticleUnits);
            m_dot = NvStyle.Solid("Dot", m_reticle, NvStyle.Accent);
            m_dot.rectTransform.sizeDelta = Vector2.one * (ReticleUnits * DotRatio * 0.45f);
            m_ringTrack = NvStyle.Solid("MeterTrack", m_reticle, NvStyle.Dim);
            m_ringTrack.rectTransform.sizeDelta = new Vector2(ReticleUnits, 9f);
            m_ringTrack.rectTransform.anchoredPosition = new Vector2(0f, -ReticleUnits * 0.5f - 14f);
            m_ring = NvStyle.Solid("Meter", m_reticle, NvStyle.Accent);
            m_ring.sprite = NvStyle.White;
            m_ring.type = Image.Type.Filled;
            m_ring.fillMethod = Image.FillMethod.Horizontal;
            m_ring.fillOrigin = (int)Image.OriginHorizontal.Left;
            m_ring.rectTransform.sizeDelta = m_ringTrack.rectTransform.sizeDelta;
            m_ring.rectTransform.anchoredPosition = m_ringTrack.rectTransform.anchoredPosition;

            // Chip: cut-corner accent tag that sizes itself to the text, hanging under the meter
            var chipGo = new GameObject("Chip", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            chipGo.transform.SetParent(m_reticle, false);
            m_chip = (RectTransform)chipGo.transform;
            m_chip.anchoredPosition = new Vector2(0f, -ChipOffset);
            m_chip.pivot = new Vector2(0.5f, 1f);
            m_chipBackground = NvStyle.ChamferImage("Background", m_chip, 10f, NvStyle.Accent);
            m_chipBackground.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NvStyle.Stretch(m_chipBackground.rectTransform);
            var chipLayout = chipGo.GetComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(18, 18, 6, 8);
            chipLayout.childControlWidth = true;
            chipLayout.childControlHeight = true;
            var chipFitter = chipGo.GetComponent<ContentSizeFitter>();
            chipFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            chipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            m_chipText = CreateText("Text", m_chip, ChipFontSize, FontStyle.Normal, Color.black, TextAnchor.MiddleCenter);
            m_chipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_chip.gameObject.SetActive(false);
        }

        private void BuildCard()
        {
            var canvasGo = new GameObject("InfoCard", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 95;
            m_card = (RectTransform)canvasGo.transform;
            m_card.pivot = new Vector2(0.5f, 1f); // grows downwards, the top edge is placed in UpdateCard
            m_card.sizeDelta = new Vector2(CardWidth, 100f);
            m_cardGroup = canvasGo.GetComponent<CanvasGroup>();
            m_cardGroup.interactable = false;
            m_cardGroup.blocksRaycasts = false;

            // Black glass with scanlines, cut corners, an accent outline and a short accent mark on the top edge
            m_cardBackground = canvasGo.GetComponent<Image>();
            m_cardBackground.sprite = NvStyle.Chamfer(16, true);
            m_cardBackground.type = Image.Type.Sliced;
            m_cardBackground.pixelsPerUnitMultiplier = 16f / CornerRadius;
            m_cardBackground.raycastTarget = false;
            m_cardScan = NvStyle.ScanlineOverlay(m_card, 5f, 1200f);
            m_cardScan.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            m_cardOutline = NvStyle.ChamferImage("Outline", m_card, CornerRadius, NvStyle.Accent, false, 2.5f);
            m_cardOutline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NvStyle.Stretch(m_cardOutline.rectTransform);
            m_cardMark = NvStyle.Solid("Mark", m_card, NvStyle.Accent);
            m_cardMark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NvStyle.Place(m_cardMark.rectTransform, CornerRadius + 10f, 0f, 140f, 6f);

            var layout = canvasGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30, 30, 26, 20);
            layout.spacing = 9f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            canvasGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_title = CreateText("Title", m_card, 32, FontStyle.Normal, NvStyle.Accent, TextAnchor.UpperLeft);

            var slotGo = new GameObject("ImageSlot", typeof(RectTransform), typeof(LayoutElement));
            slotGo.transform.SetParent(m_card, false);
            m_imageSlot = slotGo.GetComponent<LayoutElement>();
            var imageGo = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(slotGo.transform, false);
            m_image = imageGo.GetComponent<RawImage>();
            m_image.raycastTarget = false;
            var imageRect = m_image.rectTransform;
            imageRect.anchorMin = imageRect.anchorMax = new Vector2(0f, 0.5f); // left-aligned like the text
            imageRect.pivot = new Vector2(0f, 0.5f);
            slotGo.SetActive(false);
            m_summary = CreateText("Summary", m_card, 22, FontStyle.Normal, NvStyle.Muted, TextAnchor.UpperLeft);
            m_body = CreateText("Body", m_card, 21, FontStyle.Normal, NvStyle.Text, TextAnchor.UpperLeft);
            m_body.supportRichText = true;
            m_body.lineSpacing = 1.12f;
            m_footer = CreateText("Footer", m_card, 15, FontStyle.Normal, NvStyle.Muted, TextAnchor.UpperRight);
            m_footer.text = CloseHint;

            // A short glitch when the card opens and when the answer arrives (no motion: the card floats on its own)
            m_cardGlitch = canvasGo.AddComponent<NvGlitch>();
            m_cardGlitch.Init(m_card, new Vector2(CardWidth, 500f), m_card, false);
        }

        private static Text CreateText(string name, Transform parent, int size, FontStyle style, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = NvStyle.Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.lineSpacing = 1.05f;
            text.color = color;
            text.alignment = alignment;
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

        /// <summary>White rounded rectangle for a 9-sliced background: corners of the given radius, stretchable middle.</summary>
        internal static Sprite RoundedRectSprite(int radius)
        {
            var size = radius * 2 + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Distance to the nearest corner circle centre (0 inside the straight middle part)
                    var dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - size * 0.5f) - 1f - (size * 0.5f - 1f - radius));
                    var dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - size * 0.5f) - 1f - (size * 0.5f - 1f - radius));
                    var alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}
