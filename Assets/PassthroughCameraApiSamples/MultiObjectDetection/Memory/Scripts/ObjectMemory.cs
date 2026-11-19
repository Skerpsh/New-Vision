// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Object memory: remembers where each kind of object was last seen (from the confirmed detections, a few places per
    /// class) and leads you back to one chosen on the wrist panel (GENERAL > FIND): HUD brackets on the spot, a chevron at
    /// the edge of the view while it's behind you, and when the detector sees it again: FOUND, the marker turns green and the
    /// stick on the headset beeps. Memory lasts for the session (positions are in this session's tracking space).
    /// </summary>
    public class ObjectMemory : MonoBehaviour
    {
        public class Spot
        {
            public string Name;
            public Vector3 Position;
            public float LastSeen;
            public float Score;
        }

        private const float RecordInterval = 0.3f;
        private const float MergeRadius = 0.6f;
        private const int MaxPerClass = 3;
        private const int MaxSpots = 80;
        private const float FoundViewAngle = 30f;
        private const float ArrowAngle = 22f;
        private const float FoundHoldSeconds = 3f;
        private const float MarkerUnits = 100f;
        private const float MarkerAngleDeg = 4f;

        private readonly List<Spot> m_spots = new();
        private readonly List<Spot> m_sorted = new();
        private SentisInferenceUiManager m_ui;
        private Transform m_head;
        private StickClient m_stick;
        private float m_nextRecord;

        private float m_foundAt = -10f;

        // HUD
        private RectTransform m_marker;
        private NvBrackets m_markerBrackets;
        private Image m_markerTag;
        private Text m_markerText;
        private RectTransform m_arrow;
        private Image[] m_chevron;
        private Text m_arrowText;
        private Vector2 m_arrowDir = Vector2.right;

        /// <summary>What is being searched for, or null.</summary>
        public Spot Target { get; private set; }

        /// <summary>The searched object was seen again (the panel shows a message).</summary>
        public event Action<Spot> Found;

        public bool TargetFound => Target != null && Time.time - m_foundAt < FoundHoldSeconds;

        public int Count => m_spots.Count;

        /// <summary>Confirmed detections tracked right now (shown or not).</summary>
        public int InView { get; private set; }

        public void Init(SentisInferenceUiManager ui, Transform head, StickClient stick)
        {
            m_ui = ui;
            m_head = head;
            m_stick = stick;
            NvStyle.EnsureTheme();
            BuildHud();
        }

        /// <summary>Remembered places, the most recently seen first.</summary>
        public IReadOnlyList<Spot> Sorted()
        {
            m_sorted.Clear();
            m_sorted.AddRange(m_spots);
            m_sorted.Sort((a, b) => b.LastSeen.CompareTo(a.LastSeen));
            return m_sorted;
        }

        public void StartFind(Spot spot)
        {
            Target = spot;
            m_foundAt = -10f;
            Debug.Log($"[NewVision] memory: finding {spot.Name} at {spot.Position}");
        }

        public void StopFind() => Target = null;

        public void Clear()
        {
            m_spots.Clear();
            Target = null;
        }

        public float DistanceTo(Spot spot) => m_head != null ? Vector3.Distance(m_head.position, spot.Position) : 0f;

        public static string Age(Spot spot)
        {
            var seconds = Time.time - spot.LastSeen;
            return seconds < 10f ? "NOW" : seconds < 60f ? $"{seconds:0}S" : seconds < 3600f ? $"{seconds / 60f:0}M" : $"{seconds / 3600f:0}H";
        }

        private void Update()
        {
            if (m_ui == null || m_head == null)
            {
                return;
            }
            if (Time.time >= m_nextRecord)
            {
                m_nextRecord = Time.time + RecordInterval;
                Record();
            }
            Guide();
        }

        // ------------------------------------------------------------------ memory

        private void Record()
        {
            var inView = 0;
            foreach (var box in m_ui.m_boxDrawn)
            {
                if (!box.Confirmed || string.IsNullOrEmpty(box.ClassName))
                {
                    continue;
                }
                inView++;
                var position = box.TargetPosition;
                Spot same = null;
                var count = 0;
                Spot oldest = null;
                foreach (var spot in m_spots)
                {
                    if (spot.Name != box.ClassName)
                    {
                        continue;
                    }
                    count++;
                    if (oldest == null || spot.LastSeen < oldest.LastSeen)
                    {
                        oldest = spot;
                    }
                    if (same == null && Vector3.Distance(spot.Position, position) < MergeRadius)
                    {
                        same = spot;
                    }
                }
                if (same != null)
                {
                    same.Position = Vector3.Lerp(same.Position, position, 0.5f);
                    same.LastSeen = Time.time;
                    same.Score = Mathf.Max(same.Score * 0.9f, box.Score);
                    continue;
                }
                if (count >= MaxPerClass && oldest != null)
                {
                    _ = m_spots.Remove(oldest);
                }
                m_spots.Add(new Spot { Name = box.ClassName, Position = position, LastSeen = Time.time, Score = box.Score });
            }
            InView = inView;
            while (m_spots.Count > MaxSpots)
            {
                var oldest = m_spots[0];
                foreach (var spot in m_spots)
                {
                    if (spot.LastSeen < oldest.LastSeen && spot != Target)
                    {
                        oldest = spot;
                    }
                }
                _ = m_spots.Remove(oldest);
            }
            CheckFound();
        }

        /// <summary>Found when the detector sees the searched kind of object in front of you again (it may have been moved).</summary>
        private void CheckFound()
        {
            if (Target == null || TargetFound)
            {
                return;
            }
            foreach (var box in m_ui.m_boxDrawn)
            {
                if (!box.Confirmed || box.ClassName != Target.Name)
                {
                    continue;
                }
                var toBox = box.TargetPosition - m_head.position;
                if (Vector3.Angle(m_head.forward, toBox) > FoundViewAngle)
                {
                    continue;
                }
                Target.Position = box.TargetPosition;
                Target.LastSeen = Time.time;
                m_foundAt = Time.time;
                Debug.Log($"[NewVision] memory: found {Target.Name}");
                if (m_stick != null && m_stick.Connected)
                {
                    _ = m_stick.Send("LOG FOUND " + Target.Name.ToUpperInvariant());
                    _ = m_stick.Send("BEEP");
                }
                Found?.Invoke(Target);
                return;
            }
        }

        // ------------------------------------------------------------------ guidance HUD

        private void Guide()
        {
            if (Target != null && TargetFound && Time.time - m_foundAt >= FoundHoldSeconds - 0.05f)
            {
                Target = null;   // shown as found for a moment, then the search ends
            }
            if (Target == null)
            {
                SetActive(m_marker, false);
                SetActive(m_arrow, false);
                return;
            }

            var color = TargetFound ? NvStyle.Green : NvStyle.Accent;
            var toTarget = Target.Position - m_head.position;
            var distance = toTarget.magnitude;
            var label = $"{Target.Name.ToUpperInvariant()} // {(TargetFound ? "FOUND" : distance.ToString("0.0") + "M")}";

            // Brackets on the remembered spot, the same angular size at any distance, breathing while searching
            SetActive(m_marker, true);
            var markerSize = 2f * Mathf.Max(0.3f, distance) * Mathf.Tan(MarkerAngleDeg * 0.5f * Mathf.Deg2Rad);
            m_marker.SetPositionAndRotation(Target.Position, Quaternion.LookRotation(toTarget.sqrMagnitude > 1e-4f ? toTarget : m_head.forward, Vector3.up));
            m_marker.localScale = Vector3.one * (markerSize / MarkerUnits);
            m_markerBrackets.rectTransform.localScale = Vector3.one * (TargetFound ? 1f : 1f + 0.12f * Mathf.Sin(Time.time * 5f));
            m_markerBrackets.color = color;
            m_markerTag.color = color;
            m_markerText.text = label;
            m_markerTag.rectTransform.sizeDelta = new Vector2(m_markerText.preferredWidth + 24f, 34f);

            // Chevron at the edge of the view when the spot is off to the side or behind
            var outside = Vector3.Angle(m_head.forward, toTarget) > ArrowAngle;
            SetActive(m_arrow, outside);
            if (!outside)
            {
                return;
            }
            var local = Quaternion.Inverse(m_head.rotation) * toTarget;
            var dir = new Vector2(local.x, local.y);
            if (local.z < 0f && dir.sqrMagnitude < 0.01f)
            {
                dir = Vector2.right;   // straight behind: turn either way
            }
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            m_arrowDir = Vector2.Lerp(m_arrowDir, dir, 1f - Mathf.Exp(-10f * Time.deltaTime)).normalized;
            const float distanceFromEye = 0.9f;
            const float radius = 0.2f;
            var offset = m_head.right * (m_arrowDir.x * radius) + m_head.up * (m_arrowDir.y * radius);
            var rotation = Quaternion.LookRotation(m_head.forward, m_head.up) *
                           Quaternion.Euler(0f, 0f, Mathf.Atan2(m_arrowDir.y, m_arrowDir.x) * Mathf.Rad2Deg);
            m_arrow.SetPositionAndRotation(m_head.position + m_head.forward * distanceFromEye + offset, rotation);
            m_arrow.localScale = Vector3.one * 0.0009f;
            foreach (var bar in m_chevron)
            {
                bar.color = color;
            }
            m_arrowText.text = $"{Target.Name.ToUpperInvariant()} {distance:0.0}M";
            m_arrowText.color = color;
            // The text stays upright while the chevron turns
            m_arrowText.rectTransform.localRotation = Quaternion.Inverse(Quaternion.Euler(0f, 0f, Mathf.Atan2(m_arrowDir.y, m_arrowDir.x) * Mathf.Rad2Deg));
        }

        private static void SetActive(Component c, bool active)
        {
            if (c.gameObject.activeSelf != active)
            {
                c.gameObject.SetActive(active);
            }
        }

        private void BuildHud()
        {
            m_marker = NewCanvas("MemoryMarker", 91);
            m_marker.sizeDelta = new Vector2(MarkerUnits, MarkerUnits);
            m_markerBrackets = NvStyle.Brackets("Brackets", m_marker, NvStyle.Accent, 0.3f, 4f);
            m_markerBrackets.OutlineAlpha = 0.25f;
            NvStyle.Stretch(m_markerBrackets.rectTransform);
            m_markerTag = NvStyle.ChamferImage("Tag", m_marker, 8f, NvStyle.Accent);
            var tagRect = m_markerTag.rectTransform;
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = Vector2.zero;
            tagRect.anchoredPosition = new Vector2(0f, 6f);
            m_markerText = NvStyle.Label("Text", tagRect, 22, Color.black);
            NvStyle.Stretch(m_markerText.rectTransform);
            m_markerText.rectTransform.offsetMin = new Vector2(12f, 0f);
            m_marker.gameObject.SetActive(false);

            // Chevron ">" from two bars, pointing along the canvas' +x; the label hangs under it
            m_arrow = NewCanvas("MemoryArrow", 93);
            m_arrow.sizeDelta = new Vector2(120f, 120f);
            m_chevron = new Image[2];
            for (var i = 0; i < 2; i++)
            {
                var bar = NvStyle.Solid("Chevron", m_arrow, NvStyle.Accent);
                bar.rectTransform.sizeDelta = new Vector2(46f, 9f);
                bar.rectTransform.anchoredPosition = new Vector2(0f, i == 0 ? 14f : -14f);
                bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -40f : 40f);
                m_chevron[i] = bar;
            }
            m_arrowText = NvStyle.Label("Text", m_arrow, 22, NvStyle.Accent, TextAnchor.MiddleCenter);
            m_arrowText.rectTransform.sizeDelta = new Vector2(300f, 30f);
            m_arrowText.rectTransform.anchoredPosition = new Vector2(-70f, 0f);
            m_arrow.gameObject.SetActive(false);
        }

        private RectTransform NewCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = order;
            return (RectTransform)go.transform;
        }
    }
}
