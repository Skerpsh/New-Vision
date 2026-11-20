// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Live subtitles at the bottom of the view: a black-glass strip that trails the head on the same soft spring as the
    /// reticle (it lags a little when the head turns and settles back). Finished phrases in the text colour, the phrase
    /// being spoken in muted colour, two lines at most (older words scroll off). Fades out after a pause.
    /// </summary>
    public class SubtitleView : MonoBehaviour
    {
        private const float Width = 820f;
        private const float Distance = 1.3f;
        private const float BelowCenterDeg = 24f;
        private const float MetersPerUnit = 0.0009f;
        private const int FontSize = 32;
        private const int MaxChars = 86;          // about two lines at this width
        private const float HoldSeconds = 5f;
        private const float FadeSeconds = 0.35f;
        // Same feel as the reticle: lightly under-damped spring on the direction, never far behind
        private const float Stiffness = 120f;
        private const float Damping = 18f;
        private const float MaxLagDeg = 6f;

        private SpeechRecognizer m_speech;
        private Transform m_head;

        private RectTransform m_canvas;
        private CanvasGroup m_group;
        private Image m_background;
        private Image m_outline;
        private Image m_mark;
        private RawImage m_scan;
        private Image m_tag;
        private Text m_tagText;
        private Image m_level;
        private Text m_text;
        private NvGlitch m_glitch;

        private readonly StringBuilder m_history = new();
        private string m_partial = "";
        private float m_lastSpeech = -100f;
        private float m_alpha;
        private Quaternion m_view = Quaternion.identity;
        private Vector3 m_angularVelocity;
        private bool m_followStarted;

        public void Init(SpeechRecognizer speech, Transform head)
        {
            m_speech = speech;
            m_head = head;
            NvStyle.EnsureTheme();
            Build();
            m_speech.Partial += OnPartial;
            m_speech.Final += OnFinal;
        }

        private void OnDestroy()
        {
            if (m_speech != null)
            {
                m_speech.Partial -= OnPartial;
                m_speech.Final -= OnFinal;
            }
        }

        private void OnPartial(string text)
        {
            m_partial = Sentence(text);
            if (m_partial.Length > 0)
            {
                m_lastSpeech = Time.time;
            }
            Redraw();
        }

        private void OnFinal(string text)
        {
            var sentence = Sentence(text);
            if (sentence.Length == 0)
            {
                return;
            }
            if (m_history.Length > 0)
            {
                _ = m_history.Append("  ");
            }
            _ = m_history.Append(sentence).Append('.');
            m_partial = "";
            m_lastSpeech = Time.time;
            m_glitch.Pulse(0.15f, 0.25f);   // a phrase lands with a small glitch
            Redraw();
        }

        /// <summary>The model gives lowercase words without punctuation: capitalise the phrase.</summary>
        private static string Sentence(string text)
        {
            text = text?.Trim() ?? "";
            return text.Length == 0 ? "" : char.ToUpper(text[0]) + text.Substring(1);
        }

        /// <summary>Finished phrases + the current one; only the tail that fits in two lines, cut at a word.</summary>
        private void Redraw()
        {
            var history = m_history.ToString();
            var separator = history.Length > 0 && m_partial.Length > 0 ? "  " : "";
            var full = history + separator + m_partial;
            var currentStart = history.Length + separator.Length;   // where the phrase being spoken begins in 'full'

            // Too long for two lines: drop words from the front (at a space), and forget them for good
            var cut = 0;
            if (full.Length > MaxChars)
            {
                cut = full.Length - MaxChars;
                var space = full.IndexOf(' ', cut);
                if (space >= 0 && space < full.Length - 1)
                {
                    cut = space + 1;
                }
                _ = m_history.Remove(0, Mathf.Min(cut, m_history.Length));
            }
            var done = cut < history.Length ? full.Substring(cut, history.Length - cut) : "";
            var current = m_partial.Length > 0 ? full.Substring(Mathf.Max(cut, currentStart)) : "";
            if (cut > 0)
            {
                if (done.Length > 0)
                {
                    done = "…" + done.TrimStart();
                }
                else
                {
                    current = "…" + current.TrimStart();
                }
            }

            var sb = new StringBuilder();
            if (done.Length > 0)
            {
                _ = sb.Append($"<color={NvStyle.Hex(NvStyle.Text)}>").Append(Escape(done)).Append("</color>");
            }
            if (current.Length > 0)
            {
                if (sb.Length > 0)
                {
                    _ = sb.Append("  ");
                }
                _ = sb.Append($"<color={NvStyle.Hex(NvStyle.Muted)}>").Append(Escape(current)).Append("</color>");
            }
            m_text.text = sb.ToString();
        }

        private static string Escape(string text) => text.Replace('<', '‹').Replace('>', '›');

        private void LateUpdate()
        {
            if (m_head == null)
            {
                return;
            }
            var state = m_speech.Current;
            var listening = state == SpeechRecognizer.State.Listening;
            var speaking = Time.time - m_lastSpeech < HoldSeconds || m_partial.Length > 0;
            // While starting up (or on trouble) the strip shows its status for a moment
            var status = state is SpeechRecognizer.State.Installing or SpeechRecognizer.State.Loading or
                SpeechRecognizer.State.NoModel or SpeechRecognizer.State.NoPermission or SpeechRecognizer.State.Error;
            var visible = (listening && speaking) || status;
            var wasVisible = m_alpha > 0f;
            m_alpha = Mathf.MoveTowards(m_alpha, visible ? 1f : 0f, Time.deltaTime / FadeSeconds);
            if (visible && !wasVisible)
            {
                m_glitch.Pulse(0.25f, 0.6f);
            }
            if (m_alpha <= 0f)
            {
                if (m_canvas.gameObject.activeSelf)
                {
                    m_canvas.gameObject.SetActive(false);
                    m_history.Clear();   // a new conversation starts clean
                }
                m_followStarted = false;
                return;
            }
            if (!m_canvas.gameObject.activeSelf)
            {
                m_canvas.gameObject.SetActive(true);
            }
            m_group.alpha = m_alpha;

            if (status)
            {
                m_text.text = $"<color={NvStyle.Hex(NvStyle.Muted)}>" + state switch
                {
                    SpeechRecognizer.State.Installing => "INSTALLING THE SPEECH MODEL...",
                    SpeechRecognizer.State.Loading => "LOADING THE SPEECH MODEL...",
                    SpeechRecognizer.State.NoModel => "NO SPEECH MODEL IN THIS BUILD // Tools/get_speech_model.ps1",
                    SpeechRecognizer.State.NoPermission => "MICROPHONE NOT ALLOWED",
                    _ => "SPEECH RECOGNITION FAILED // SEE LOGS"
                } + "</color>";
            }

            Recolor();
            m_level.fillAmount = Mathf.Clamp01(m_speech.Level * 3f);
            Follow();
        }

        /// <summary>
        /// Fixed to a point of the view (like a caption on a screen), not to the world: it follows the whole head pose,
        /// tilt and roll included, so looking straight down or up never makes it spin. The view it hangs in lags a little
        /// behind head turns on a soft spring and settles back, like the reticle.
        /// </summary>
        private void Follow()
        {
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            var target = m_head.rotation;
            if (!m_followStarted)
            {
                m_view = target;
                m_angularVelocity = Vector3.zero;
                m_followStarted = true;
            }
            // Spring on the rotation: the error as an axis-angle vector drives an angular velocity
            (target * Quaternion.Inverse(m_view)).ToAngleAxis(out var angle, out var axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }
            var error = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad) : Vector3.zero;
            m_angularVelocity += (error * Stiffness - m_angularVelocity * Damping) * dt;
            m_view = Quaternion.AngleAxis(m_angularVelocity.magnitude * dt * Mathf.Rad2Deg, m_angularVelocity.normalized) * m_view;
            // Never more than a few degrees behind, however fast the turn
            if (Quaternion.Angle(m_view, target) > MaxLagDeg)
            {
                m_view = Quaternion.RotateTowards(target, m_view, MaxLagDeg);
            }

            // A fixed spot below the middle of that view, facing the eyes
            var below = m_view * Quaternion.Euler(BelowCenterDeg, 0f, 0f);
            m_canvas.SetPositionAndRotation(m_head.position + below * Vector3.forward * Distance, below);
            m_canvas.localScale = Vector3.one * MetersPerUnit;
        }

        private void Recolor()
        {
            m_background.color = NvStyle.WithAlpha(NvStyle.Background, 0.82f);
            m_outline.color = NvStyle.Dim;
            m_mark.color = NvStyle.Accent;
            m_scan.color = NvStyle.Scan;
            m_tag.color = NvStyle.Accent;
            m_level.color = NvStyle.Accent;
        }

        private void Build()
        {
            var go = new GameObject("Subtitles", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            NvStyle.Crisp(canvas);
            canvas.sortingOrder = 94;
            m_canvas = (RectTransform)go.transform;
            m_canvas.sizeDelta = new Vector2(Width, 100f);
            m_group = go.GetComponent<CanvasGroup>();
            m_group.interactable = false;
            m_group.blocksRaycasts = false;
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(26, 26, 40, 18);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Background, scanlines, outline and the accent mark: out of the layout, stretched behind the text
            m_background = NvStyle.ChamferImage("Background", m_canvas, 18f, NvStyle.Background);
            m_scan = NvStyle.ScanlineOverlay(m_canvas, 4f, 160f);
            m_outline = NvStyle.ChamferImage("Outline", m_canvas, 18f, NvStyle.Dim, false, 2f);
            foreach (var g in new Graphic[] { m_background, m_scan, m_outline })
            {
                g.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                NvStyle.Stretch(g.rectTransform);
            }
            m_mark = NvStyle.Solid("Mark", m_canvas, NvStyle.Accent);
            m_mark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NvStyle.Place(m_mark.rectTransform, 30f, 0f, 120f, 4f);

            // Tag on the top edge: SUBS // RU, and a small microphone level meter next to it
            m_tag = NvStyle.ChamferImage("Tag", m_canvas, 6f, NvStyle.Accent);
            m_tag.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NvStyle.Place(m_tag.rectTransform, 26f, 10f, 118f, 24f);
            m_tagText = NvStyle.Label("Text", m_tag.transform, 16, Color.black, TextAnchor.MiddleCenter);
            m_tagText.text = "SUBS // RU";
            NvStyle.Stretch(m_tagText.rectTransform);
            var track = NvStyle.Solid("LevelTrack", m_canvas, NvStyle.Dim);
            track.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NvStyle.Place(track.rectTransform, 154f, 19f, 80f, 6f);
            m_level = NvStyle.Solid("Level", track.transform, NvStyle.Accent);
            m_level.sprite = NvStyle.White;
            m_level.type = Image.Type.Filled;
            m_level.fillMethod = Image.FillMethod.Horizontal;
            NvStyle.Stretch(m_level.rectTransform);

            m_text = NvStyle.Label("Text", m_canvas, FontSize, NvStyle.Text, TextAnchor.UpperLeft);
            m_text.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_text.supportRichText = true;
            m_text.lineSpacing = 1.1f;

            m_glitch = go.AddComponent<NvGlitch>();
            m_glitch.Init(m_canvas, new Vector2(Width, 140f), m_canvas, false);

            go.SetActive(false);
        }
    }
}
