// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Look at the laptop (the detector sees "laptop") and use microgestures of either hand:
    /// thumb tap on the side of the index finger = open Explorer (Win+E), thumb swipe left = close the window (Alt+F4).
    /// The commands go over Bluetooth to the stick, which types them on the laptop as its Bluetooth keyboard.
    /// </summary>
    public class LaptopGestures : MonoBehaviour
    {
        private const string EnabledKey = "NewVision.LaptopGestures";
        /// <summary>Boxes flicker; the laptop still counts as looked at this long after the ray left its box.</summary>
        private const float GazeGrace = 0.8f;
        private const float Cooldown = 0.6f;
        private static readonly string[] TargetClasses = { "laptop" };

        /// <summary>True while the user is looking at the laptop: "What is this?" must not start by dwelling on it.</summary>
        public static bool LookingAtLaptop { get; private set; }

        private StickClient m_stick;
        private HandTracker m_hands;
        private SentisInferenceUiManager m_ui;
        private Transform m_head;

        private float m_lastGazeTime = -100f;
        private Vector3 m_anchor;
        private float m_nextCommand;
        private RectTransform m_hint;
        private Image m_background;
        private Text m_hintText;
        private string m_flash;
        private float m_flashUntil;

        public bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public void Init(StickClient stick, HandTracker hands, SentisInferenceUiManager ui, Transform head)
        {
            m_stick = stick;
            m_hands = hands;
            m_ui = ui;
            m_head = head;
            m_stick.Reply += OnReply;
            BuildHint();
        }

        private void Update()
        {
            LookingAtLaptop = false;
            if (m_head == null || !Enabled || BrowserPanel.IsOpen)
            {
                m_hint.gameObject.SetActive(false);
                return;
            }

            var ray = new Ray(m_head.position, m_head.forward);
            if (FindLaptop(ray, out var anchor))
            {
                m_lastGazeTime = Time.time;
                m_anchor = anchor;
            }
            LookingAtLaptop = Time.time - m_lastGazeTime < GazeGrace;

            if (LookingAtLaptop && Time.time >= m_nextCommand)
            {
                // The left hand belongs to the wrist panel while it is open
                var right = m_hands.Get(HandTracker.Right).Gesture;
                var left = WristPanel.IsVisible ? OVRHand.MicrogestureType.NoGesture : m_hands.Get(HandTracker.Left).Gesture;
                var gesture = right != OVRHand.MicrogestureType.NoGesture ? right : left;
                switch (gesture)
                {
                    case OVRHand.MicrogestureType.ThumbTap:
                        Run("explorer", "EXPLORER");
                        break;
                    case OVRHand.MicrogestureType.SwipeLeft:
                        Run("close", "CLOSE");
                        break;
                }
            }

            UpdateHint();
        }

        private void Run(string command, string label)
        {
            m_nextCommand = Time.time + Cooldown;
            if (!m_stick.Connected)
            {
                Flash("NO STICK");
                return;
            }
            _ = m_stick.Send("CMD " + command);
            Flash(label + " ...");
        }

        private void OnReply(string reply)
        {
            if (reply.StartsWith("OK explorer"))
            {
                Flash("EXPLORER // OK");
            }
            else if (reply.StartsWith("OK close"))
            {
                Flash("CLOSED // OK");
            }
            else if (reply.StartsWith("ERR notpaired"))
            {
                Flash("NOT PAIRED");
            }
        }

        private void Flash(string text)
        {
            m_flash = text;
            m_flashUntil = Time.time + 1.5f;
        }

        /// <summary>Confirmed laptop box the view ray goes through; anchor = top middle of the box.</summary>
        private bool FindLaptop(Ray ray, out Vector3 anchor)
        {
            anchor = default;
            foreach (var box in m_ui.m_boxDrawn)
            {
                if (!box.Confirmed || box.BoxRectTransform == null || !box.BoxRectTransform.gameObject.activeInHierarchy ||
                    Array.FindIndex(TargetClasses, c => string.Equals(c, box.ClassName, StringComparison.OrdinalIgnoreCase)) < 0)
                {
                    continue;
                }
                var rect = box.BoxRectTransform;
                if (!new Plane(rect.forward, rect.position).Raycast(ray, out var enter))
                {
                    continue;
                }
                Vector2 local = rect.InverseTransformPoint(ray.GetPoint(enter));
                var size = rect.sizeDelta;
                // A bit of margin around the box: the laptop screen is often only part of it
                if (Mathf.Abs(local.x) <= size.x * 0.6f && Mathf.Abs(local.y) <= size.y * 0.6f)
                {
                    anchor = rect.TransformPoint(new Vector3(0f, size.y * 0.5f, 0f));
                    return true;
                }
            }
            return false;
        }

        private void UpdateHint()
        {
            var flashing = Time.time < m_flashUntil;
            var visible = LookingAtLaptop || flashing;
            m_hint.gameObject.SetActive(visible && m_anchor != Vector3.zero);
            if (!m_hint.gameObject.activeSelf)
            {
                return;
            }
            m_background.color = flashing && m_flash.StartsWith("NO") ? NvStyle.Yellow : NvStyle.Accent;
            var connected = m_stick.Connected;
            m_hintText.text = flashing ? m_flash
                : !connected ? "LAPTOP // NO STICK"
                : !m_stick.LaptopPaired ? "LAPTOP // NOT PAIRED"
                : "TAP: EXPLORER  //  SWIPE L: CLOSE";
            var distance = Vector3.Distance(m_head.position, m_anchor);
            m_hint.position = m_anchor + Vector3.up * (0.04f * distance);
            m_hint.rotation = Quaternion.LookRotation(m_hint.position - m_head.position, Vector3.up);
            m_hint.localScale = Vector3.one * (0.0011f * distance); // same angular size at any distance
        }

        private void BuildHint()
        {
            var go = new GameObject("LaptopHint", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 92;
            m_hint = (RectTransform)go.transform;
            m_hint.pivot = new Vector2(0.5f, 0f);
            // Cut-corner accent tag with black text, like the detection tags
            m_background = go.GetComponent<Image>();
            m_background.sprite = NvStyle.Chamfer(16, true);
            m_background.type = Image.Type.Sliced;
            m_background.pixelsPerUnitMultiplier = 16f / 10f;
            m_background.color = NvStyle.Accent;
            m_background.raycastTarget = false;
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 6, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            m_hintText = textGo.GetComponent<Text>();
            m_hintText.font = NvStyle.Font;
            m_hintText.fontSize = 24;
            m_hintText.color = Color.black;
            m_hintText.alignment = TextAnchor.MiddleCenter;
            m_hintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_hintText.raycastTarget = false;
            go.SetActive(false);
        }
    }
}
