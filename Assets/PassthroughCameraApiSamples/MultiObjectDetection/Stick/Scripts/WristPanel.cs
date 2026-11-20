// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// The only control surface of New Vision (no controllers): turn the inside of the left wrist to your face and a
    /// cyberdeck panel appears above the forearm; poke its tiles with the right index fingertip.
    /// Tab 01 GENERAL: detection, model, classes, boxes, Gemini, QR, laptop gestures, Capri, theme, glitch FX, FIND
    /// (object memory: lead me back to something seen earlier).
    /// Tab 02 STICK: link state and every function of the M5StickC on the headset.
    /// Tab 03 SYS: live numbers of the headset (FPS, detection time, temperature, memory, GPU, Gemini latency).
    /// Shaking the hand glitches the panel (NvGlitch), switching pages decodes the text out of noise.
    /// </summary>
    public class WristPanel : MonoBehaviour
    {
        private const float Width = 600f;
        private const float Height = 600f;
        /// <summary>Meters per canvas unit: 600 units = 12 cm.</summary>
        private const float Scale = 0.0002f;
        private const float ShowFacing = 0.55f;
        private const float HideFacing = 0.3f;
        // Poke along the panel normal, meters (negative = in front of the panel, towards the user)
        private const float ArmDepth = -0.012f;
        private const float PressDepth = -0.002f;
        private const float HoverDepth = -0.05f;
        private const float BehindLimit = 0.03f;
        private const float ConfirmSeconds = 3f;
        private const float DecodeSeconds = 0.3f;
        private const float MessageSeconds = 2.5f;

        private const float Margin = 16f;
        private const float TabStickX = 182f;
        private const float TabSysX = 316f;
        private const float GridTop = 74f;
        private const float Gap = 12f;
        private const int ClassRows = 7;

        private static readonly byte[] s_brightLevels = { 8, 100, 255 };
        private static readonly string[] s_brightNames = { "LOW", "MED", "HIGH" };
        private static readonly byte[] s_volumeLevels = { 0, 90, 170, 255 };
        private static readonly string[] s_volumeNames = { "OFF", "LOW", "MED", "HIGH" };
        private static readonly string[] s_screens = { "home", "debug", "off" };

        private enum Page { General, Stick, Sys, Classes, Find }
        private enum Style { Tile, Row, Small, Tab }

        private class Button
        {
            public Page Page;
            public Style Style;
            public RectTransform Rect;
            public Image Outline;
            public Image Fill;
            public Text Caption;
            public Text Value;
            public Func<string> ValueText;
            public Func<bool> IsOn;       // value in accent (on) or muted (off)
            public Func<bool> IsActive;   // filled (current tab)
            public Action Press;
            public bool Danger;
            public float FlashUntil;
        }

        public static bool IsVisible { get; private set; }

        private StickClient m_stick;
        private HandTracker m_hands;
        private LaptopGestures m_gestures;
        private NewVisionControls m_nv;
        private Transform m_head;

        private RectTransform m_canvas;
        private RectTransform m_content;
        private CanvasGroup m_group;
        private NvGlitch m_glitch;
        private Image m_background;
        private Image m_outline;
        private RawImage m_scan;
        private Image m_headerLine;
        private Image m_headerMark;
        private Image m_linkDot;
        private readonly Image[] m_battery = new Image[5];
        private Text m_batteryText;
        private Text m_footer;
        private Image m_cursor;
        private readonly List<Button> m_buttons = new();
        // Stick tab status
        private RectTransform m_stickStatus;
        private Image m_questPill;
        private Text m_questPillText;
        private Image m_laptopPill;
        private Text m_laptopPillText;
        private Text m_stickLine;
        // Classes page
        private Text m_classesHeader;
        private readonly Button[] m_classButtons = new Button[ClassRows * 2];
        private int m_classPage;
        // Find page (object memory)
        private Text m_findHeader;
        private readonly Button[] m_findButtons = new Button[ClassRows * 2];
        private int m_findPage;
        private IReadOnlyList<ObjectMemory.Spot> m_findList = Array.Empty<ObjectMemory.Spot>();
        private float m_nextFindSort;
        // SYS page
        private float m_fps = 72f;
        private float m_fpsAccum;
        private int m_fpsFrames;
        private float m_batteryTemp = -1f;
        private float m_nextTempRead;

        private Page m_page = Page.General;
        private float m_pageChangedAt = -10f;
        private const float OpenSeconds = 0.22f;
        private const float CloseSeconds = 0.16f;
        private const float OpenDelay = 0.08f;
        private const float CloseDelay = 0.25f;
        private const float LostGraceSeconds = 0.4f;
        private float m_wantSince;
        private float m_lastTracked = -10f;

        // Pose filters: position in m/s terms (beta 20: at 0.5 m/s the cutoff is ~11 Hz), directions per rad/s
        private readonly OneEuro m_positionFilter = new(1.2f, 20f);
        private readonly OneEuro m_normalFilter = new(1.0f, 6f);
        private readonly OneEuro m_upFilter = new(1.0f, 6f);
        private bool m_placed;

        private bool m_shown;
        private bool m_wasShown;
        private float m_alpha;   // 0 closed .. 1 open
        private Image m_scanBeam;
        private bool m_armed;
        private string m_message;
        private float m_messageTime = -100f;
        private string m_confirm;
        private float m_confirmUntil;
        private bool m_wasConnected;

        // Hand motion for the glitch: smoothed velocity and acceleration of the wrist
        private Vector3 m_lastWrist;
        private Vector3 m_velocity;
        private Vector3 m_accel;
        private bool m_haveWrist;
        private Quaternion m_lastRotation;

        public void Init(StickClient stick, HandTracker hands, LaptopGestures gestures, NewVisionControls controls, Transform head)
        {
            m_stick = stick;
            m_hands = hands;
            m_gestures = gestures;
            m_nv = controls;
            m_head = head;
            NvStyle.EnsureTheme();
            m_stick.Reply += OnReply;
            m_hands.PosesUpdated += LatePlace;
            if (m_nv.Memory != null)
            {
                m_nv.Memory.Found += spot => ShowMessage("FOUND " + spot.Name.ToUpperInvariant());
            }
            Build();
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (m_head == null)
            {
                return;
            }
            SyncStick();
            MeasureSystem();

            var left = m_hands.Get(HandTracker.Left);
            bool want;
            if (left.Valid && !BrowserPanel.IsOpen)
            {
                m_lastTracked = Time.time;
                var toHead = (m_head.position - left.Palm).normalized;
                var facing = Vector3.Dot(left.PalmNormal, toHead);
                want = m_shown ? facing > HideFacing : facing > ShowFacing && left.HighConfidence;
            }
            else
            {
                // A hand lost for a moment (fast motion, the other hand in front) keeps the panel up
                want = m_shown && !BrowserPanel.IsOpen && Time.time - m_lastTracked < LostGraceSeconds;
            }
            // Debounce: the wish has to hold a moment, so a wrist right at the angle limit doesn't make it flicker
            if (want == m_shown)
            {
                m_wantSince = Time.time;
            }
            else if (Time.time - m_wantSince >= (want ? OpenDelay : CloseDelay))
            {
                m_shown = want;
                m_wantSince = Time.time;
            }

            if (m_shown != m_wasShown)
            {
                m_wasShown = m_shown;
                Debug.Log($"[NewVision] panel: {(m_shown ? "open" : "closed")} on {m_page}");
                if (m_shown)
                {
                    m_pageChangedAt = Time.time;   // opening decodes the text, like a screen waking up
                }
                m_glitch.Pulse(m_shown ? OpenSeconds + 0.1f : CloseSeconds + 0.05f, 0.9f);
            }

            // Old monitor switching on / off: a bright line opens up into the picture (or collapses into it), torn by the glitch
            m_alpha = Mathf.MoveTowards(m_alpha, m_shown ? 1f : 0f, Time.deltaTime / (m_shown ? OpenSeconds : CloseSeconds));
            var eased = 1f - (1f - m_alpha) * (1f - m_alpha) * (1f - m_alpha);
            m_content.localScale = new Vector3(Mathf.Lerp(1.06f, 1f, eased), Mathf.Max(0.015f, eased), 1f);
            m_group.alpha = Mathf.Clamp01(m_alpha * 3f);
            m_scanBeam.enabled = m_alpha < 0.999f;
            m_scanBeam.color = NvStyle.WithAlpha(NvStyle.Text, 1f - eased);
            IsVisible = m_alpha > 0.5f;
            m_canvas.gameObject.SetActive(m_alpha > 0f);
            if (m_alpha <= 0f)
            {
                m_armed = false;
                m_haveWrist = false;
                return;
            }

            if (left.Valid)
            {
                if (!m_placed)
                {
                    Place(left);   // the first frame of a new opening: no stale pose from last time
                }
                FeelMotion(left);
            }
            Refresh();
            Poke();
        }

        /// <summary>Placement right before rendering, from the freshest hand pose (see HandTracker.PosesUpdated).</summary>
        private void LatePlace()
        {
            if (m_canvas == null || !m_canvas.gameObject.activeSelf)
            {
                m_placed = false;
                return;
            }
            var left = m_hands.Get(HandTracker.Left);
            if (left.Valid)
            {
                Place(left);
            }
        }

        /// <summary>
        /// Fixed to the inner forearm like a wrist screen: lies in the plane of the palm, its top towards the fingers,
        /// a little above the skin. It moves and turns only with the hand (no turning to the head, no lag).
        /// </summary>
        private void Place(HandTracker.Hand hand)
        {
            var normal = hand.PalmNormal.normalized;
            var up = Vector3.ProjectOnPlane(hand.FingerDirection, normal);
            if (up.sqrMagnitude < 1e-6f)
            {
                return;
            }
            up.Normalize();
            var position = hand.Wrist - up * 0.085f + normal * 0.02f;

            // One-Euro filter: hand tracking jitters by a millimetre or two, which a rigid panel shows as shimmering
            // text. Held still it is smoothed hard; moving, the filter opens up and adds next to no lag.
            var dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
            if (!m_placed)
            {
                m_positionFilter.Reset(position);
                m_normalFilter.Reset(normal);
                m_upFilter.Reset(up);
                m_placed = true;
            }
            position = m_positionFilter.Filter(position, dt);
            normal = m_normalFilter.Filter(normal, dt).normalized;
            up = Vector3.ProjectOnPlane(m_upFilter.Filter(up, dt), normal).normalized;

            // The UI faces -forward: forward points into the arm, so the readable side faces out of the palm
            m_canvas.SetPositionAndRotation(position, Quaternion.LookRotation(-normal, up));
            m_canvas.localScale = Vector3.one * Scale;
        }

        /// <summary>One-Euro filter (Casiez et al.): cutoff frequency rises with speed — smooth when slow, quick when fast.</summary>
        private class OneEuro
        {
            private readonly float m_minCutoff;
            private readonly float m_beta;
            private const float DerivativeCutoff = 1f;
            private Vector3 m_value;
            private Vector3 m_speed;

            public OneEuro(float minCutoff, float beta)
            {
                m_minCutoff = minCutoff;
                m_beta = beta;
            }

            public void Reset(Vector3 value)
            {
                m_value = value;
                m_speed = Vector3.zero;
            }

            public Vector3 Filter(Vector3 value, float dt)
            {
                m_speed = Vector3.Lerp(m_speed, (value - m_value) / dt, Alpha(DerivativeCutoff, dt));
                var cutoff = m_minCutoff + m_beta * m_speed.magnitude;
                m_value = Vector3.Lerp(m_value, value, Alpha(cutoff, dt));
                return m_value;
            }

            private static float Alpha(float cutoff, float dt)
            {
                var tau = 1f / (2f * Mathf.PI * cutoff);
                return 1f / (1f + tau / dt);
            }
        }

        /// <summary>Wrist acceleration (filtered: hand tracking jitters) in the panel's right / up axes, for the glitch.</summary>
        private void FeelMotion(HandTracker.Hand hand)
        {
            var dt = Mathf.Max(1e-3f, Time.deltaTime);
            // Relative to the head: walking moves hand and head together and must not shake the panel
            var wrist = hand.Wrist - m_head.position;
            if (!m_haveWrist)
            {
                m_haveWrist = true;
                m_lastWrist = wrist;
                m_velocity = m_accel = Vector3.zero;
                m_lastRotation = m_canvas.rotation;
                return;
            }
            var velocity = (wrist - m_lastWrist) / dt;
            m_lastWrist = wrist;
            var newVelocity = Vector3.Lerp(m_velocity, velocity, 1f - Mathf.Exp(-20f * dt));
            var accel = (newVelocity - m_velocity) / dt;
            m_velocity = newVelocity;
            m_accel = Vector3.Lerp(m_accel, accel, 1f - Mathf.Exp(-25f * dt));

            var delta = m_canvas.rotation * Quaternion.Inverse(m_lastRotation);
            m_lastRotation = m_canvas.rotation;
            delta.ToAngleAxis(out var angle, out var axis);
            var twist = Vector3.Dot(axis, m_canvas.forward) * (angle > 180f ? angle - 360f : angle) / dt;

            // While a fingertip is at the panel the tiles must stay where they are: no shaking under the finger
            if (m_cursor.enabled)
            {
                m_glitch.Feel(Vector2.zero);
                return;
            }
            m_glitch.Feel(new Vector2(Vector3.Dot(m_accel, m_canvas.right), Vector3.Dot(m_accel, m_canvas.up)), twist);
        }

        // ------------------------------------------------------------------ state shown

        private void Refresh()
        {
            m_background.color = NvStyle.WithAlpha(NvStyle.Background, 0.9f);
            m_outline.color = NvStyle.Dim;
            m_scan.color = NvStyle.Scan;
            m_headerLine.color = NvStyle.Dim;
            m_headerMark.color = NvStyle.Accent;
            m_headerMark.rectTransform.anchoredPosition = new Vector2(m_page switch
            {
                Page.Stick => TabStickX,
                Page.Sys => TabSysX,
                _ => Margin
            }, -60f);

            var connected = m_stick.Connected;
            m_linkDot.color = !connected ? NvStyle.Red : m_stick.LaptopPaired ? NvStyle.Green : NvStyle.Yellow;
            // The battery of what the tab is about: the headset on GENERAL / CLASSES, the stick on STICK
            int battery;
            bool charging;
            if (m_page == Page.Stick)
            {
                battery = connected ? m_stick.IntField("bat", -1) : -1;
                charging = m_stick.Field("chg") == "1";
            }
            else
            {
                battery = SystemInfo.batteryLevel < 0f ? -1 : Mathf.RoundToInt(SystemInfo.batteryLevel * 100f);
                charging = SystemInfo.batteryStatus is BatteryStatus.Charging or BatteryStatus.Full;
            }
            var lit = battery < 0 ? 0 : (Mathf.Clamp(battery, 0, 100) + 19) / 20;
            var batteryColor = battery <= 15 ? NvStyle.Red : battery <= 35 ? NvStyle.Yellow : NvStyle.Accent;
            for (var i = 0; i < m_battery.Length; i++)
            {
                m_battery[i].color = i < lit ? batteryColor : NvStyle.Dim;
            }
            m_batteryText.text = battery < 0 ? "--" : (charging ? "+" : "") + battery + "%";
            m_batteryText.color = NvStyle.Text;

            if (m_confirm != null && Time.time > m_confirmUntil)
            {
                m_confirm = null;
            }

            var decode = Mathf.Clamp01((Time.time - m_pageChangedAt) / DecodeSeconds);
            foreach (var button in m_buttons)
            {
                var visible = button.Page == m_page || button.Style == Style.Tab;
                if (button.Rect.gameObject.activeSelf != visible)
                {
                    button.Rect.gameObject.SetActive(visible);
                }
                if (visible)
                {
                    PaintButton(button, decode);
                }
            }

            m_stickStatus.gameObject.SetActive(m_page == Page.Stick);
            if (m_page == Page.Stick)
            {
                PaintPill(m_questPill, m_questPillText, connected, connected ? "QUEST LINK" : m_stick.PortState.ToUpperInvariant(), decode);
                PaintPill(m_laptopPill, m_laptopPillText, m_stick.LaptopPaired, m_stick.LaptopPaired ? "LAPTOP" : "NO LAPTOP", decode);
                m_stickLine.text = connected
                    ? $"PING {m_stick.PingMs}MS  //  UP {m_stick.IntField("up") / 60}M  //  HEAP {m_stick.IntField("heap") / 1024}K"
                    : "NEW VISION STICK  //  BLE";
                m_stickLine.color = NvStyle.Muted;
            }

            m_classesHeader.gameObject.SetActive(m_page == Page.Classes);
            if (m_page == Page.Classes && m_nv.Classes != null)
            {
                var pages = ClassPages;
                m_classesHeader.text = NvStyle.Decode(
                    $"CLASSES  {m_nv.Classes.EnabledCount}/{m_nv.Classes.Labels.Length}  //  PAGE {m_classPage + 1}/{pages}", decode);
                m_classesHeader.color = NvStyle.Accent;
            }

            m_findHeader.gameObject.SetActive(m_page == Page.Find);
            if (m_page == Page.Find && m_nv.Memory != null)
            {
                // Re-sorted twice a second: ages and distances change, rows shouldn't jump every frame
                if (Time.time >= m_nextFindSort)
                {
                    m_nextFindSort = Time.time + 0.5f;
                    m_findList = new List<ObjectMemory.Spot>(m_nv.Memory.Sorted());
                    m_findPage = Mathf.Min(m_findPage, FindPages - 1);
                }
                var target = m_nv.Memory.Target;
                m_findHeader.text = NvStyle.Decode(target != null
                    ? $"FINDING {target.Name.ToUpperInvariant()}  //  {m_nv.Memory.DistanceTo(target):0.0}M"
                    : $"MEMORY  {m_findList.Count}  //  PAGE {m_findPage + 1}/{FindPages}", decode);
                m_findHeader.color = NvStyle.Accent;
            }

            var showMessage = Time.time - m_messageTime < MessageSeconds;
            m_footer.text = showMessage ? m_message : FooterText();
            m_footer.color = showMessage ? NvStyle.Text : NvStyle.Muted;
        }

        private string FooterText() => m_page switch
        {
            Page.Stick => m_stick.Connected ? $"SCREEN {m_stick.Field("scr")?.ToUpperInvariant()}  //  THEME {NvStyle.Themes[NvStyle.ThemeIndex].Name}" : "",
            Page.Sys => $"UP {Time.realtimeSinceStartup / 60f:0}M  //  MODEL {m_nv.ModelNames[m_nv.GetModel()]}  //  {SystemInfo.systemMemorySize / 1024}GB",
            Page.Find => m_nv.Memory != null && m_nv.Memory.Target != null ? "TAP AGAIN OR STOP TO END" : "TAP AN OBJECT TO FIND IT",
            _ => m_nv.GetDetecting != null && m_nv.GetDetecting() ? "DETECTION ONLINE" : "DETECTION PAUSED"
        };

        private void PaintButton(Button b, float decode)
        {
            var flashing = Time.time < b.FlashUntil;
            var active = b.IsActive != null && b.IsActive();
            var filled = flashing || active;
            var on = b.IsOn == null || b.IsOn();
            var accent = b.Danger ? NvStyle.Red : NvStyle.Accent;
            b.Fill.enabled = filled;
            b.Fill.color = accent;
            b.Outline.color = b == m_hovered ? accent : b.Danger ? NvStyle.WithAlpha(NvStyle.Red, 0.45f) : NvStyle.Dim;
            if (b.Caption != null)
            {
                b.Caption.color = filled ? Color.black : NvStyle.Muted;
            }
            var value = b.ValueText();
            // Only the page that just opened decodes; values that change later just change
            b.Value.text = decode < 1f ? NvStyle.Decode(value, decode) : value;
            b.Value.color = filled ? Color.black : on ? (b.Style == Style.Tab ? NvStyle.Text : accent) : NvStyle.Muted;
        }

        private static void PaintPill(Image pill, Text text, bool good, string label, float decode)
        {
            var color = good ? NvStyle.Green : NvStyle.Yellow;
            pill.color = color;
            text.color = color;
            text.text = NvStyle.Decode(label, decode);
        }

        private Button m_hovered;

        // ------------------------------------------------------------------ poke

        private void Poke()
        {
            var right = m_hands.Get(HandTracker.Right);
            if (!right.Valid)
            {
                m_cursor.enabled = false;
                m_armed = false;
                m_hovered = null;
                return;
            }
            var local = m_canvas.InverseTransformPoint(right.IndexTip);
            var depth = local.z * Scale;
            var point = new Vector2(local.x, local.y);
            var onPanel = Mathf.Abs(point.x) <= Width * 0.5f && Mathf.Abs(point.y) <= Height * 0.5f;
            if (!onPanel || depth < HoverDepth || depth > BehindLimit)
            {
                m_cursor.enabled = false;
                m_armed = depth < ArmDepth || !onPanel;
                m_hovered = null;
                return;
            }

            m_cursor.enabled = true;
            m_cursor.rectTransform.anchoredPosition = point;
            var closeness = Mathf.InverseLerp(HoverDepth, PressDepth, depth);
            m_cursor.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(40f, 14f, closeness);
            m_cursor.color = NvStyle.WithAlpha(NvStyle.Accent, Mathf.Lerp(0.35f, 1f, closeness));

            m_hovered = ButtonAt(right.IndexTip);
            if (depth < ArmDepth)
            {
                m_armed = true;
            }
            else if (m_armed && depth > PressDepth && m_hovered != null)
            {
                m_armed = false;
                m_hovered.FlashUntil = Time.time + 0.18f;
                Debug.Log($"[NewVision] panel: {m_page} > {m_hovered.Caption?.text ?? m_hovered.ValueText()} ({m_hovered.ValueText()})");
                m_hovered.Press();
            }
        }

        private Button ButtonAt(Vector3 world)
        {
            foreach (var button in m_buttons)
            {
                if (!button.Rect.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Vector2 local = button.Rect.InverseTransformPoint(world);
                if (button.Rect.rect.Contains(local))
                {
                    return button;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ actions

        private void ShowPage(Page page)
        {
            if (m_page == page)
            {
                return;
            }
            m_page = page;
            m_pageChangedAt = Time.time;
            m_glitch.Pulse();
            m_confirm = null;
        }

        private void ShowMessage(string text)
        {
            m_message = text;
            m_messageTime = Time.time;
        }

        private void OnReply(string reply)
        {
            if (reply.StartsWith("ERR notpaired"))
            {
                ShowMessage("LAPTOP NOT PAIRED");
            }
            else if (reply.StartsWith("ERR"))
            {
                ShowMessage(reply.ToUpperInvariant());
            }
        }

        private void SendStick(string line, string feedback)
        {
            if (!m_stick.Connected)
            {
                ShowMessage("NO STICK");
                return;
            }
            _ = m_stick.Send(line);
            if (feedback != null)
            {
                ShowMessage(feedback);
            }
        }

        /// <summary>Dangerous actions need a second press within a few seconds.</summary>
        private void Confirmed(string id, Action action)
        {
            if (m_confirm == id && Time.time <= m_confirmUntil)
            {
                m_confirm = null;
                action();
            }
            else
            {
                m_confirm = id;
                m_confirmUntil = Time.time + ConfirmSeconds;
                ShowMessage("PRESS AGAIN TO CONFIRM");
            }
        }

        /// <summary>
        /// The stick follows the panel's theme and glitch level and gets the time for its clock:
        /// sent when the link comes up and whenever they change.
        /// </summary>
        private void SyncStick()
        {
            var connected = m_stick.Connected;
            if (connected && !m_wasConnected)
            {
                SendLook();   // the time is sent by StickClient whenever the stick reports clk=0
            }
            m_wasConnected = connected;
        }

        private void SendLook()
        {
            _ = m_stick.Send($"SET theme {NvStyle.ThemeIndex}");
            _ = m_stick.Send($"SET fx {NvStyle.FxLevel}");
        }

        private static int Nearest(byte[] levels, int value)
        {
            var best = 0;
            for (var i = 1; i < levels.Length; i++)
            {
                if (Mathf.Abs(levels[i] - value) < Mathf.Abs(levels[best] - value))
                {
                    best = i;
                }
            }
            return best;
        }

        private int ClassPages => m_nv.Classes == null ? 1 : Mathf.Max(1, Mathf.CeilToInt(m_nv.Classes.Labels.Length / (float)m_classButtons.Length));

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var go = new GameObject("WristPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 98;
            m_canvas = (RectTransform)go.transform;
            m_canvas.sizeDelta = new Vector2(Width, Height);
            m_canvas.localScale = Vector3.one * Scale;
            m_group = go.GetComponent<CanvasGroup>();
            m_group.interactable = false;
            m_group.blocksRaycasts = false;

            // Everything that shakes and tears lives in the content; the cursor and the glitch overlay stay on top
            m_content = NvStyle.Rect("Content", m_canvas);
            m_content.sizeDelta = new Vector2(Width, Height);

            m_background = NvStyle.ChamferImage("Background", m_content, 26f, NvStyle.Background);
            NvStyle.Stretch(m_background.rectTransform);
            m_scan = NvStyle.ScanlineOverlay(m_content, 4f, Height);
            m_outline = NvStyle.ChamferImage("Outline", m_content, 26f, NvStyle.Dim, false, 2f);
            NvStyle.Stretch(m_outline.rectTransform);

            BuildHeader();
            BuildGeneral();
            BuildStick();
            BuildSys();
            BuildClasses();
            BuildFind();

            m_footer = NvStyle.Label("Footer", m_content, 18, NvStyle.Muted);
            NvStyle.Place(m_footer.rectTransform, Margin + 4f, Height - 52f, Width - Margin * 2f, 36f);

            m_cursor = NvStyle.Solid("Cursor", m_canvas, NvStyle.Accent);
            m_cursor.rectTransform.anchorMin = m_cursor.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_cursor.enabled = false;

            m_glitch = go.AddComponent<NvGlitch>();
            m_glitch.Init(m_content, new Vector2(Width, Height), m_canvas);
            m_cursor.transform.SetAsLastSibling();

            // The bright line the picture opens from and collapses into
            m_scanBeam = NvStyle.Solid("Beam", m_canvas, Color.white);
            m_scanBeam.rectTransform.anchorMin = m_scanBeam.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_scanBeam.rectTransform.sizeDelta = new Vector2(Width * 1.04f, 5f);
            m_scanBeam.enabled = false;

            go.SetActive(false);
        }

        private void BuildHeader()
        {
            AddButton(Page.General, Style.Tab, Margin, 14f, TabStickX - Margin - 10f, 40f, null, () => "01 GENERAL",
                () => ShowPage(Page.General), active: () => m_page is Page.General or Page.Classes or Page.Find);
            AddButton(Page.Stick, Style.Tab, TabStickX, 14f, TabSysX - TabStickX - 10f, 40f, null, () => "02 STICK",
                () => ShowPage(Page.Stick), active: () => m_page == Page.Stick);
            AddButton(Page.Sys, Style.Tab, TabSysX, 14f, 104f, 40f, null, () => "03 SYS",
                () => ShowPage(Page.Sys), active: () => m_page == Page.Sys);

            m_linkDot = NvStyle.Solid("Link", m_content, NvStyle.Red);
            NvStyle.Place(m_linkDot.rectTransform, TabSysX + 116f, 27f, 12f, 12f);
            for (var i = 0; i < m_battery.Length; i++)
            {
                m_battery[i] = NvStyle.Solid("Battery", m_content, NvStyle.Dim);
                NvStyle.Place(m_battery[i].rectTransform, Width - Margin - 5 * 10f + i * 10f, 24f, 7f, 20f);
            }
            m_batteryText = NvStyle.Label("BatteryText", m_content, 18, NvStyle.Text, TextAnchor.MiddleRight);
            NvStyle.Place(m_batteryText.rectTransform, Width - Margin - 5 * 10f - 68f, 14f, 64f, 40f);

            m_headerLine = NvStyle.Solid("HeaderLine", m_content, NvStyle.Dim);
            NvStyle.Place(m_headerLine.rectTransform, 0f, 60f, Width, 2f);
            m_headerMark = NvStyle.Solid("HeaderMark", m_content, NvStyle.Accent);
            NvStyle.Place(m_headerMark.rectTransform, Margin, 60f, 120f, 2f);
        }

        private static float TileWidth => (Width - Margin * 2f - Gap * 2f) / 3f;

        private void AddTile(Page page, int column, int row, float top, float height, string caption, Func<string> value,
            Action press, Func<bool> on = null, bool danger = false)
        {
            AddButton(page, Style.Tile, Margin + column * (TileWidth + Gap), top + row * (height + Gap), TileWidth, height,
                caption, value, press, on, null, danger);
        }

        private void BuildGeneral()
        {
            const float h = 78f;
            var g = Page.General;
            AddTile(g, 0, 0, GridTop, h, "DETECT", () => m_nv.GetDetecting() ? "ON" : "OFF", () =>
            {
                var on = !m_nv.GetDetecting();
                m_nv.SetDetecting(on);
                ShowMessage(on ? "DETECTION ONLINE" : "DETECTION PAUSED");
            }, () => m_nv.GetDetecting());
            AddTile(g, 1, 0, GridTop, h, "MODEL", () => m_nv.ModelNames[m_nv.GetModel()], () =>
            {
                m_nv.SetModel(m_nv.GetModel() + 1);
                ShowMessage("MODEL " + m_nv.ModelNames[m_nv.GetModel()]);
            });
            AddTile(g, 2, 0, GridTop, h, "CLASSES", () => $"{m_nv.Classes.EnabledCount}/{m_nv.Classes.Labels.Length}",
                () => ShowPage(Page.Classes));

            AddTile(g, 0, 1, GridTop, h, "BOXES", () => m_nv.GetMaxBoxes().ToString(),
                () => m_nv.SetMaxBoxes(m_nv.GetMaxBoxes() % 6 + 1));
            AddTile(g, 1, 1, GridTop, h, "ASK", () => ObjectInfoLookup.DwellEnabled ? "GAZE" : "PINCH",
                () => ObjectInfoLookup.DwellEnabled = !ObjectInfoLookup.DwellEnabled, () => ObjectInfoLookup.DwellEnabled);
            AddTile(g, 2, 1, GridTop, h, "QR", () => ObjectInfoLookup.QrEnabled ? "ON" : "OFF",
                () => ObjectInfoLookup.QrEnabled = !ObjectInfoLookup.QrEnabled, () => ObjectInfoLookup.QrEnabled);

            AddTile(g, 0, 2, GridTop, h, "LAPTOP", () => m_gestures.Enabled ? "ON" : "OFF",
                () => m_gestures.Enabled = !m_gestures.Enabled, () => m_gestures.Enabled);
            AddTile(g, 1, 2, GridTop, h, "CAPRI", () => CapriState(), () =>
            {
                var capri = m_nv.Companion;
                if (capri == null || !capri.Available)
                {
                    ShowMessage("CAPRI NOT IN THIS BUILD");
                }
                else if (capri.Visible)
                {
                    capri.Hide();
                }
                else
                {
                    capri.Recall();
                }
            }, () => m_nv.Companion != null && m_nv.Companion.Visible);
            AddTile(g, 2, 2, GridTop, h, "CAPRI", () => "SIT", () =>
            {
                if (m_nv.Companion != null && m_nv.Companion.Available)
                {
                    m_nv.Companion.Sit();
                }
            }, () => m_nv.Companion != null && m_nv.Companion.Available);

            AddTile(g, 0, 3, GridTop, h, "THEME", () => NvStyle.Themes[NvStyle.ThemeIndex].Name, () =>
            {
                NvStyle.ThemeIndex++;
                m_glitch.Pulse(0.3f);
                if (m_stick.Connected)
                {
                    SendLook();
                }
            });
            AddTile(g, 1, 3, GridTop, h, "FX", () => NvStyle.FxNames[NvStyle.FxLevel], () =>
            {
                NvStyle.FxLevel = (NvStyle.FxLevel + 1) % 3;
                m_glitch.Pulse(0.3f);
                if (m_stick.Connected)
                {
                    SendLook();
                }
            }, () => NvStyle.FxLevel > 0);
            AddTile(g, 2, 3, GridTop, h, "FIND", () =>
            {
                var memory = m_nv.Memory;
                return memory == null ? "--" : memory.Target != null ? memory.Target.Name.ToUpperInvariant() : memory.Count.ToString();
            }, () =>
            {
                m_findPage = 0;
                m_nextFindSort = 0f;
                ShowPage(Page.Find);
            }, () => m_nv.Memory != null && m_nv.Memory.Count > 0);

            AddTile(g, 0, 4, GridTop, h, "SUBS", SubsState, () =>
            {
                if (m_nv.Speech == null)
                {
                    return;
                }
                m_nv.Speech.Enabled = !m_nv.Speech.Enabled;
                ShowMessage(m_nv.Speech.Enabled ? "SUBTITLES ON // RU" : "SUBTITLES OFF");
            }, () => m_nv.Speech != null && m_nv.Speech.Enabled);
        }

        private string SubsState()
        {
            if (m_nv.Speech == null || !m_nv.Speech.Enabled)
            {
                return "OFF";
            }
            return m_nv.Speech.Current switch
            {
                SpeechRecognizer.State.Listening => "RU",
                SpeechRecognizer.State.Installing or SpeechRecognizer.State.Loading => "LOAD",
                SpeechRecognizer.State.NoPermission => "NO MIC",
                SpeechRecognizer.State.NoModel => "NO MODEL",
                SpeechRecognizer.State.Error => "ERROR",
                _ => "..."
            };
        }

        private string CapriState()
        {
            var capri = m_nv.Companion;
            return capri == null || !capri.Available ? "N/A" : capri.Visible ? "ON" : "OFF";
        }

        private void BuildStick()
        {
            // Status: two pills (headset link, laptop) and a muted line, Vartovyi's ALL CLEAR style
            m_stickStatus = NvStyle.Rect("StickStatus", m_content);
            NvStyle.Place(m_stickStatus, 0f, 0f, Width, Height);
            var pillWidth = (Width - Margin * 2f - Gap) / 2f;
            (m_questPill, m_questPillText) = Pill("Quest", Margin, pillWidth);
            (m_laptopPill, m_laptopPillText) = Pill("Laptop", Margin + pillWidth + Gap, pillWidth);
            m_stickLine = NvStyle.Label("StickLine", m_stickStatus, 18, NvStyle.Muted);
            NvStyle.Place(m_stickLine.rectTransform, Margin + 4f, GridTop + 50f, Width - Margin * 2f, 30f);

            const float top = GridTop + 92f;
            const float h = 74f;
            var s = Page.Stick;
            AddTile(s, 0, 0, top, h, "PLAY", () => "PAUSE", () => SendStick("CMD play", "PLAY/PAUSE -> LAPTOP"));
            AddTile(s, 1, 0, top, h, "MUTE", () => "SOUND", () => SendStick("CMD mute", "MUTE -> LAPTOP"));
            AddTile(s, 2, 0, top, h, "BEEP", () => "TEST", () => SendStick("BEEP", null));

            AddTile(s, 0, 1, top, h, "SCREEN", () => (m_stick.Field("scr") ?? "--").ToUpperInvariant(), () =>
            {
                var index = Array.IndexOf(s_screens, m_stick.Field("scr"));
                var next = s_screens[(index + 1) % s_screens.Length];
                SendStick($"SET scr {next}", null);
                m_stick.SetField("scr", next);
            }, () => m_stick.Field("scr") != "off");
            AddTile(s, 1, 1, top, h, "BRIGHT", () => m_stick.Connected ? s_brightNames[Nearest(s_brightLevels, m_stick.IntField("bri", 100))] : "--", () =>
            {
                var next = s_brightLevels[(Nearest(s_brightLevels, m_stick.IntField("bri", 100)) + 1) % s_brightLevels.Length];
                SendStick($"SET bri {next}", null);
                m_stick.SetField("bri", next.ToString());
            });
            AddTile(s, 2, 1, top, h, "VOLUME", () => m_stick.Connected ? s_volumeNames[Nearest(s_volumeLevels, m_stick.IntField("vol", 90))] : "--", () =>
            {
                var next = s_volumeLevels[(Nearest(s_volumeLevels, m_stick.IntField("vol", 90)) + 1) % s_volumeLevels.Length];
                SendStick($"SET vol {next}", null);
                m_stick.SetField("vol", next.ToString());
            }, () => m_stick.IntField("vol", 90) > 0);

            AddTile(s, 0, 2, top, h, "LINK", () => "RESET", () =>
            {
                m_stick.Reconnect();
                ShowMessage("RECONNECTING");
            });
            AddTile(s, 1, 2, top, h, "REBOOT", () => m_confirm == "reboot" ? "SURE?" : "RUN",
                () => Confirmed("reboot", () => SendStick("REBOOT", "STICK REBOOTING")), danger: true);
            AddTile(s, 2, 2, top, h, "POWER", () => m_confirm == "off" ? "SURE?" : "OFF",
                () => Confirmed("off", () => SendStick("OFF", "STICK OFF // SIDE BUTTON TO WAKE")), danger: true);

            AddTile(s, 0, 3, top, h, "FLIP", () => m_stick.Connected ? (m_stick.Field("rot") == "1" ? "180°" : "0°") : "--", () =>
            {
                var next = m_stick.Field("rot") == "1" ? "0" : "1";
                SendStick($"SET rot {next}", next == "1" ? "SCREEN TURNED 180°" : "SCREEN UPRIGHT");
                m_stick.SetField("rot", next);
            }, () => m_stick.Field("rot") == "1");
        }

        private (Image, Text) Pill(string name, float x, float width)
        {
            var pill = NvStyle.ChamferImage(name, m_stickStatus, 10f, NvStyle.Yellow, false, 2f);
            NvStyle.Place(pill.rectTransform, x, GridTop, width, 40f);
            var square = NvStyle.Solid("Square", pill.transform, NvStyle.Yellow);
            NvStyle.Place(square.rectTransform, 12f, 13f, 14f, 14f);
            var text = NvStyle.Label("Text", pill.transform, 20, NvStyle.Yellow);
            NvStyle.Place(text.rectTransform, 36f, 0f, width - 44f, 40f);
            // The square follows the pill colour
            pill.gameObject.AddComponent<PillSquare>().Init(pill, square);
            return (pill, text);
        }

        /// <summary>Keeps the pill's little square in the pill's colour.</summary>
        private class PillSquare : MonoBehaviour
        {
            private Image m_pill;
            private Image m_square;

            public void Init(Image pill, Image square)
            {
                m_pill = pill;
                m_square = square;
            }

            private void LateUpdate() => m_square.color = m_pill.color;
        }

        private void BuildClasses()
        {
            m_classesHeader = NvStyle.Label("ClassesHeader", m_content, 20, NvStyle.Accent);
            NvStyle.Place(m_classesHeader.rectTransform, Margin + 4f, GridTop - 2f, Width - Margin * 2f, 30f);

            const float top = GridTop + 34f;
            const float rowHeight = 44f;
            const float rowGap = 6f;
            var columnWidth = (Width - Margin * 2f - Gap) / 2f;
            for (var i = 0; i < m_classButtons.Length; i++)
            {
                var slot = i;
                var column = i / ClassRows;
                var row = i % ClassRows;
                m_classButtons[i] = AddButton(Page.Classes, Style.Row, Margin + column * (columnWidth + Gap), top + row * (rowHeight + rowGap),
                    columnWidth, rowHeight, null, () => ClassText(slot), () => ToggleClass(slot), () => ClassOn(slot));
            }

            // Bottom bar: page back / forward, presets, back to GENERAL
            var barTop = top + ClassRows * (rowHeight + rowGap) + 4f;
            var labels = new[] { "<", ">", "ALL", "NONE", "HOME", "BACK" };
            var actions = new Action[]
            {
                () => { m_classPage = (m_classPage + ClassPages - 1) % ClassPages; m_pageChangedAt = Time.time; },
                () => { m_classPage = (m_classPage + 1) % ClassPages; m_pageChangedAt = Time.time; },
                () => m_nv.Classes.SetAll(true),
                () => m_nv.Classes.SetAll(false),
                () => m_nv.Classes.HomePreset(),
                () => ShowPage(Page.General)
            };
            var width = (Width - Margin * 2f - Gap * (labels.Length - 1)) / labels.Length;
            for (var i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                _ = AddButton(Page.Classes, Style.Small, Margin + i * (width + Gap), barTop, width, 46f, null, () => label, actions[i]);
            }
        }

        // ------------------------------------------------------------------ SYS

        /// <summary>Live numbers of the headset; the tiles are read-only (MEMORY opens FIND).</summary>
        private void BuildSys()
        {
            const float h = 100f;
            var s = Page.Sys;
            AddTile(s, 0, 0, GridTop, h, "FPS", () => $"{m_fps:0}", Noop, () => m_fps >= 65f);
            AddTile(s, 1, 0, GridTop, h, "YOLO", () => SentisInferenceRunManager.LastInferenceMs > 0f
                ? $"{SentisInferenceRunManager.LastInferenceMs:0}MS" : "--", Noop,
                () => SentisInferenceRunManager.LastInferenceMs is > 0f and < 400f);
            AddTile(s, 2, 0, GridTop, h, "LAYERS", () => SentisInferenceRunManager.LastLayersPerFrame > 0
                ? $"{SentisInferenceRunManager.LastLayersPerFrame}/F" : "--", Noop);

            AddTile(s, 0, 1, GridTop, h, "OBJECTS", () => m_nv.GetDetecting() ? ConfirmedObjects().ToString() : "OFF", Noop,
                () => m_nv.GetDetecting());
            AddTile(s, 1, 1, GridTop, h, "MEMORY", () => m_nv.Memory != null ? m_nv.Memory.Count.ToString() : "--", () =>
            {
                m_findPage = 0;
                m_nextFindSort = 0f;
                ShowPage(Page.Find);
            });
            AddTile(s, 2, 1, GridTop, h, "ASK", () => ObjectInfoLookup.LastAnswerSeconds > 0f
                ? $"{ObjectInfoLookup.LastAnswerSeconds:0.0}S" : "--", Noop);

            AddTile(s, 0, 2, GridTop, h, "TEMP", () => m_batteryTemp > 0f ? $"{m_batteryTemp:0}°C" : "--", Noop,
                () => m_batteryTemp is > 0f and < 42f);
            AddTile(s, 1, 2, GridTop, h, "RAM", () =>
            {
                var used = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                return used > 0 ? $"{used / (1024 * 1024)}MB" : "--";
            }, Noop);
            AddTile(s, 2, 2, GridTop, h, "GPU", () => OVRPlugin.gpuUtilSupported ? $"{OVRPlugin.gpuUtilLevel * 100f:0}%" : "--", Noop,
                () => !OVRPlugin.gpuUtilSupported || OVRPlugin.gpuUtilLevel < 0.9f);

            AddTile(s, 0, 3, GridTop, h, "BATTERY", () => SystemInfo.batteryLevel < 0f ? "--" : $"{SystemInfo.batteryLevel * 100f:0}%", Noop,
                () => SystemInfo.batteryLevel is < 0f or > 0.2f);
            AddTile(s, 1, 3, GridTop, h, "STICK", () => m_stick.Connected ? $"{m_stick.PingMs}MS" : "OFF", () => ShowPage(Page.Stick),
                () => m_stick.Connected);
            AddTile(s, 2, 3, GridTop, h, "LAPTOP", () => m_stick.LaptopPaired ? "LINK" : "--", Noop, () => m_stick.LaptopPaired);
        }

        private static void Noop() { }

        private int ConfirmedObjects() => m_nv.Memory != null ? m_nv.Memory.InView : 0;

        /// <summary>FPS over the last half second, and the battery temperature from Android every 5 s.</summary>
        private void MeasureSystem()
        {
            m_fpsAccum += Time.unscaledDeltaTime;
            m_fpsFrames++;
            if (m_fpsAccum >= 0.5f)
            {
                m_fps = m_fpsFrames / m_fpsAccum;
                m_fpsAccum = 0f;
                m_fpsFrames = 0;
            }
            if (Time.time >= m_nextTempRead)
            {
                m_nextTempRead = Time.time + 5f;
                m_batteryTemp = ReadBatteryTemperature();
            }
        }

        // The sticky ACTION_BATTERY_CHANGED intent carries the temperature in tenths of a degree.
        // registerReceiver(null, filter) needs an explicit JNI signature: Unity can't type a null argument.
        private static float ReadBatteryTemperature()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var filter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED");
                var contextClass = AndroidJNI.FindClass("android/content/Context");
                var method = AndroidJNI.GetMethodID(contextClass, "registerReceiver",
                    "(Landroid/content/BroadcastReceiver;Landroid/content/IntentFilter;)Landroid/content/Intent;");
                AndroidJNI.DeleteLocalRef(contextClass);
                var intentPtr = AndroidJNI.CallObjectMethod(activity.GetRawObject(), method,
                    new[] { new jvalue { l = IntPtr.Zero }, new jvalue { l = filter.GetRawObject() } });
                if (intentPtr == IntPtr.Zero)
                {
                    return -1f;
                }
                using var intent = new AndroidJavaObject(intentPtr);
                AndroidJNI.DeleteLocalRef(intentPtr);
                var tenths = intent.Call<int>("getIntExtra", "temperature", -10);
                return tenths / 10f;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] battery temperature: {e.Message}");
                return -1f;
            }
#else
            return -1f;
#endif
        }

        // ------------------------------------------------------------------ FIND (object memory)

        private int FindPages => Mathf.Max(1, Mathf.CeilToInt(m_findList.Count / (float)m_findButtons.Length));

        private void BuildFind()
        {
            m_findHeader = NvStyle.Label("FindHeader", m_content, 20, NvStyle.Accent);
            NvStyle.Place(m_findHeader.rectTransform, Margin + 4f, GridTop - 2f, Width - Margin * 2f, 30f);

            const float top = GridTop + 34f;
            const float rowHeight = 44f;
            const float rowGap = 6f;
            var columnWidth = (Width - Margin * 2f - Gap) / 2f;
            for (var i = 0; i < m_findButtons.Length; i++)
            {
                var slot = i;
                var column = i / ClassRows;
                var row = i % ClassRows;
                m_findButtons[i] = AddButton(Page.Find, Style.Row, Margin + column * (columnWidth + Gap), top + row * (rowHeight + rowGap),
                    columnWidth, rowHeight, null, () => FindText(slot), () => PickFind(slot), () => FindSpot(slot) != null,
                    () => FindSpot(slot) != null && FindSpot(slot) == m_nv.Memory?.Target);
            }

            var barTop = top + ClassRows * (rowHeight + rowGap) + 4f;
            var labels = new[] { "<", ">", "STOP", "CLEAR", "BACK" };
            var actions = new Action[]
            {
                () => { m_findPage = (m_findPage + FindPages - 1) % FindPages; m_pageChangedAt = Time.time; },
                () => { m_findPage = (m_findPage + 1) % FindPages; m_pageChangedAt = Time.time; },
                () => m_nv.Memory?.StopFind(),
                () => Confirmed("clear", () =>
                {
                    m_nv.Memory?.Clear();
                    m_findList = Array.Empty<ObjectMemory.Spot>();
                    ShowMessage("MEMORY CLEARED");
                }),
                () => ShowPage(Page.General)
            };
            var width = (Width - Margin * 2f - Gap * (labels.Length - 1)) / labels.Length;
            for (var i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                var index = i;
                _ = AddButton(Page.Find, Style.Small, Margin + i * (width + Gap), barTop, width, 46f, null,
                    () => index == 3 && m_confirm == "clear" ? "SURE?" : label, actions[i], danger: index == 3);
            }
        }

        private ObjectMemory.Spot FindSpot(int slot)
        {
            var index = m_findPage * m_findButtons.Length + slot;
            return index < m_findList.Count ? m_findList[index] : null;
        }

        private string FindText(int slot)
        {
            var spot = FindSpot(slot);
            return spot == null ? "" : $"{spot.Name.ToUpperInvariant()}  {ObjectMemory.Age(spot)}  {m_nv.Memory.DistanceTo(spot):0.0}M";
        }

        private void PickFind(int slot)
        {
            var spot = FindSpot(slot);
            if (spot == null || m_nv.Memory == null)
            {
                return;
            }
            if (m_nv.Memory.Target == spot)
            {
                m_nv.Memory.StopFind();
                ShowMessage("SEARCH ENDED");
                return;
            }
            m_nv.Memory.StartFind(spot);
            ShowMessage("FINDING " + spot.Name.ToUpperInvariant());
        }

        private int ClassIndex(int slot) => m_classPage * m_classButtons.Length + slot;

        private string ClassText(int slot)
        {
            var index = ClassIndex(slot);
            if (m_nv.Classes == null || index >= m_nv.Classes.Labels.Length)
            {
                return "";
            }
            return (m_nv.Classes.EnabledClasses[index] ? "[X] " : "[ ] ") + m_nv.Classes.Labels[index].ToUpperInvariant();
        }

        private bool ClassOn(int slot)
        {
            var index = ClassIndex(slot);
            return m_nv.Classes != null && index < m_nv.Classes.Labels.Length && m_nv.Classes.EnabledClasses[index];
        }

        private void ToggleClass(int slot)
        {
            var index = ClassIndex(slot);
            if (m_nv.Classes != null && index < m_nv.Classes.Labels.Length)
            {
                m_nv.Classes.Toggle(index);
            }
        }

        private Button AddButton(Page page, Style style, float x, float y, float width, float height, string caption,
            Func<string> value, Action press, Func<bool> on = null, Func<bool> active = null, bool danger = false)
        {
            var rect = NvStyle.Rect(caption ?? "Button", m_content);
            NvStyle.Place(rect, x, y, width, height);
            var cut = style == Style.Tile ? 14f : 8f;
            var fill = NvStyle.ChamferImage("Fill", rect, cut, NvStyle.Accent);
            NvStyle.Stretch(fill.rectTransform);
            var outline = NvStyle.ChamferImage("Outline", rect, cut, NvStyle.Dim, false, 2f);
            NvStyle.Stretch(outline.rectTransform);

            Text captionText = null;
            Text valueText;
            switch (style)
            {
                case Style.Tile:
                    captionText = NvStyle.Label("Caption", rect, 17, NvStyle.Muted, TextAnchor.UpperLeft);
                    captionText.text = caption;
                    NvStyle.Place(captionText.rectTransform, 14f, 10f, width - 20f, 24f);
                    valueText = NvStyle.Label("Value", rect, 30, NvStyle.Accent, TextAnchor.LowerLeft);
                    NvStyle.Place(valueText.rectTransform, 14f, height - 52f, width - 20f, 42f);
                    break;
                case Style.Row:
                    valueText = NvStyle.Label("Value", rect, 18, NvStyle.Accent);
                    NvStyle.Place(valueText.rectTransform, 12f, 0f, width - 16f, height);
                    break;
                default:
                    valueText = NvStyle.Label("Value", rect, style == Style.Tab ? 22 : 20, NvStyle.Text, TextAnchor.MiddleCenter);
                    NvStyle.Stretch(valueText.rectTransform);
                    break;
            }
            // Long names stay inside their tile
            valueText.horizontalOverflow = HorizontalWrapMode.Wrap;
            valueText.resizeTextForBestFit = true;
            valueText.resizeTextMinSize = 12;
            valueText.resizeTextMaxSize = valueText.fontSize;

            var button = new Button
            {
                Page = page,
                Style = style,
                Rect = rect,
                Outline = outline,
                Fill = fill,
                Caption = captionText,
                Value = valueText,
                ValueText = value,
                IsOn = on,
                IsActive = active,
                Press = press,
                Danger = danger
            };
            m_buttons.Add(button);
            return button;
        }

        private void OnDestroy()
        {
            if (m_stick != null)
            {
                m_stick.Reply -= OnReply;
            }
            if (m_hands != null)
            {
                m_hands.PosesUpdated -= LatePlace;
            }
            IsVisible = false;
        }
    }
}
