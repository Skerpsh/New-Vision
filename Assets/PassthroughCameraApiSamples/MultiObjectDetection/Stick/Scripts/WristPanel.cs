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
    /// Four tabs, each split into titled sections, one job per tab:
    /// 01 VISION: DETECTION (on/off, model, boxes) and OBJECTS (classes, FIND = lead me back to something seen, QR codes).
    /// 02 ASSIST: GEMINI "what is this?" (gaze / pinch, answer language), SUBTITLES and CAPRI the companion.
    /// 03 STICK: link state, LAPTOP (gestures, media keys), the STICK's own settings and SERVICE (relink, reboot, power).
    /// 04 SYS: read-only live numbers of the headset (they look different from buttons) and LOOK (theme, glitch FX).
    /// Every tile tells what it is: a lamp = on/off switch, ">" = opens a page, red = needs a second press,
    /// greyed out = not available right now. Holding the fingertip over a tile shows a one-line help in the status bar.
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
        private const float TabTop = 14f;
        private const float TabHeight = 40f;
        private const float TabGap = 8f;
        private const float HeaderLineY = 60f;
        private const float ContentTop = 74f;
        private const float Gap = 12f;
        private const float SectionHeight = 28f;
        private const float StatusLineY = Height - 58f;
        private const float StatusTop = Height - 52f;
        private const int ClassRows = 7;

        private static readonly Page[] s_tabs = { Page.Vision, Page.Assist, Page.Stick, Page.Sys };
        private static readonly string[] s_tabNames = { "01 VISION", "02 ASSIST", "03 STICK", "04 SYS" };

        private static readonly byte[] s_brightLevels = { 8, 100, 255 };
        private static readonly string[] s_brightNames = { "LOW", "MED", "HIGH" };
        private static readonly byte[] s_volumeLevels = { 0, 90, 170, 255 };
        private static readonly string[] s_volumeNames = { "OFF", "LOW", "MED", "HIGH" };
        private static readonly string[] s_screens = { "home", "debug", "off" };

        private enum Page { Vision, Assist, Stick, Sys, Classes, Find }
        // Tile = button with a caption and a big value, Stat = read-only number, Row = list line, Small = bar button
        private enum Style { Tile, Stat, Row, Small, Tab }

        private class Button
        {
            public Page Page;
            public Style Style;
            public RectTransform Rect;
            public Image Outline;
            public Image Fill;
            public Text Caption;
            public Text Value;
            public Image Lamp;            // on/off switches: lit when on
            public Text Chevron;          // tiles that open a page
            public Func<string> ValueText;
            public Func<bool> IsOn;       // value in accent (on) or muted (off); for Stat: healthy or warning
            public Func<bool> IsActive;   // filled (current tab)
            public Func<bool> IsAvailable; // greyed out when false (no stick, no Capri in the build, ...)
            public Action Press;          // null = read-only, the finger passes through
            public string Hint;           // shown in the status bar while the fingertip hovers the tile
            public bool Danger;
            public float FlashUntil;
        }

        /// <summary>What a tile is, for the layout helpers (Section / Row).</summary>
        private class Spec
        {
            public string Caption;
            public Func<string> Value;
            public Action Press;
            public Func<bool> On;
            public Func<bool> Available;
            public string Hint;
            public bool Switch;   // shows the lamp
            public bool Opens;    // shows ">"
            public bool Danger;
            public Style Style = Style.Tile;
        }

        /// <summary>A titled group of tiles: "DETECTION ———— YOLO 96MS".</summary>
        private class Section
        {
            public Page Page;
            public RectTransform Root;
            public Text Title;
            public Text Summary;
            public Func<string> SummaryText;
            public Image Mark;
            public Image Rule;
            public string TitleText;
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
        private Image m_statusLine;
        private Image m_cursor;
        private readonly List<Button> m_buttons = new();
        private readonly List<Section> m_sections = new();
        // Layout cursor while building a page (see BeginPage / AddSection / AddRow)
        private Page m_buildPage;
        private float m_buildY;
        // Stick tab status
        private RectTransform m_stickStatus;
        private Image m_questPill;
        private Text m_questPillText;
        private Image m_laptopPill;
        private Text m_laptopPillText;
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

        private Page m_page = Page.Vision;
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
            m_statusLine.color = NvStyle.Dim;
            m_headerMark.color = NvStyle.Accent;
            m_headerMark.rectTransform.anchoredPosition = new Vector2(Margin + TabIndex(m_page) * (TabWidth + TabGap), -HeaderLineY);

            var connected = m_stick.Connected;
            m_linkDot.color = !connected ? NvStyle.Red : m_stick.LaptopPaired ? NvStyle.Green : NvStyle.Yellow;
            // The battery of what the tab is about: the stick on STICK, the headset everywhere else
            int battery;
            bool charging;
            var stickBattery = m_page == Page.Stick;
            if (stickBattery)
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
            m_batteryText.text = (stickBattery ? "STICK " : "HMD ") + (battery < 0 ? "--" : (charging ? "+" : "") + battery + "%");
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
            foreach (var section in m_sections)
            {
                var visible = section.Page == m_page;
                if (section.Root.gameObject.activeSelf != visible)
                {
                    section.Root.gameObject.SetActive(visible);
                }
                if (visible)
                {
                    PaintSection(section, decode);
                }
            }

            m_stickStatus.gameObject.SetActive(m_page == Page.Stick);
            if (m_page == Page.Stick)
            {
                PaintPill(m_questPill, m_questPillText, connected, connected ? "STICK LINKED" : "STICK " + m_stick.PortState.ToUpperInvariant(), decode);
                PaintPill(m_laptopPill, m_laptopPillText, m_stick.LaptopPaired, m_stick.LaptopPaired ? "LAPTOP PAIRED" : "NO LAPTOP", decode);
            }

            m_classesHeader.gameObject.SetActive(m_page == Page.Classes);
            if (m_page == Page.Classes && m_nv.Classes != null)
            {
                var pages = ClassPages;
                m_classesHeader.text = NvStyle.Decode(
                    $"VISION > CLASSES  {m_nv.Classes.EnabledCount}/{m_nv.Classes.Labels.Length}  //  PAGE {m_classPage + 1}/{pages}", decode);
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
                    ? $"VISION > FIND  {target.Name.ToUpperInvariant()}  //  {m_nv.Memory.DistanceTo(target):0.0}M"
                    : $"VISION > FIND  {m_findList.Count} SEEN  //  PAGE {m_findPage + 1}/{FindPages}", decode);
                m_findHeader.color = NvStyle.Accent;
            }

            // Status bar: a fresh message first, then help for the tile under the finger, then the page's own line
            var showMessage = Time.time - m_messageTime < MessageSeconds;
            var hint = m_hovered?.Hint;
            m_footer.text = showMessage ? m_message : hint ?? FooterText();
            m_footer.color = showMessage ? NvStyle.Text : hint != null ? NvStyle.Accent : NvStyle.Muted;
        }

        private static int TabIndex(Page page) => page switch
        {
            Page.Assist => 1,
            Page.Stick => 2,
            Page.Sys => 3,
            _ => 0   // VISION and its sub-pages CLASSES / FIND
        };

        private string FooterText() => m_page switch
        {
            Page.Vision => m_nv.GetDetecting != null && m_nv.GetDetecting() ? "DETECTION ONLINE" : "DETECTION PAUSED",
            Page.Assist => ObjectInfoLookup.DwellEnabled ? "LOOK AT A THING 2 S OR PINCH TO ASK" : "PINCH AT A THING TO ASK",
            Page.Stick => m_stick.Connected ? $"SCREEN {m_stick.Field("scr")?.ToUpperInvariant()}  //  THEME {NvStyle.Themes[NvStyle.ThemeIndex].Name}"
                : "NO STICK  //  SERVICE > RELINK",
            Page.Sys => $"UP {Time.realtimeSinceStartup / 60f:0}M  //  MODEL {m_nv.ModelNames[m_nv.GetModel()]}  //  {SystemInfo.systemMemorySize / 1024}GB",
            Page.Classes => "TAP A CLASS TO SWITCH IT",
            Page.Find => m_nv.Memory != null && m_nv.Memory.Target != null ? "TAP AGAIN OR STOP TO END" : "TAP AN OBJECT TO FIND IT",
            _ => ""
        };

        private void PaintButton(Button b, float decode)
        {
            var flashing = Time.time < b.FlashUntil;
            var active = b.IsActive != null && b.IsActive();
            var available = b.IsAvailable == null || b.IsAvailable();
            var stat = b.Style == Style.Stat;
            var filled = !stat && (flashing || active);
            var on = b.IsOn == null || b.IsOn();
            var accent = b.Danger ? NvStyle.Red : NvStyle.Accent;
            b.Fill.enabled = filled;
            b.Fill.color = accent;
            var value = b.ValueText();
            // Only the page that just opened decodes; values that change later just change
            b.Value.text = decode < 1f ? NvStyle.Decode(value, decode) : value;

            if (stat)
            {
                // Read-only: no frame, only a bar on the left; a number out of its healthy range turns yellow
                b.Outline.color = on ? NvStyle.Dim : NvStyle.Yellow;
                b.Caption.color = NvStyle.Muted;
                b.Value.color = on ? NvStyle.Text : NvStyle.Yellow;
                return;
            }

            var hovered = b == m_hovered;
            b.Outline.color = hovered ? accent
                : !available ? NvStyle.WithAlpha(NvStyle.Dim, 0.5f)
                : b.Danger ? NvStyle.WithAlpha(NvStyle.Red, 0.45f) : NvStyle.Dim;
            if (b.Caption != null)
            {
                b.Caption.color = filled ? Color.black : available ? NvStyle.Muted : NvStyle.Dim;
            }
            b.Value.color = filled ? Color.black
                : !available ? NvStyle.Dim
                : on ? (b.Style == Style.Tab ? NvStyle.Text : accent) : NvStyle.Muted;
            if (b.Lamp != null)
            {
                b.Lamp.color = filled ? Color.black : on && available ? accent : NvStyle.Dim;
            }
            if (b.Chevron != null)
            {
                b.Chevron.color = filled ? Color.black : hovered ? accent : NvStyle.Muted;
            }
        }

        private static void PaintSection(Section section, float decode)
        {
            section.Title.text = decode < 1f ? NvStyle.Decode(section.TitleText, decode) : section.TitleText;
            section.Title.color = NvStyle.Text;
            section.Mark.color = NvStyle.Accent;
            section.Rule.color = NvStyle.Dim;
            if (section.Summary != null)
            {
                section.Summary.text = section.SummaryText() ?? "";
                section.Summary.color = NvStyle.Muted;
            }
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
                if (button.Press == null || !button.Rect.gameObject.activeInHierarchy)
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
            BuildVision();
            BuildAssist();
            BuildStick();
            BuildSys();
            BuildClasses();
            BuildFind();
            BuildStatusBar();

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

        private static float TabWidth => (Width - Margin * 2f - TabGap * (s_tabs.Length - 1)) / s_tabs.Length;

        /// <summary>Four equal tabs over the whole width; the accent line under the header marks the open one.</summary>
        private void BuildHeader()
        {
            for (var i = 0; i < s_tabs.Length; i++)
            {
                var page = s_tabs[i];
                var name = s_tabNames[i];
                var index = i;
                _ = AddButton(page, Style.Tab, Margin + i * (TabWidth + TabGap), TabTop, TabWidth, TabHeight, null, () => name,
                    () => ShowPage(page), active: () => TabIndex(m_page) == index);
            }
            m_headerLine = NvStyle.Solid("HeaderLine", m_content, NvStyle.Dim);
            NvStyle.Place(m_headerLine.rectTransform, 0f, HeaderLineY, Width, 2f);
            m_headerMark = NvStyle.Solid("HeaderMark", m_content, NvStyle.Accent);
            NvStyle.Place(m_headerMark.rectTransform, Margin, HeaderLineY, TabWidth, 2f);
        }

        /// <summary>
        /// The bottom line: messages, help for the hovered tile, the page's own status; on the right the stick link
        /// lamp (red = no stick, yellow = stick only, green = stick + laptop) and the battery of the headset or the stick.
        /// </summary>
        private void BuildStatusBar()
        {
            m_statusLine = NvStyle.Solid("StatusLine", m_content, NvStyle.Dim);
            NvStyle.Place(m_statusLine.rectTransform, 0f, StatusLineY, Width, 2f);

            const float bars = 5 * 10f;
            const float textWidth = 128f;
            var right = Width - Margin;
            for (var i = 0; i < m_battery.Length; i++)
            {
                m_battery[i] = NvStyle.Solid("Battery", m_content, NvStyle.Dim);
                NvStyle.Place(m_battery[i].rectTransform, right - bars + i * 10f, StatusTop + 10f, 7f, 20f);
            }
            m_batteryText = NvStyle.Label("BatteryText", m_content, 17, NvStyle.Text, TextAnchor.MiddleRight);
            NvStyle.Place(m_batteryText.rectTransform, right - bars - 6f - textWidth, StatusTop, textWidth, 40f);
            var dotX = right - bars - 6f - textWidth - 16f;
            m_linkDot = NvStyle.Solid("Link", m_content, NvStyle.Red);
            NvStyle.Place(m_linkDot.rectTransform, dotX, StatusTop + 14f, 12f, 12f);

            m_footer = NvStyle.Label("Footer", m_content, 17, NvStyle.Muted);
            NvStyle.Place(m_footer.rectTransform, Margin + 4f, StatusTop, dotX - Margin - 14f, 40f);
            m_footer.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_footer.resizeTextForBestFit = true;
            m_footer.resizeTextMinSize = 11;
            m_footer.resizeTextMaxSize = m_footer.fontSize;
        }

        // ------------------------------------------------------------------ layout helpers

        private void BeginPage(Page page, float top = ContentTop)
        {
            m_buildPage = page;
            m_buildY = top;
        }

        /// <summary>A section title with an accent mark, a thin rule under it and an optional live summary on the right.</summary>
        private void AddSection(string title, Func<string> summary = null)
        {
            var width = Width - Margin * 2f;
            var root = NvStyle.Rect("Section " + title, m_content);
            NvStyle.Place(root, Margin, m_buildY, width, SectionHeight);
            var mark = NvStyle.Solid("Mark", root, NvStyle.Accent);
            NvStyle.Place(mark.rectTransform, 0f, 7f, 6f, 13f);
            var titleText = NvStyle.Label("Title", root, 17, NvStyle.Text);
            NvStyle.Place(titleText.rectTransform, 14f, 0f, 290f, SectionHeight - 2f);
            Text summaryText = null;
            if (summary != null)
            {
                summaryText = NvStyle.Label("Summary", root, 15, NvStyle.Muted, TextAnchor.MiddleRight);
                NvStyle.Place(summaryText.rectTransform, 300f, 0f, width - 300f, SectionHeight - 2f);
                summaryText.resizeTextForBestFit = true;
                summaryText.resizeTextMinSize = 11;
                summaryText.resizeTextMaxSize = summaryText.fontSize;
            }
            var rule = NvStyle.Solid("Rule", root, NvStyle.Dim);
            NvStyle.Place(rule.rectTransform, 0f, SectionHeight - 2f, width, 2f);
            m_sections.Add(new Section
            {
                Page = m_buildPage,
                Root = root,
                Title = titleText,
                TitleText = title,
                Summary = summaryText,
                SummaryText = summary,
                Mark = mark,
                Rule = rule
            });
            m_buildY += SectionHeight + 6f;
        }

        /// <summary>One row of equal tiles across the panel.</summary>
        private void AddRow(float height, int columns, params Spec[] specs)
        {
            var width = (Width - Margin * 2f - Gap * (columns - 1)) / columns;
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                _ = AddButton(m_buildPage, spec.Style, Margin + i * (width + Gap), m_buildY, width, height, spec.Caption,
                    spec.Value, spec.Press, spec.On, null, spec.Danger, spec.Available, spec.Hint, spec.Switch, spec.Opens);
            }
            m_buildY += height + Gap;
        }

        private static Spec Stat(string caption, Func<string> value, Func<bool> healthy = null) =>
            new() { Caption = caption, Value = value, On = healthy, Style = Style.Stat };

        // ------------------------------------------------------------------ 01 VISION

        private void BuildVision()
        {
            BeginPage(Page.Vision);
            AddSection("DETECTION", () => !m_nv.GetDetecting() ? "PAUSED"
                : SentisInferenceRunManager.LastInferenceMs > 0f ? $"YOLO {SentisInferenceRunManager.LastInferenceMs:0}MS" : "STARTING");
            AddRow(120f, 3,
                new Spec
                {
                    Caption = "DETECT", Value = () => m_nv.GetDetecting() ? "ON" : "OFF", On = () => m_nv.GetDetecting(), Switch = true,
                    Hint = "PAUSE OR RESUME OBJECT DETECTION",
                    Press = () =>
                    {
                        var on = !m_nv.GetDetecting();
                        m_nv.SetDetecting(on);
                        ShowMessage(on ? "DETECTION ONLINE" : "DETECTION PAUSED");
                    }
                },
                new Spec
                {
                    Caption = "MODEL", Value = () => m_nv.ModelNames[m_nv.GetModel()],
                    Hint = "NEXT YOLO MODEL  //  SMALLER = FASTER",
                    Press = () =>
                    {
                        m_nv.SetModel(m_nv.GetModel() + 1);
                        ShowMessage("MODEL " + m_nv.ModelNames[m_nv.GetModel()]);
                    }
                },
                new Spec
                {
                    Caption = "BOXES", Value = () => m_nv.GetMaxBoxes().ToString(),
                    Hint = "HOW MANY OBJECTS ARE TAGGED AT ONCE",
                    Press = () =>
                    {
                        m_nv.SetMaxBoxes(m_nv.GetMaxBoxes() % 6 + 1);
                        ShowMessage($"UP TO {m_nv.GetMaxBoxes()} BOXES");
                    }
                });

            AddSection("OBJECTS", () => $"IN VIEW {ConfirmedObjects()}  //  SEEN {(m_nv.Memory != null ? m_nv.Memory.Count : 0)}");
            AddRow(120f, 3,
                new Spec
                {
                    Caption = "CLASSES", Value = () => $"{m_nv.Classes.EnabledCount}/{m_nv.Classes.Labels.Length}", Opens = true,
                    Hint = "CHOOSE WHICH THINGS TO DETECT",
                    Press = () => ShowPage(Page.Classes)
                },
                new Spec
                {
                    Caption = "FIND", Opens = true,
                    Value = () =>
                    {
                        var memory = m_nv.Memory;
                        return memory == null ? "--" : memory.Target != null ? memory.Target.Name.ToUpperInvariant() : memory.Count.ToString();
                    },
                    On = () => m_nv.Memory != null && m_nv.Memory.Count > 0,
                    Hint = "LEAD ME BACK TO SOMETHING I SAW",
                    Press = OpenFind
                },
                new Spec
                {
                    Caption = "QR", Value = () => ObjectInfoLookup.QrEnabled ? "ON" : "OFF", On = () => ObjectInfoLookup.QrEnabled, Switch = true,
                    Hint = "READ QR CODES AND BARCODES",
                    Press = () =>
                    {
                        ObjectInfoLookup.QrEnabled = !ObjectInfoLookup.QrEnabled;
                        ShowMessage(ObjectInfoLookup.QrEnabled ? "CODE SCANNER ON" : "CODE SCANNER OFF");
                    }
                });
        }

        private void OpenFind()
        {
            m_findPage = 0;
            m_nextFindSort = 0f;
            ShowPage(Page.Find);
        }

        // ------------------------------------------------------------------ 02 ASSIST

        private void BuildAssist()
        {
            BeginPage(Page.Assist);
            AddSection("GEMINI  //  WHAT IS THIS?", () => ObjectInfoLookup.LastAnswerSeconds > 0f
                ? $"LAST ANSWER {ObjectInfoLookup.LastAnswerSeconds:0.0}S" : "");
            AddRow(100f, 2,
                new Spec
                {
                    Caption = "ASK BY", Value = () => ObjectInfoLookup.DwellEnabled ? "GAZE" : "PINCH", On = () => ObjectInfoLookup.DwellEnabled,
                    Hint = "GAZE: LOOK 2 S OR PINCH  //  PINCH: ONLY PINCH",
                    Press = () =>
                    {
                        ObjectInfoLookup.DwellEnabled = !ObjectInfoLookup.DwellEnabled;
                        ShowMessage(ObjectInfoLookup.DwellEnabled ? "ASK BY GAZE OR PINCH" : "ASK BY PINCH ONLY");
                    }
                },
                new Spec
                {
                    Caption = "LANGUAGE", Value = () => ObjectInfoLookup.AnswerRussian ? "RU" : "EN", On = () => ObjectInfoLookup.AnswerRussian,
                    Hint = "LANGUAGE OF GEMINI ANSWERS",
                    Press = () =>
                    {
                        ObjectInfoLookup.AnswerRussian = !ObjectInfoLookup.AnswerRussian;
                        ShowMessage(ObjectInfoLookup.AnswerRussian ? "GEMINI ANSWERS // RU" : "GEMINI ANSWERS // EN");
                    }
                });

            AddSection("SUBTITLES", () => m_nv.Speech != null && m_nv.Speech.Enabled ? "MICROPHONE // RU" : "");
            AddRow(100f, 2,
                new Spec
                {
                    Caption = "SUBS", Value = SubsState, On = () => m_nv.Speech != null && m_nv.Speech.Enabled, Switch = true,
                    Available = () => m_nv.Speech != null,
                    Hint = "LIVE RUSSIAN SUBTITLES OF WHAT YOU HEAR",
                    Press = () =>
                    {
                        if (m_nv.Speech == null)
                        {
                            ShowMessage("SUBTITLES NOT IN THIS BUILD");
                            return;
                        }
                        m_nv.Speech.Enabled = !m_nv.Speech.Enabled;
                        ShowMessage(m_nv.Speech.Enabled ? "SUBTITLES ON // RU" : "SUBTITLES OFF");
                    }
                });

            AddSection("CAPRI  //  COMPANION", () => m_nv.Companion == null || !m_nv.Companion.Available ? "NOT IN THIS BUILD" : "");
            AddRow(100f, 2,
                new Spec
                {
                    Caption = "SHOW", Value = CapriState, On = () => m_nv.Companion != null && m_nv.Companion.Visible, Switch = true,
                    Available = CapriAvailable,
                    Hint = "CALL CAPRI OR SEND HER AWAY",
                    Press = () =>
                    {
                        var capri = m_nv.Companion;
                        if (!CapriAvailable())
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
                    }
                },
                new Spec
                {
                    Caption = "SIT", Value = () => "GO", Available = CapriAvailable,
                    Hint = "CAPRI SITS ON THE EDGE OF THE BED",
                    Press = () =>
                    {
                        if (CapriAvailable())
                        {
                            m_nv.Companion.Sit();
                        }
                        else
                        {
                            ShowMessage("CAPRI NOT IN THIS BUILD");
                        }
                    }
                });
        }

        private bool CapriAvailable() => m_nv.Companion != null && m_nv.Companion.Available;

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

        // ------------------------------------------------------------------ 03 STICK

        private void BuildStick()
        {
            // Status: two pills (stick link, laptop), Vartovyi's ALL CLEAR style
            m_stickStatus = NvStyle.Rect("StickStatus", m_content);
            NvStyle.Place(m_stickStatus, 0f, 0f, Width, Height);
            var pillWidth = (Width - Margin * 2f - Gap) / 2f;
            (m_questPill, m_questPillText) = Pill("Quest", Margin, pillWidth);
            (m_laptopPill, m_laptopPillText) = Pill("Laptop", Margin + pillWidth + Gap, pillWidth);

            BeginPage(Page.Stick, ContentTop + 40f + Gap);
            Func<bool> linked = () => m_stick.Connected;
            Func<bool> paired = () => m_stick.LaptopPaired;

            AddSection("LAPTOP  //  VIA STICK", () => m_stick.LaptopPaired ? "BLUETOOTH KEYBOARD" : "NOT PAIRED");
            AddRow(80f, 3,
                new Spec
                {
                    Caption = "GESTURES", Value = () => m_gestures.Enabled ? "ON" : "OFF", On = () => m_gestures.Enabled, Switch = true,
                    Hint = "THUMB GESTURES WHILE LOOKING AT IT",
                    Press = () =>
                    {
                        m_gestures.Enabled = !m_gestures.Enabled;
                        ShowMessage(m_gestures.Enabled ? "LAPTOP GESTURES ON" : "LAPTOP GESTURES OFF");
                    }
                },
                new Spec
                {
                    Caption = "PLAY", Value = () => "PAUSE", Available = paired,
                    Hint = "PLAY / PAUSE ON THE LAPTOP",
                    Press = () => SendStick("CMD play", "PLAY/PAUSE -> LAPTOP")
                },
                new Spec
                {
                    Caption = "MUTE", Value = () => "SOUND", Available = paired,
                    Hint = "MUTE / UNMUTE THE LAPTOP",
                    Press = () => SendStick("CMD mute", "MUTE -> LAPTOP")
                });

            AddSection("STICK", () => m_stick.Connected
                ? $"PING {m_stick.PingMs}MS  //  UP {m_stick.IntField("up") / 60}M  //  HEAP {m_stick.IntField("heap") / 1024}K"
                : "NOT CONNECTED");
            AddRow(80f, 4,
                new Spec
                {
                    Caption = "SCREEN", Value = () => (m_stick.Field("scr") ?? "--").ToUpperInvariant(), On = () => m_stick.Field("scr") != "off",
                    Available = linked, Hint = "STICK SCREEN: HOME / DEBUG / OFF",
                    Press = () =>
                    {
                        var index = Array.IndexOf(s_screens, m_stick.Field("scr"));
                        var next = s_screens[(index + 1) % s_screens.Length];
                        SendStick($"SET scr {next}", null);
                        m_stick.SetField("scr", next);
                    }
                },
                new Spec
                {
                    Caption = "BRIGHT", Value = () => m_stick.Connected ? s_brightNames[Nearest(s_brightLevels, m_stick.IntField("bri", 100))] : "--",
                    Available = linked, Hint = "BRIGHTNESS OF THE STICK SCREEN",
                    Press = () =>
                    {
                        var next = s_brightLevels[(Nearest(s_brightLevels, m_stick.IntField("bri", 100)) + 1) % s_brightLevels.Length];
                        SendStick($"SET bri {next}", null);
                        m_stick.SetField("bri", next.ToString());
                    }
                },
                new Spec
                {
                    Caption = "VOLUME", Value = () => m_stick.Connected ? s_volumeNames[Nearest(s_volumeLevels, m_stick.IntField("vol", 90))] : "--",
                    On = () => m_stick.IntField("vol", 90) > 0, Available = linked, Hint = "LOUDNESS OF THE STICK BEEPS",
                    Press = () =>
                    {
                        var next = s_volumeLevels[(Nearest(s_volumeLevels, m_stick.IntField("vol", 90)) + 1) % s_volumeLevels.Length];
                        SendStick($"SET vol {next}", null);
                        m_stick.SetField("vol", next.ToString());
                    }
                },
                new Spec
                {
                    Caption = "FLIP", Value = () => m_stick.Connected ? (m_stick.Field("rot") == "1" ? "180°" : "0°") : "--",
                    Available = linked, Hint = "TURN THE STICK SCREEN UPSIDE DOWN",
                    Press = () =>
                    {
                        var next = m_stick.Field("rot") == "1" ? "0" : "1";
                        SendStick($"SET rot {next}", next == "1" ? "SCREEN TURNED 180°" : "SCREEN UPRIGHT");
                        m_stick.SetField("rot", next);
                    }
                });

            AddSection("SERVICE");
            AddRow(52f, 4,
                new Spec
                {
                    Value = () => "BEEP", Style = Style.Small, Available = linked,
                    Hint = "ONE BEEP: CHECKS THE LINK",
                    Press = () => SendStick("BEEP", null)
                },
                new Spec
                {
                    Value = () => "RELINK", Style = Style.Small,
                    Hint = "DROP AND REBUILD THE BLUETOOTH LINK",
                    Press = () =>
                    {
                        m_stick.Reconnect();
                        ShowMessage("RECONNECTING");
                    }
                },
                new Spec
                {
                    Value = () => m_confirm == "reboot" ? "SURE?" : "REBOOT", Style = Style.Small, Danger = true, Available = linked,
                    Hint = "RESTART THE STICK  //  PRESS TWICE",
                    Press = () => Confirmed("reboot", () => SendStick("REBOOT", "STICK REBOOTING"))
                },
                new Spec
                {
                    Value = () => m_confirm == "off" ? "SURE?" : "POWER", Style = Style.Small, Danger = true, Available = linked,
                    Hint = "TURN THE STICK OFF  //  PRESS TWICE",
                    Press = () => Confirmed("off", () => SendStick("OFF", "STICK OFF // SIDE BUTTON TO WAKE"))
                });
        }

        private (Image, Text) Pill(string name, float x, float width)
        {
            var pill = NvStyle.ChamferImage(name, m_stickStatus, 10f, NvStyle.Yellow, false, 2f);
            NvStyle.Place(pill.rectTransform, x, ContentTop, width, 40f);
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
            NvStyle.Place(m_classesHeader.rectTransform, Margin + 4f, ContentTop - 2f, Width - Margin * 2f, 30f);

            const float top = ContentTop + 34f;
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

            // Bottom bar: page back / forward, presets, back to VISION
            var barTop = top + ClassRows * (rowHeight + rowGap) + 4f;
            var labels = new[] { "<", ">", "ALL", "NONE", "HOME", "BACK" };
            var hints = new[] { "PREVIOUS PAGE", "NEXT PAGE", "DETECT EVERYTHING", "DETECT NOTHING", "THINGS FOUND AT HOME", "BACK TO VISION" };
            var actions = new Action[]
            {
                () => { m_classPage = (m_classPage + ClassPages - 1) % ClassPages; m_pageChangedAt = Time.time; },
                () => { m_classPage = (m_classPage + 1) % ClassPages; m_pageChangedAt = Time.time; },
                () => m_nv.Classes.SetAll(true),
                () => m_nv.Classes.SetAll(false),
                () => m_nv.Classes.HomePreset(),
                () => ShowPage(Page.Vision)
            };
            var width = (Width - Margin * 2f - Gap * (labels.Length - 1)) / labels.Length;
            for (var i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                _ = AddButton(Page.Classes, Style.Small, Margin + i * (width + Gap), barTop, width, 46f, null, () => label, actions[i],
                    hint: hints[i]);
            }
        }

        // ------------------------------------------------------------------ 04 SYS

        /// <summary>Live numbers of the headset (read-only, drawn without a frame) and the look of the whole UI.</summary>
        private void BuildSys()
        {
            BeginPage(Page.Sys);
            AddSection("PERFORMANCE", () => $"UP {Time.realtimeSinceStartup / 60f:0}M");
            AddRow(64f, 3,
                Stat("FPS", () => $"{m_fps:0}", () => m_fps >= 65f),
                Stat("YOLO", () => SentisInferenceRunManager.LastInferenceMs > 0f ? $"{SentisInferenceRunManager.LastInferenceMs:0}MS" : "--",
                    () => SentisInferenceRunManager.LastInferenceMs < 400f),
                Stat("LAYERS", () => SentisInferenceRunManager.LastLayersPerFrame > 0 ? $"{SentisInferenceRunManager.LastLayersPerFrame}/F" : "--"));
            // GPU load is not shown: OVRPlugin.gpuUtilLevel is always 0 on this headset
            AddRow(64f, 3,
                Stat("TEMP", () => m_batteryTemp > 0f ? $"{m_batteryTemp:0}°C" : "--", () => m_batteryTemp < 42f),
                Stat("RAM", () =>
                {
                    var used = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                    return used > 0 ? $"{used / (1024 * 1024)}MB" : "--";
                }),
                Stat("BATTERY", () => SystemInfo.batteryLevel < 0f ? "--" : $"{SystemInfo.batteryLevel * 100f:0}%",
                    () => SystemInfo.batteryLevel is < 0f or > 0.2f));

            AddSection("SESSION");
            AddRow(64f, 3,
                Stat("IN VIEW", () => m_nv.GetDetecting() ? ConfirmedObjects().ToString() : "OFF"),
                Stat("SEEN", () => m_nv.Memory != null ? m_nv.Memory.Count.ToString() : "--"),
                Stat("GEMINI", () => ObjectInfoLookup.LastAnswerSeconds > 0f ? $"{ObjectInfoLookup.LastAnswerSeconds:0.0}S" : "--"));

            AddSection("LOOK");
            AddRow(80f, 3,
                new Spec
                {
                    Caption = "THEME", Value = () => NvStyle.Themes[NvStyle.ThemeIndex].Name,
                    Hint = "COLOURS OF PANEL, HUD AND STICK",
                    Press = () =>
                    {
                        NvStyle.ThemeIndex++;
                        m_glitch.Pulse(0.3f);
                        if (m_stick.Connected)
                        {
                            SendLook();
                        }
                    }
                },
                new Spec
                {
                    Caption = "FX", Value = () => NvStyle.FxNames[NvStyle.FxLevel], On = () => NvStyle.FxLevel > 0,
                    Hint = "GLITCH EFFECTS: OFF / LOW / HIGH",
                    Press = () =>
                    {
                        NvStyle.FxLevel = (NvStyle.FxLevel + 1) % 3;
                        m_glitch.Pulse(0.3f);
                        if (m_stick.Connected)
                        {
                            SendLook();
                        }
                    }
                });
        }

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
            NvStyle.Place(m_findHeader.rectTransform, Margin + 4f, ContentTop - 2f, Width - Margin * 2f, 30f);

            const float top = ContentTop + 34f;
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
            var hints = new[] { "PREVIOUS PAGE", "NEXT PAGE", "STOP LEADING ME", "FORGET EVERYTHING  //  PRESS TWICE", "BACK TO VISION" };
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
                () => ShowPage(Page.Vision)
            };
            var width = (Width - Margin * 2f - Gap * (labels.Length - 1)) / labels.Length;
            for (var i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                var index = i;
                _ = AddButton(Page.Find, Style.Small, Margin + i * (width + Gap), barTop, width, 46f, null,
                    () => index == 3 && m_confirm == "clear" ? "SURE?" : label, actions[i], danger: index == 3, hint: hints[i]);
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
            Func<string> value, Action press, Func<bool> on = null, Func<bool> active = null, bool danger = false,
            Func<bool> available = null, string hint = null, bool lamp = false, bool opens = false)
        {
            var rect = NvStyle.Rect(caption ?? "Button", m_content);
            NvStyle.Place(rect, x, y, width, height);
            var cut = style == Style.Tile ? 14f : 8f;
            var fill = NvStyle.ChamferImage("Fill", rect, cut, NvStyle.Accent);
            NvStyle.Stretch(fill.rectTransform);
            Image outline;
            if (style == Style.Stat)
            {
                // A read-only number has no frame to press, only a bar on its left
                fill.enabled = false;
                outline = NvStyle.Solid("Bar", rect, NvStyle.Dim);
                NvStyle.Place(outline.rectTransform, 0f, 4f, 3f, height - 8f);
            }
            else
            {
                outline = NvStyle.ChamferImage("Outline", rect, cut, NvStyle.Dim, false, 2f);
                NvStyle.Stretch(outline.rectTransform);
            }

            // Room on the right of the caption for the lamp or the ">"
            var corner = lamp || opens ? 30f : 0f;
            Text captionText = null;
            Text valueText;
            switch (style)
            {
                case Style.Tile:
                    captionText = NvStyle.Label("Caption", rect, 17, NvStyle.Muted, TextAnchor.UpperLeft);
                    captionText.text = caption;
                    NvStyle.Place(captionText.rectTransform, 14f, 10f, width - 20f - corner, 24f);
                    valueText = NvStyle.Label("Value", rect, 30, NvStyle.Accent, TextAnchor.LowerLeft);
                    NvStyle.Place(valueText.rectTransform, 14f, height - 52f, width - 20f, 42f);
                    break;
                case Style.Stat:
                    captionText = NvStyle.Label("Caption", rect, 15, NvStyle.Muted, TextAnchor.UpperLeft);
                    captionText.text = caption;
                    NvStyle.Place(captionText.rectTransform, 14f, 4f, width - 18f, 22f);
                    valueText = NvStyle.Label("Value", rect, 28, NvStyle.Text, TextAnchor.LowerLeft);
                    NvStyle.Place(valueText.rectTransform, 14f, height - 40f, width - 18f, 36f);
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

            Image lampImage = null;
            if (lamp)
            {
                // On/off switch: a small square lamp in the top right corner, lit while on
                lampImage = NvStyle.Solid("Lamp", rect, NvStyle.Dim);
                NvStyle.Place(lampImage.rectTransform, width - 26f, 14f, 12f, 12f);
            }
            Text chevron = null;
            if (opens)
            {
                // Opens a page of its own
                chevron = NvStyle.Label("Opens", rect, 24, NvStyle.Muted, TextAnchor.MiddleCenter);
                chevron.text = ">";
                NvStyle.Place(chevron.rectTransform, width - 34f, 4f, 28f, 30f);
            }

            var button = new Button
            {
                Page = page,
                Style = style,
                Rect = rect,
                Outline = outline,
                Fill = fill,
                Caption = captionText,
                Value = valueText,
                Lamp = lampImage,
                Chevron = chevron,
                ValueText = value,
                IsOn = on,
                IsActive = active,
                IsAvailable = available,
                Press = press,
                Hint = hint,
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
