// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// In-headset menu to choose which object classes are detected. Controlled with controller buttons and thumbsticks:
    /// Y or Menu opens/closes, thumbstick up/down moves (left/right jumps a page), A or trigger toggles, B closes.
    /// The selection is saved between app launches.
    /// </summary>
    public class ClassFilterMenu : MonoBehaviour
    {
        private const string PrefsKey = "NewVision.EnabledClasses";
        private const int VisibleRows = 13;
        private const int PageJump = 10;
        private const float MenuDistance = 1.0f;
        private const float StickThreshold = 0.6f;
        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.08f;

        private static readonly string[] s_furnitureAndPlants =
        {
            "Chair", "Couch", "Potted plant", "Bed", "Dining table", "Toilet", "TV", "Laptop",
            "Refrigerator", "Sink", "Oven", "Microwave", "Clock", "Vase"
        };

        private enum MenuAction { AllOn, AllOff, FurnitureAndPlants }
        private static readonly (MenuAction action, string title)[] s_actions =
        {
            (MenuAction.AllOn, "Enable all"),
            (MenuAction.AllOff, "Disable all"),
            (MenuAction.FurnitureAndPlants, "Only furniture & plants"),
        };

        /// <summary>True while the menu is shown; other controls should ignore buttons meanwhile.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// True while the menu is open and on the frame it was closed, so the button that closed the menu
        /// is not also handled by other controls (scripts' Update order within a frame is not defined).
        /// </summary>
        public static bool BlocksInput => IsOpen || Time.frameCount <= s_lastOpenFrame + 1;
        private static int s_lastOpenFrame = -10;

        /// <summary>Enabled flag per class id.</summary>
        public bool[] EnabledClasses { get; private set; }

        /// <summary>Raised whenever the selection changes.</summary>
        public event Action<ClassFilterMenu> Changed;

        private string[] m_labels;
        private Transform m_head;
        private Canvas m_canvas;
        private Text m_text;
        private int m_cursor;
        private int m_scrollTop;
        private int m_stickDirection;
        private float m_nextRepeatTime;
        private readonly StringBuilder m_sb = new();
        private string[] m_modelNames;
        private int m_modelIndex;
        private Action<int> m_onModelSelected;

        // Rows: [model switch] + quick actions + one row per class
        private int ModelRows => m_modelNames != null && m_modelNames.Length > 1 ? 1 : 0;
        private int FirstClassRow => ModelRows + s_actions.Length;
        private int RowCount => FirstClassRow + m_labels.Length;

        /// <summary>Adds a "Model" row; pressing A on it cycles through the models and calls onSelect with the new index.</summary>
        public void SetModelOptions(string[] modelNames, int currentIndex, Action<int> onSelect)
        {
            m_modelNames = modelNames;
            m_modelIndex = currentIndex;
            m_onModelSelected = onSelect;
            if (IsOpen)
            {
                Redraw();
            }
        }

        public void Init(TextAsset labelsAsset)
        {
            m_labels = labelsAsset.text.Split('\n');
            for (var i = 0; i < m_labels.Length; i++)
            {
                m_labels[i] = m_labels[i].Trim();
            }
            EnabledClasses = new bool[m_labels.Length];
            Load();

            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
            {
                m_head = cameraRig.centerEyeAnchor;
            }
            else if (Camera.main != null)
            {
                m_head = Camera.main.transform;
            }

            BuildUi();
            SetOpen(false);
        }

        private void OnDestroy() => IsOpen = false;

        private void Update()
        {
            if (m_labels == null)
            {
                return;
            }

            if (OVRInput.GetDown(OVRInput.RawButton.Y) || OVRInput.GetDown(OVRInput.RawButton.Start))
            {
                SetOpen(!IsOpen);
                return;
            }
            if (!IsOpen)
            {
                return;
            }
            s_lastOpenFrame = Time.frameCount;
            if (OVRInput.GetDown(OVRInput.RawButton.B))
            {
                SetOpen(false);
                return;
            }

            HandleStick();

            if (OVRInput.GetDown(OVRInput.RawButton.A) || OVRInput.GetDown(OVRInput.RawButton.X) ||
                OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) || OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger))
            {
                ActivateRow(m_cursor);
            }
        }

        private void HandleStick()
        {
            var stick = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick) + OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
            // 1 = down, -1 = up, 2 = page down, -2 = page up
            var direction = 0;
            if (Mathf.Abs(stick.y) >= Mathf.Abs(stick.x))
            {
                direction = stick.y > StickThreshold ? -1 : stick.y < -StickThreshold ? 1 : 0;
            }
            else
            {
                direction = stick.x > StickThreshold ? 2 : stick.x < -StickThreshold ? -2 : 0;
            }

            if (direction == 0)
            {
                m_stickDirection = 0;
                return;
            }
            if (direction != m_stickDirection)
            {
                m_stickDirection = direction;
                m_nextRepeatTime = Time.time + RepeatDelay;
            }
            else if (Time.time >= m_nextRepeatTime)
            {
                m_nextRepeatTime = Time.time + RepeatInterval;
            }
            else
            {
                return;
            }

            var step = Mathf.Abs(direction) == 2 ? PageJump * Math.Sign(direction) : direction;
            m_cursor = Mathf.Clamp(m_cursor + step, 0, RowCount - 1);
            Redraw();
        }

        private void ActivateRow(int row)
        {
            if (row < ModelRows)
            {
                m_modelIndex = (m_modelIndex + 1) % m_modelNames.Length;
                m_onModelSelected?.Invoke(m_modelIndex);
                Redraw();
                return;
            }

            row -= ModelRows;
            if (row < s_actions.Length)
            {
                switch (s_actions[row].action)
                {
                    case MenuAction.AllOn:
                        SetAll(true);
                        break;
                    case MenuAction.AllOff:
                        SetAll(false);
                        break;
                    case MenuAction.FurnitureAndPlants:
                        SetAll(false);
                        for (var i = 0; i < m_labels.Length; i++)
                        {
                            EnabledClasses[i] = Array.IndexOf(s_furnitureAndPlants, m_labels[i]) >= 0;
                        }
                        break;
                }
            }
            else
            {
                var classId = row - s_actions.Length;
                EnabledClasses[classId] = !EnabledClasses[classId];
            }

            Save();
            Redraw();
            Changed?.Invoke(this);
        }

        private void SetAll(bool value)
        {
            for (var i = 0; i < EnabledClasses.Length; i++)
            {
                EnabledClasses[i] = value;
            }
        }

        private void SetOpen(bool open)
        {
            IsOpen = open;
            s_lastOpenFrame = Time.frameCount;
            m_canvas.gameObject.SetActive(open);
            if (open)
            {
                PlaceInFrontOfHead();
                Redraw();
            }
        }

        private void PlaceInFrontOfHead()
        {
            if (m_head == null)
            {
                return;
            }
            // Level with the horizon, at eye height, straight ahead
            var forward = Vector3.ProjectOnPlane(m_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(m_head.up, Vector3.up);
            }
            forward.Normalize();
            var position = m_head.position + forward * MenuDistance;
            m_canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        private void Redraw()
        {
            if (m_cursor < m_scrollTop)
            {
                m_scrollTop = m_cursor;
            }
            else if (m_cursor >= m_scrollTop + VisibleRows)
            {
                m_scrollTop = m_cursor - VisibleRows + 1;
            }

            var enabledCount = 0;
            foreach (var enabled in EnabledClasses)
            {
                if (enabled)
                {
                    enabledCount++;
                }
            }

            m_sb.Clear();
            m_sb.Append($"<b>Objects to detect</b>  ({enabledCount}/{EnabledClasses.Length})\n\n");
            m_sb.Append(m_scrollTop > 0 ? "<color=#888888>   more ^</color>\n" : "\n");
            for (var row = m_scrollTop; row < Mathf.Min(m_scrollTop + VisibleRows, RowCount); row++)
            {
                var selected = row == m_cursor;
                string line;
                if (row < ModelRows)
                {
                    line = $"<color=#FFB86C>Model: {m_modelNames[m_modelIndex]}</color>";
                }
                else if (row < FirstClassRow)
                {
                    line = $"<color=#9CC8FF>» {s_actions[row - ModelRows].title}</color>";
                }
                else
                {
                    var classId = row - FirstClassRow;
                    line = EnabledClasses[classId]
                        ? $"<color=#5CFD9B>[x]</color> {m_labels[classId]}"
                        : $"<color=#777777>[ ]  {m_labels[classId]}</color>";
                }
                m_sb.Append(selected ? $"<color=#FFD54A>></color> {line}\n" : $"    {line}\n");
            }
            m_sb.Append(m_scrollTop + VisibleRows < RowCount ? "<color=#888888>   more v</color>\n" : "\n");
            m_sb.Append("\n<size=20><color=#AAAAAA>Stick: move (left/right: page)   A / trigger: toggle   B / Y: close</color></size>");
            m_text.text = m_sb.ToString();
        }

        private void BuildUi()
        {
            // World-space canvas, 1 unit = 1 mm, 0.5 x 0.72 m
            var canvasGo = new GameObject("ClassFilterMenu", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            m_canvas = canvasGo.GetComponent<Canvas>();
            m_canvas.renderMode = RenderMode.WorldSpace;
            m_canvas.sortingOrder = 100;
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(500f, 720f);
            canvasRect.localScale = Vector3.one * 0.001f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasGo.transform, false);
            StretchToParent((RectTransform)background.transform, 0f);
            var image = background.GetComponent<Image>();
            image.color = new Color(0.06f, 0.08f, 0.1f, 0.88f);
            image.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            StretchToParent((RectTransform)textGo.transform, 24f);
            m_text = textGo.GetComponent<Text>();
            m_text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_text.fontSize = 28;
            m_text.lineSpacing = 1.05f;
            m_text.color = Color.white;
            m_text.supportRichText = true;
            m_text.alignment = TextAnchor.UpperLeft;
            m_text.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_text.verticalOverflow = VerticalWrapMode.Overflow;
            m_text.raycastTarget = false;
        }

        private static void StretchToParent(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        private void Load()
        {
            var saved = PlayerPrefs.GetString(PrefsKey, "");
            for (var i = 0; i < EnabledClasses.Length; i++)
            {
                // Everything is enabled until the user changes it
                EnabledClasses[i] = saved.Length != EnabledClasses.Length || saved[i] == '1';
            }
        }

        private void Save()
        {
            var chars = new char[EnabledClasses.Length];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = EnabledClasses[i] ? '1' : '0';
            }
            PlayerPrefs.SetString(PrefsKey, new string(chars));
            PlayerPrefs.Save();
        }
    }
}
