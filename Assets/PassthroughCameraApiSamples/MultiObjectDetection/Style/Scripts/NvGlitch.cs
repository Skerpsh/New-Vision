// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only
// Impact FX after Vartovyi (github.com/Skerpsh/vartovyi, MIT): the picture hangs on a damped spring and lags behind
// motion; a knock tears rows sideways, throws coloured fragments and, on a hard hit, flashes.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Glitch effects for one world-space canvas: call <see cref="Feel"/> every frame with the motion of what the
    /// canvas is attached to (m/s² in canvas right/up axes), or <see cref="Pulse"/> for a short glitch on a change.
    /// Respects <see cref="NvStyle.FxLevel"/>.
    /// </summary>
    public class NvGlitch : MonoBehaviour
    {
        private enum Knock { None, Swing, Strike }

        // Light shakes already count: a quick flick of the wrist is ~10 m/s², a sharp jolt 25+
        private const float BriskAccel = 5f;
        private const float SwingAccel = 11f;
        private const float StrikeAccel = 24f;
        private const float KnockGap = 0.3f;
        private const float SwingSeconds = 0.6f;
        private const float StrikeSeconds = 1.2f;
        private const float FlashSeconds = 0.09f;
        private const float PushPerAccel = 420f;   // canvas units/s of spring speed per (m/s²·s)
        private const float Stiffness = 360f;
        private const float Damping = 9f;

        private struct Spring
        {
            public float X, V;

            public void Step(float dt, float limit)
            {
                V += (-Stiffness * X - Damping * V) * dt;
                X += V * dt;
                if (X > limit) { X = limit; V = 0f; }
                if (X < -limit) { X = -limit; V = 0f; }
            }

            public bool Moving => Mathf.Abs(X) > 0.5f || Mathf.Abs(V) > 5f;
        }

        private RectTransform m_content;
        private bool m_moveContent;
        private RectTransform m_overlay;
        private Image m_flash;
        private readonly List<Image> m_fragments = new();
        private Material m_material;
        private Vector2 m_size;
        private Vector2 m_basePosition;
        private float m_limit;

        private Spring m_x, m_y, m_skew;
        private Knock m_kind;
        private float m_knockTime = -10f;
        private float m_lastKnock = -10f;
        private float m_pulseUntil;
        private float m_pulseStrength = 0.35f;
        private float m_flashUntil;

        private static readonly int s_amount = Shader.PropertyToID("_Amount");
        private static readonly int s_shift = Shader.PropertyToID("_Shift");
        private static readonly int s_band = Shader.PropertyToID("_Band");
        private static readonly int s_seed = Shader.PropertyToID("_Seed");

        /// <summary>
        /// content: what shakes and tears (all its Graphics get the glitch material); size: its size in canvas units;
        /// overlay parent: where fragments are drawn on top (usually the canvas root).
        /// </summary>
        public void Init(RectTransform content, Vector2 size, Transform overlayParent, bool moveContent = true)
        {
            m_content = content;
            m_moveContent = moveContent;
            m_basePosition = content.anchoredPosition;
            m_size = size;
            m_limit = Mathf.Min(size.x, size.y) * 0.08f;

            var shader = Resources.Load<Shader>("NewVision/UIGlitch");
            if (shader != null)
            {
                m_material = new Material(shader);
                m_material.SetFloat(s_band, Mathf.Max(4f, size.y / 40f));
                ApplyMaterial();
            }
            else
            {
                Debug.LogWarning("[NewVision] UIGlitch shader missing");
            }

            m_overlay = NvStyle.Rect("GlitchOverlay", overlayParent);
            m_overlay.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            NvStyle.Stretch(m_overlay);
            m_flash = NvStyle.Solid("Flash", m_overlay, Color.white);
            NvStyle.Stretch(m_flash.rectTransform);
            m_flash.enabled = false;
            for (var i = 0; i < 14; i++)
            {
                var fragment = NvStyle.Solid("Fragment", m_overlay, Color.white);
                fragment.rectTransform.anchorMin = fragment.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                fragment.enabled = false;
                m_fragments.Add(fragment);
            }
        }

        /// <summary>Gives every Graphic under the content the glitch material (call again after adding UI).</summary>
        public void ApplyMaterial()
        {
            if (m_material == null || m_content == null)
            {
                return;
            }
            foreach (var graphic in m_content.GetComponentsInChildren<Graphic>(true))
            {
                // Pictures (link previews) keep their own material
                if (graphic is not RawImage || graphic.name == "Scanlines")
                {
                    graphic.material = m_material;
                }
            }
        }

        /// <summary>The overlay is a child of the canvas: keep it drawn last after adding more UI.</summary>
        public void BringOverlayToFront() => m_overlay?.SetAsLastSibling();

        /// <summary>A short burst of torn lines, as when a screen changes.</summary>
        public void Pulse(float seconds = 0.22f, float strength = 0.35f)
        {
            if (NvStyle.FxLevel > 0)
            {
                // A stronger pulse during a weaker one takes over; a new one after the last ended starts fresh
                m_pulseStrength = Time.time < m_pulseUntil ? Mathf.Max(m_pulseStrength, strength) : strength;
                m_pulseUntil = Mathf.Max(m_pulseUntil, Time.time + seconds);
            }
        }

        /// <summary>Feeds the motion of the thing the canvas hangs on: acceleration in m/s², along canvas right / up.</summary>
        public void Feel(Vector2 accel, float twistDegPerSec = 0f)
        {
            var level = NvStyle.FxLevel;
            var dt = Mathf.Min(0.05f, Time.deltaTime);
            if (level == 0)
            {
                m_x = m_y = m_skew = default;
                m_kind = Knock.None;
                return;
            }
            var magnitude = accel.magnitude;
            var knock = Knock.None;
            if (magnitude >= StrikeAccel)
            {
                knock = Knock.Strike;
            }
            else if (level == 2 && magnitude >= SwingAccel)
            {
                knock = Knock.Swing;
            }

            // The picture lags behind the motion. On LOW only knocks move it, on HIGH any brisk motion does
            if (knock != Knock.None || (level == 2 && magnitude > BriskAccel))
            {
                var push = PushPerAccel * dt * (knock != Knock.None ? 2f : 1f);
                m_x.V -= accel.x * push;
                m_y.V -= accel.y * push;
            }
            if (level == 2 && Mathf.Abs(twistDegPerSec) > 150f)
            {
                m_skew.V += twistDegPerSec * 0.02f * dt * 50f;
            }

            if (knock != Knock.None && Time.time - m_lastKnock > KnockGap && (KnockLevel() <= 0f || knock >= m_kind))
            {
                m_lastKnock = Time.time;
                m_kind = knock;
                m_knockTime = Time.time;
                if (knock == Knock.Strike)
                {
                    m_flashUntil = Time.time + FlashSeconds;
                }
            }
        }

        private float KnockLevel()
        {
            if (m_kind == Knock.None)
            {
                return 0f;
            }
            var duration = m_kind == Knock.Strike ? StrikeSeconds : SwingSeconds;
            var e = Time.time - m_knockTime;
            if (e >= duration)
            {
                m_kind = Knock.None;
                return 0f;
            }
            return (m_kind == Knock.Swing ? 0.5f : 1f) * (1f - e / duration);
        }

        private void LateUpdate()
        {
            if (m_content == null)
            {
                return;
            }
            var dt = Mathf.Min(0.05f, Time.deltaTime);
            m_x.Step(dt, m_limit);
            m_y.Step(dt, m_limit * 0.75f);
            m_skew.Step(dt, 3f);
            if (m_moveContent)
            {
                m_content.anchoredPosition = m_basePosition + new Vector2(m_x.X, m_y.X);
                m_content.localRotation = Quaternion.Euler(0f, 0f, m_skew.X);
            }

            var k = KnockLevel();
            var pulse = Time.time < m_pulseUntil ? m_pulseStrength : 0f;
            var tear = Mathf.Max(k, pulse);
            if (m_material != null)
            {
                m_material.SetFloat(s_amount, tear > 0f ? Mathf.Lerp(0.08f, 0.35f, tear) : 0f);
                m_material.SetFloat(s_shift, m_size.x * Mathf.Lerp(0.02f, 0.07f, tear));
                m_material.SetFloat(s_seed, Random.Range(0f, 1000f)); // new tears every frame: the picture shudders
            }

            // Coloured fragments and stuck lines over the picture
            var count = tear > 0f ? 2 + (int)(tear * 10f) : 0;
            for (var i = 0; i < m_fragments.Count; i++)
            {
                var fragment = m_fragments[i];
                var on = i < count;
                fragment.enabled = on;
                if (!on)
                {
                    continue;
                }
                var stuckColumn = i % 5 == 0;
                var w = stuckColumn ? 2f : Random.Range(m_size.x * 0.05f, m_size.x * 0.45f);
                var h = stuckColumn ? m_size.y : Random.Range(1.5f, 2.5f + 6f * tear);
                fragment.rectTransform.sizeDelta = new Vector2(w, h);
                fragment.rectTransform.anchoredPosition = new Vector2(
                    Random.Range(-m_size.x * 0.5f, m_size.x * 0.5f),
                    stuckColumn ? 0f : Random.Range(-m_size.y * 0.5f, m_size.y * 0.5f));
                fragment.color = (i % 4) switch
                {
                    0 => NvStyle.Cyan,
                    1 => NvStyle.Magenta,
                    2 => NvStyle.Accent,
                    _ => Color.white
                };
            }

            var flashing = Time.time < m_flashUntil;
            m_flash.enabled = flashing;
            if (flashing)
            {
                m_flash.color = NvStyle.WithAlpha(NvStyle.Accent, 0.35f);
            }
        }

        private void OnDestroy()
        {
            if (m_material != null)
            {
                Destroy(m_material);
            }
        }
    }
}
