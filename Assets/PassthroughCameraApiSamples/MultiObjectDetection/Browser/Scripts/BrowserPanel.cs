// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// The in-app browser, modelled on the Quest Browser window:
    /// - point with a controller or hand ray: trigger / pinch taps, drag scrolls (with fling), hovering lights links up;
    /// - or touch the page with the index finger like a touch screen;
    /// - thumbstick scrolls smoothly;
    /// - the bar under the window moves it, the corners resize it;
    /// - text fields open the Quest system keyboard (an on-screen keyboard is the fallback);
    /// - B closes it.
    /// Everything is built in code; hit testing is done here, without Unity's EventSystem.
    /// </summary>
    public class BrowserPanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        // Page in WebView pixels; on the panel 1 canvas unit = 1 page pixel
        private const int PageWidth = 1280;
        private const int PageHeight = 800;
        private const float MetersPerUnit = 0.00072f; // page 0.92 m wide at scale 1
        private const float MinScale = 0.5f;
        private const float MaxScale = 2.2f;
        private const float Distance = 1.1f;
        private const float Padding = 14f;
        private const float ToolbarHeight = 72f;
        private const float ButtonGap = 10f;
        private const float GrabBarOffset = 34f;
        /// <summary>Page pixels the pointer may move while pressed before the press turns into a drag (no tap then).</summary>
        private const float DragThreshold = 18f;
        /// <summary>Thumbstick scrolling speed at full tilt, page pixels per second (gentle tilt is much slower).</summary>
        private const float StickScrollSpeed = 1800f;
        /// <summary>Below this pinch strength / trigger value a press counts as being let go: the page stops following,
        /// so the jerk of opening the fingers doesn't fling the page somewhere.</summary>
        private const float HandReleaseFreeze = 0.82f;
        private const float TriggerReleaseFreeze = 0.5f;
        private const float StatePollInterval = 0.2f;
        private const float LaserLength = 3f;

        // Direct touch with the index fingertip (meters in front of the panel)
        private const float TouchHoverDistance = 0.08f;
        private const float TouchPressDistance = 0.004f;
        private const float TouchReleaseDistance = 0.014f;

        // Fallback keyboard: 10 units per row
        private const float KeyUnit = 96f;
        private const float KeyHeight = 72f;
        private const float KeyGap = 8f;

        private static readonly Color PanelColor = new(0.04f, 0.06f, 0.08f, 0.88f);
        private static readonly Color ButtonColor = new(1f, 1f, 1f, 0.08f);
        private static readonly Color ButtonHover = new(1f, 1f, 1f, 0.2f);
        private static readonly Color ButtonPressed = new(0.55f, 1f, 0.6f, 0.35f);
        private static readonly Color DisabledText = new(1f, 1f, 1f, 0.3f);
        private static readonly Color AccentColor = new(0.55f, 1f, 0.6f);

        private class Button
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public Action Click;
            public bool Enabled = true;
        }

        private enum Target { None, Page, Button, Grab, ResizeLeft, ResizeRight }

        private Transform m_head;
        private BrowserPointer m_pointer;
        private WebViewBridge m_web;
        private float m_scale = 1f;

        private RectTransform m_root;
        private RectTransform m_pageRect;
        private Text m_message;
        private Image m_messageBackground;
        private Image m_progress;
        private RectTransform m_cursor;
        private Image m_cursorRing;
        private Image m_cursorDot;
        private Image m_grabBar;
        private RectTransform m_grabArea;
        private Image m_resizeLeft;
        private Image m_resizeRight;
        private Button m_backButton;
        private Button m_forwardButton;
        private Button m_urlButton;
        private readonly List<Button> m_buttons = new();

        private RectTransform m_keyboard;
        private readonly List<(Button button, string letter, string symbol)> m_charKeys = new();
        private Button m_shiftKey;
        private Button m_symbolsKey;
        private bool m_keyboardVisible;
        private bool m_shift;
        private bool m_symbols;
        private bool m_urlMode;
        private string m_urlEdit = "";

        private TouchScreenKeyboard m_systemKeyboard;
        private bool m_systemKeyboardForUrl;
        private string m_systemKeyboardSent;
        private int m_focusVersion;

        private float m_nextPoll;
        private float m_openTime;
        private float m_messageUntil;
        private string m_url = "";
        private bool m_pageInputFocused;

        // Current press (ray or finger)
        private Target m_pressTarget;
        private Button m_pressButton;
        private Vector2 m_pressPixel;
        private Vector2 m_lastPixel;
        private bool m_dragging;
        private bool m_fingerTouching;
        private int m_fingerHand = -1;
        private Vector3 m_grabOffset;
        private float m_grabDistance;
        private float m_resizeStartScale;
        private float m_resizeStartDistance;

        private bool m_dragFrozen;
        private readonly OneEuroFilter m_dragFilter = new();

        // Hover
        private bool m_hovering;
        private Vector2 m_hoverPixel;

        public void Init(Transform head, Transform trackingSpace)
        {
            m_head = head;
            var pointerGo = new GameObject("BrowserPointer");
            pointerGo.transform.SetParent(transform, false);
            m_pointer = pointerGo.AddComponent<BrowserPointer>();
            m_pointer.Init(trackingSpace);
            BuildPanel();
            BuildKeyboard();
            m_root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            IsOpen = false;
            m_web?.Dispose();
        }

        public void Open(string url)
        {
            m_web ??= new WebViewBridge(PageWidth, PageHeight);
            m_pageRect.GetComponent<RawImage>().texture = m_web.Texture;
            IsOpen = true;
            m_root.gameObject.SetActive(true);
            Recenter();
            m_web.SetActive(true);
            m_web.LoadUrl(url);
            m_url = url;
            SetUrlMode(false);
            ShowKeyboard(false);
            SetMessage(m_web.Available ? null : "The browser only works on the headset");
            m_nextPoll = 0f;
            m_openTime = Time.time;
            m_focusVersion = m_web.FocusVersion;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            EndPress(m_lastPixel);
            if (m_hovering)
            {
                m_web.Hover(m_hoverPixel, true);
                m_hovering = false;
            }
            CloseSystemKeyboard();
            IsOpen = false;
            ShowKeyboard(false);
            m_web.SetActive(false);
            m_pointer.DrawLaser(false, Vector3.zero);
            m_root.gameObject.SetActive(false);
        }

        /// <summary>Puts the window in front of the head, level, slightly below eye height.</summary>
        private void Recenter()
        {
            var forward = Vector3.ProjectOnPlane(m_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(m_head.up, Vector3.up);
            }
            forward.Normalize();
            var position = m_head.position + forward * Distance + Vector3.down * 0.08f;
            PlaceAt(position);
        }

        /// <summary>Moves the window to a point and turns it to face the head.</summary>
        private void PlaceAt(Vector3 position)
        {
            var toWindow = position - m_head.position;
            var rotation = toWindow.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toWindow, Vector3.up) : m_root.rotation;
            m_root.SetPositionAndRotation(position, rotation);
            m_root.localScale = Vector3.one * (MetersPerUnit * m_scale);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }
            m_web.UpdateTexture();
            PollState();
            UpdateSystemKeyboard();

            if (OVRInput.GetDown(OVRInput.RawButton.B))
            {
                Close();
                return;
            }

            m_pointer.Poll();
            if (!HandleFingerTouch())
            {
                HandleRay();
            }
            UpdateResizeHandles();
        }

        private void PollState()
        {
            if (Time.time < m_nextPoll)
            {
                return;
            }
            m_nextPoll = Time.time + StatePollInterval;

            m_url = m_web.Url;
            var progress = m_web.Progress;
            m_progress.enabled = progress < 100;
            m_progress.rectTransform.sizeDelta = new Vector2(PageWidth * progress / 100f, 4f);
            SetEnabled(m_backButton, m_web.CanGoBack);
            SetEnabled(m_forwardButton, m_web.CanGoForward);
            if (!m_urlMode)
            {
                UpdateUrlText();
            }

            var message = m_web.TakeMessage();
            if (!string.IsNullOrEmpty(message))
            {
                SetMessage(message);
            }
            if (m_message.enabled && Time.time > m_messageUntil)
            {
                SetMessage(null);
            }

            // A text field of the page got the focus: system keyboard, or the fallback keyboard
            var focused = m_web.InputFocused;
            var version = m_web.FocusVersion;
            if (focused && version != m_focusVersion && !m_urlMode)
            {
                OnPageFieldFocused();
            }
            else if (!focused && m_pageInputFocused && !m_urlMode && m_keyboardVisible)
            {
                ShowKeyboard(false);
            }
            m_focusVersion = version;
            m_pageInputFocused = focused;
        }

        // ---- Pointing with a ray ----

        private void HandleRay()
        {
            var ray = m_pointer.Ray;
            var hitPoint = ray.GetPoint(LaserLength);
            var target = Target.None;
            Button button = null;
            var pixel = Vector2.zero;
            var plane = new Plane(m_root.forward, m_root.position);
            if (m_pointer.Valid && plane.Raycast(ray, out var enter) && enter > 0f)
            {
                hitPoint = ray.GetPoint(enter);
                target = HitTest(hitPoint, out button, out pixel);
            }
            else if (m_pressTarget == Target.Page && m_pointer.Valid && plane.Raycast(ray, out enter))
            {
                hitPoint = ray.GetPoint(Mathf.Abs(enter));
                pixel = PagePixel(hitPoint);
            }

            // Dragging the page keeps going outside of it
            if (m_pressTarget == Target.Page && target != Target.Page)
            {
                pixel = PagePixel(hitPoint);
            }

            m_pointer.DrawLaser(m_pointer.Valid, hitPoint);
            ShowCursor(target != Target.None || m_pressTarget != Target.None, hitPoint, m_pointer.PressAmount, 1f);

            if (m_pointer.PressStarted && Time.time > m_openTime + 0.4f)
            {
                // The press goes where the ray was just before the pinch closed
                var pressTarget = target;
                var pressButton = button;
                var pressPixel = pixel;
                var pressPoint = hitPoint;
                if (m_pointer.IsHand && plane.Raycast(m_pointer.PressRay, out var pressEnter) && pressEnter > 0f)
                {
                    pressPoint = m_pointer.PressRay.GetPoint(pressEnter);
                    pressTarget = HitTest(pressPoint, out pressButton, out pressPixel);
                }
                BeginPress(pressTarget, pressButton, pressPixel, pressPoint, m_pointer.PressRay);
            }
            else if (m_pointer.Pressed)
            {
                ContinuePress(pixel, hitPoint, ray);
            }
            else if (m_pointer.PressEnded)
            {
                if (m_pressTarget == Target.Button && m_pressButton == button && button != null)
                {
                    m_pressButton.Click();
                }
                EndPress(pixel);
            }

            UpdateHover(m_pressTarget == Target.None && target == Target.Page, pixel);
            HandleStick(target == Target.Page ? pixel : new Vector2(PageWidth * 0.5f, PageHeight * 0.5f), target == Target.Page);
            UpdateButtonColors(button);
        }

        // ---- Touching with the index fingertip ----

        /// <summary>Returns true while a fingertip is close to the window (then the ray is off).</summary>
        private bool HandleFingerTouch()
        {
            var nearest = -1;
            var nearestDistance = float.MaxValue;
            var nearestPoint = Vector3.zero;
            for (var hand = 0; hand < 2; hand++)
            {
                if (!m_pointer.TryGetFingertip(hand, out var tip))
                {
                    continue;
                }
                // Positive in front of the window (the side facing the user)
                var distance = Vector3.Dot(tip - m_root.position, -m_root.forward);
                var onPlane = tip + m_root.forward * distance;
                if (distance > TouchHoverDistance || distance < -0.05f || !InsideWindow(onPlane))
                {
                    continue;
                }
                if (m_fingerTouching && hand == m_fingerHand)
                {
                    nearest = hand;
                    nearestDistance = distance;
                    nearestPoint = onPlane;
                    break;
                }
                if (distance < nearestDistance)
                {
                    nearest = hand;
                    nearestDistance = distance;
                    nearestPoint = onPlane;
                }
            }

            if (nearest < 0)
            {
                if (m_fingerTouching)
                {
                    m_fingerTouching = false;
                    EndPress(m_lastPixel);
                }
                return false;
            }

            m_pointer.DrawLaser(false, Vector3.zero);
            var target = HitTest(nearestPoint, out var button, out var pixel);
            if (m_pressTarget == Target.Page && target != Target.Page)
            {
                pixel = PagePixel(nearestPoint);
            }
            // The cursor shrinks as the finger gets closer
            var closeness = 1f - Mathf.Clamp01((nearestDistance - TouchPressDistance) / (TouchHoverDistance - TouchPressDistance));
            ShowCursor(true, nearestPoint, closeness, 0.8f);

            if (!m_fingerTouching && nearestDistance < TouchPressDistance && Time.time > m_openTime + 0.4f)
            {
                m_fingerTouching = true;
                m_fingerHand = nearest;
                BeginPress(target, button, pixel, nearestPoint, new Ray(nearestPoint + m_root.forward * -0.1f, m_root.forward));
            }
            else if (m_fingerTouching && nearestDistance > TouchReleaseDistance)
            {
                m_fingerTouching = false;
                if (m_pressTarget == Target.Button && m_pressButton == button && button != null)
                {
                    m_pressButton.Click();
                }
                EndPress(pixel);
            }
            else if (m_fingerTouching)
            {
                ContinuePress(pixel, nearestPoint, new Ray(nearestPoint + m_root.forward * -0.1f, m_root.forward));
            }

            UpdateHover(!m_fingerTouching && target == Target.Page, pixel);
            UpdateButtonColors(button);
            return true;
        }

        private bool InsideWindow(Vector3 point) =>
            Contains(m_root, point) || Contains(m_grabArea, point) || (m_keyboardVisible && Contains(m_keyboard, point));

        // ---- Press handling shared by ray and finger ----

        private void BeginPress(Target target, Button button, Vector2 pixel, Vector3 point, Ray ray)
        {
            m_pressTarget = target;
            m_pressButton = null;
            m_dragging = false;
            m_dragFrozen = false;
            // Hands shake more than controllers: smooth them more at slow speed
            m_dragFilter.Reset(pixel, m_fingerTouching ? 2.5f : m_pointer.IsHand ? 1.2f : 3f);
            switch (target)
            {
                case Target.Page:
                    if (m_hovering)
                    {
                        m_web.Hover(m_hoverPixel, true);
                        m_hovering = false;
                    }
                    m_pressPixel = pixel;
                    m_lastPixel = pixel;
                    m_web.Touch(0, pixel);
                    break;
                case Target.Button:
                    m_pressButton = button.Enabled ? button : null;
                    if (m_pressButton == null)
                    {
                        m_pressTarget = Target.None;
                    }
                    break;
                case Target.Grab:
                    m_grabDistance = Vector3.Distance(ray.origin, point);
                    m_grabOffset = m_root.position - point;
                    break;
                case Target.ResizeLeft:
                case Target.ResizeRight:
                    m_resizeStartScale = m_scale;
                    m_resizeStartDistance = Mathf.Max(0.05f, Vector3.Distance(m_root.position, point));
                    break;
            }
        }

        private void ContinuePress(Vector2 pixel, Vector3 point, Ray ray)
        {
            switch (m_pressTarget)
            {
                case Target.Page:
                    // Letting go of the pinch / trigger moves the ray: from then on the page stays where it is
                    if (!m_fingerTouching && m_pointer.PressAmount < (m_pointer.IsHand ? HandReleaseFreeze : TriggerReleaseFreeze))
                    {
                        m_dragFrozen = true;
                    }
                    if (m_dragFrozen)
                    {
                        break;
                    }
                    pixel = m_dragFilter.Filter(pixel, Time.time);
                    if (!m_dragging && Vector2.Distance(pixel, m_pressPixel) > DragThreshold)
                    {
                        m_dragging = true;
                    }
                    if (m_dragging && Vector2.Distance(pixel, m_lastPixel) > 0.25f)
                    {
                        m_web.Touch(2, pixel);
                        m_lastPixel = pixel;
                    }
                    break;
                case Target.Grab:
                    // Thumbstick pushes the window away / pulls it closer while holding it
                    m_grabDistance = Mathf.Clamp(m_grabDistance + m_pointer.Stick.y * 1.2f * Time.deltaTime, 0.3f, 4f);
                    var grabPoint = m_fingerTouching ? point : ray.GetPoint(m_grabDistance);
                    PlaceAt(grabPoint + m_grabOffset);
                    break;
                case Target.ResizeLeft:
                case Target.ResizeRight:
                    var plane = new Plane(m_root.forward, m_root.position);
                    var resizePoint = point;
                    if (!m_fingerTouching && plane.Raycast(ray, out var enter))
                    {
                        resizePoint = ray.GetPoint(enter);
                    }
                    var distance = Vector3.Distance(m_root.position, resizePoint);
                    m_scale = Mathf.Clamp(m_resizeStartScale * distance / m_resizeStartDistance, MinScale, MaxScale);
                    m_root.localScale = Vector3.one * (MetersPerUnit * m_scale);
                    break;
            }
        }

        private void EndPress(Vector2 pixel)
        {
            if (m_pressTarget == Target.Page)
            {
                if (m_dragging)
                {
                    // Lift where the page was last moved to, timed right after that move: the fling keeps the gesture's speed
                    m_web.TouchUpAtLastMove(m_lastPixel);
                }
                else
                {
                    m_web.Touch(1, m_pressPixel);
                }
            }
            m_pressTarget = Target.None;
            m_pressButton = null;
            m_dragging = false;
            m_dragFrozen = false;
        }

        /// <summary>Mouse-like hover over the page: links light up.</summary>
        private void UpdateHover(bool overPage, Vector2 pixel)
        {
            if (overPage)
            {
                if (!m_hovering || Vector2.Distance(pixel, m_hoverPixel) > 1.5f)
                {
                    m_web.Hover(pixel, false);
                    m_hoverPixel = pixel;
                    m_hovering = true;
                }
            }
            else if (m_hovering)
            {
                m_web.Hover(m_hoverPixel, true);
                m_hovering = false;
            }
        }

        /// <summary>
        /// Thumbstick scrolling: the scrollable part under the cursor (or the page) scrolls directly, smooth and exact,
        /// and stops the moment the stick is let go. Small tilts scroll slowly for reading.
        /// </summary>
        private void HandleStick(Vector2 pixel, bool overPage)
        {
            var stick = m_pointer.Stick;
            if (m_pressTarget != Target.None || stick == Vector2.zero)
            {
                return;
            }
            // Stick up = see what's above
            var curve = new Vector2(Mathf.Sign(stick.x) * stick.x * stick.x, -Mathf.Sign(stick.y) * stick.y * stick.y);
            m_web.ScrollAt(overPage ? pixel : new Vector2(PageWidth * 0.5f, PageHeight * 0.5f), curve * (StickScrollSpeed * Time.deltaTime));
        }

        private Target HitTest(Vector3 point, out Button button, out Vector2 pixel)
        {
            button = null;
            pixel = Vector2.zero;
            foreach (var candidate in m_buttons)
            {
                if (candidate.Rect.gameObject.activeInHierarchy && Contains(candidate.Rect, point))
                {
                    button = candidate;
                    return Target.Button;
                }
            }
            if (Contains(m_pageRect, point))
            {
                pixel = PagePixel(point);
                return Target.Page;
            }
            if (Contains(m_grabArea, point))
            {
                return Target.Grab;
            }
            if (Contains(m_resizeLeft.rectTransform, point))
            {
                return Target.ResizeLeft;
            }
            if (Contains(m_resizeRight.rectTransform, point))
            {
                return Target.ResizeRight;
            }
            return Target.None;
        }

        private Vector2 PagePixel(Vector3 point)
        {
            Vector2 local = m_pageRect.InverseTransformPoint(point);
            var rect = m_pageRect.rect;
            return new Vector2((local.x - rect.xMin) / rect.width * PageWidth, (rect.yMax - local.y) / rect.height * PageHeight);
        }

        private static bool Contains(RectTransform rect, Vector3 worldPoint)
        {
            Vector2 local = rect.InverseTransformPoint(worldPoint);
            return rect.rect.Contains(local);
        }

        private void ShowCursor(bool visible, Vector3 point, float press, float size)
        {
            m_cursor.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }
            m_cursor.position = point - m_root.forward * 0.002f;
            m_cursor.localScale = Vector3.one * size;
            // The ring closes around the dot as the pinch / trigger closes
            var ring = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(press));
            m_cursorRing.rectTransform.localScale = Vector3.one * ring;
            m_cursorRing.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 0.95f, press));
        }

        private void UpdateButtonColors(Button hovered)
        {
            var pressed = m_pointer.Pressed || m_fingerTouching;
            foreach (var button in m_buttons)
            {
                button.Background.color = button == m_pressButton && pressed ? ButtonPressed
                    : button == hovered && button.Enabled ? ButtonHover : ButtonColor;
            }
            if (m_shift)
            {
                m_shiftKey.Background.color = ButtonPressed;
            }
            m_grabBar.color = new Color(1f, 1f, 1f, m_pressTarget == Target.Grab ? 0.9f : 0.45f);
        }

        /// <summary>Corner handles show up only when the pointer is near them (like Quest windows).</summary>
        private void UpdateResizeHandles()
        {
            var active = m_pressTarget is Target.ResizeLeft or Target.ResizeRight;
            var near = m_cursor.gameObject.activeSelf &&
                       (Vector3.Distance(m_cursor.position, m_resizeLeft.rectTransform.position) < 0.12f * m_scale ||
                        Vector3.Distance(m_cursor.position, m_resizeRight.rectTransform.position) < 0.12f * m_scale);
            var alpha = active ? 0.9f : near ? 0.6f : 0.15f;
            m_resizeLeft.color = m_resizeRight.color = new Color(1f, 1f, 1f, alpha);
        }

        // ---- Address bar ----

        private void SetUrlMode(bool on)
        {
            m_urlMode = on;
            m_urlEdit = "";
            UpdateUrlText();
        }

        private void EditUrl()
        {
            if (OpenSystemKeyboard(m_url, TouchScreenKeyboardType.URL, false, true))
            {
                return;
            }
            SetUrlMode(true);
            ShowKeyboard(true);
        }

        private void UpdateUrlText()
        {
            if (m_urlMode)
            {
                m_urlButton.Label.text = m_urlEdit.Length > 0 ? m_urlEdit + "|" : "Search or type an address|";
                m_urlButton.Label.color = m_urlEdit.Length > 0 ? Color.white : DisabledText;
            }
            else
            {
                m_urlButton.Label.text = DisplayUrl(m_url);
                m_urlButton.Label.color = Color.white;
            }
        }

        private static string DisplayUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return url;
            }
            var text = (uri.Scheme == Uri.UriSchemeHttps ? "" : "http://") + uri.Host + (uri.PathAndQuery == "/" ? "" : uri.PathAndQuery);
            return text.Length > 48 ? text.Substring(0, 47) + "…" : text;
        }

        private void Navigate(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                return;
            }
            string url;
            if (CodeContent.TryParseWebUrl(text, out var uri))
            {
                url = uri.AbsoluteUri;
            }
            else if (!text.Contains(" ") && text.Contains(".") && CodeContent.TryParseWebUrl("https://" + text, out uri))
            {
                url = uri.AbsoluteUri;
            }
            else
            {
                url = "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
            }
            m_web.LoadUrl(url);
            m_url = url;
            UpdateUrlText();
        }

        // ---- Quest system keyboard ----

        private void OnPageFieldFocused()
        {
            var type = m_web.InputType;
            var keyboardType = type switch
            {
                "email" => TouchScreenKeyboardType.EmailAddress,
                "url" => TouchScreenKeyboardType.URL,
                "number" => TouchScreenKeyboardType.NumberPad,
                "tel" => TouchScreenKeyboardType.PhonePad,
                "search" => TouchScreenKeyboardType.Search,
                _ => TouchScreenKeyboardType.Default
            };
            if (!OpenSystemKeyboard(m_web.InputValue, keyboardType, type == "password", false))
            {
                ShowKeyboard(true);
            }
        }

        private bool OpenSystemKeyboard(string text, TouchScreenKeyboardType type, bool secure, bool forUrl)
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                return false;
            }
            try
            {
                CloseSystemKeyboard();
                m_systemKeyboard = TouchScreenKeyboard.Open(text ?? "", type, false, false, secure, false, "", 0);
                m_systemKeyboardForUrl = forUrl;
                m_systemKeyboardSent = text ?? "";
                ShowKeyboard(false);
                return m_systemKeyboard != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] browser: system keyboard failed: {e.Message}");
                m_systemKeyboard = null;
                return false;
            }
        }

        private void UpdateSystemKeyboard()
        {
            if (m_systemKeyboard == null)
            {
                return;
            }
            var status = m_systemKeyboard.status;
            var text = m_systemKeyboard.text ?? "";
            if (status == TouchScreenKeyboard.Status.Visible)
            {
                // Typing shows up in the page field live
                if (!m_systemKeyboardForUrl && text != m_systemKeyboardSent)
                {
                    m_web.SetInputValue(text);
                    m_systemKeyboardSent = text;
                }
                return;
            }

            var forUrl = m_systemKeyboardForUrl;
            m_systemKeyboard = null;
            if (status == TouchScreenKeyboard.Status.Done)
            {
                if (forUrl)
                {
                    Navigate(text);
                }
                else
                {
                    if (text != m_systemKeyboardSent)
                    {
                        m_web.SetInputValue(text);
                    }
                    m_web.Key(WebViewBridge.KeyEnter);
                }
            }
            else if (!forUrl)
            {
                m_web.BlurInput();
            }
        }

        private void CloseSystemKeyboard()
        {
            if (m_systemKeyboard != null)
            {
                m_systemKeyboard.active = false;
                m_systemKeyboard = null;
            }
        }

        // ---- Fallback keyboard ----

        private void ShowKeyboard(bool visible)
        {
            m_keyboardVisible = visible;
            m_keyboard.gameObject.SetActive(visible);
            if (!visible)
            {
                m_shift = false;
                m_symbols = false;
                UpdateKeyLabels();
            }
        }

        private void Type(string text)
        {
            if (m_shift)
            {
                text = text.ToUpperInvariant();
                m_shift = false;
                UpdateKeyLabels();
            }
            if (m_urlMode)
            {
                m_urlEdit += text;
                UpdateUrlText();
            }
            else
            {
                m_web.InsertText(text);
            }
        }

        private void Backspace()
        {
            if (m_urlMode)
            {
                if (m_urlEdit.Length > 0)
                {
                    m_urlEdit = m_urlEdit.Substring(0, m_urlEdit.Length - 1);
                    UpdateUrlText();
                }
            }
            else
            {
                m_web.Key(WebViewBridge.KeyDelete);
            }
        }

        private void Enter()
        {
            if (m_urlMode)
            {
                var text = m_urlEdit;
                SetUrlMode(false);
                ShowKeyboard(false);
                Navigate(text);
            }
            else
            {
                m_web.Key(WebViewBridge.KeyEnter);
            }
        }

        private void HideKeyboard()
        {
            if (m_urlMode)
            {
                SetUrlMode(false);
            }
            else
            {
                m_web.BlurInput();
            }
            ShowKeyboard(false);
        }

        private void UpdateKeyLabels()
        {
            foreach (var (button, letter, symbol) in m_charKeys)
            {
                var text = m_symbols ? symbol : letter;
                button.Label.text = m_shift && !m_symbols ? text.ToUpperInvariant() : text;
            }
            if (m_symbolsKey != null)
            {
                m_symbolsKey.Label.text = m_symbols ? "ABC" : "?123";
            }
        }

        private void SetMessage(string text)
        {
            m_message.enabled = !string.IsNullOrEmpty(text);
            m_messageBackground.enabled = m_message.enabled;
            m_message.text = text ?? "";
            m_messageUntil = Time.time + 4f;
        }

        private static void SetEnabled(Button button, bool enabled)
        {
            button.Enabled = enabled;
            button.Label.color = enabled ? Color.white : DisabledText;
        }

        // ---- Building the UI ----

        private void BuildPanel()
        {
            var width = PageWidth + Padding * 2f;
            var height = Padding * 3f + ToolbarHeight + PageHeight;
            var rounded = ObjectInfoView.RoundedRectSprite(28);
            var canvasGo = new GameObject("BrowserPanel", typeof(RectTransform), typeof(Canvas), typeof(Image));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            m_root = (RectTransform)canvasGo.transform;
            m_root.sizeDelta = new Vector2(width, height);
            var background = canvasGo.GetComponent<Image>();
            background.sprite = rounded;
            background.type = Image.Type.Sliced;
            background.color = PanelColor;
            background.raycastTarget = false;

            // Page (the texture's row 0 is the top of the page, so the image is flipped vertically)
            var pageGo = new GameObject("Page", typeof(RectTransform), typeof(RawImage));
            pageGo.transform.SetParent(m_root, false);
            m_pageRect = (RectTransform)pageGo.transform;
            m_pageRect.sizeDelta = new Vector2(PageWidth, PageHeight);
            m_pageRect.anchoredPosition = new Vector2(0f, -height * 0.5f + Padding + PageHeight * 0.5f);
            var page = pageGo.GetComponent<RawImage>();
            page.uvRect = new Rect(0f, 1f, 1f, -1f);
            page.raycastTarget = false;

            // Loading progress along the top edge of the page
            m_progress = CreateImage("Progress", m_pageRect, AccentColor);
            var progressRect = m_progress.rectTransform;
            progressRect.anchorMin = progressRect.anchorMax = new Vector2(0f, 1f);
            progressRect.pivot = new Vector2(0f, 1f);
            progressRect.anchoredPosition = Vector2.zero;
            progressRect.sizeDelta = new Vector2(0f, 4f);

            // Notice at the bottom of the page
            var messageGo = new GameObject("Message", typeof(RectTransform), typeof(Image));
            messageGo.transform.SetParent(m_pageRect, false);
            var messageRect = (RectTransform)messageGo.transform;
            messageRect.anchorMin = messageRect.anchorMax = new Vector2(0.5f, 0f);
            messageRect.anchoredPosition = new Vector2(0f, 60f);
            messageRect.sizeDelta = new Vector2(PageWidth - 200f, 64f);
            m_messageBackground = messageGo.GetComponent<Image>();
            m_messageBackground.sprite = rounded;
            m_messageBackground.type = Image.Type.Sliced;
            m_messageBackground.color = new Color(0f, 0f, 0f, 0.8f);
            m_message = CreateText("Text", messageRect, 26, Color.white, TextAnchor.MiddleCenter);
            Stretch(m_message.rectTransform);
            SetMessage(null);

            // Toolbar
            var toolbarY = height * 0.5f - Padding - ToolbarHeight * 0.5f;
            var x = -PageWidth * 0.5f;
            Button AddToolbarButton(string label, float buttonWidth, Action click)
            {
                var button = CreateButton(m_root, label, new Vector2(x + buttonWidth * 0.5f, toolbarY), new Vector2(buttonWidth, ToolbarHeight), click);
                x += buttonWidth + ButtonGap;
                return button;
            }
            m_backButton = AddToolbarButton("Back", 100f, () => m_web.GoBack());
            m_forwardButton = AddToolbarButton("Forward", 120f, () => m_web.GoForward());
            _ = AddToolbarButton("Reload", 110f, () => m_web.Reload());
            const float rightButtons = 110f + 120f + 200f + 100f + ButtonGap * 4f;
            var urlWidth = PageWidth * 0.5f - x - rightButtons;
            m_urlButton = AddToolbarButton("", urlWidth, EditUrl);
            m_urlButton.Label.alignment = TextAnchor.MiddleLeft;
            m_urlButton.Label.rectTransform.offsetMin = new Vector2(18f, 0f);
            m_urlButton.Label.rectTransform.offsetMax = new Vector2(-12f, 0f);
            // Long addresses are cut at the edge of the field instead of running over the next buttons
            m_urlButton.Rect.gameObject.AddComponent<RectMask2D>();
            _ = AddToolbarButton("Keys", 110f, () =>
            {
                CloseSystemKeyboard();
                ShowKeyboard(!m_keyboardVisible);
            });
            _ = AddToolbarButton("Recenter", 120f, () =>
            {
                m_scale = 1f;
                Recenter();
            });
            _ = AddToolbarButton("Quest Browser", 200f, () =>
            {
                var url = m_url;
                Close();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    Application.OpenURL(uri.AbsoluteUri);
                }
            });
            _ = AddToolbarButton("Close", 100f, Close);

            // Grab bar under the window (moves it), like Quest windows
            var grabGo = new GameObject("GrabArea", typeof(RectTransform));
            grabGo.transform.SetParent(m_root, false);
            m_grabArea = (RectTransform)grabGo.transform;
            m_grabArea.sizeDelta = new Vector2(420f, 64f);
            m_grabArea.anchoredPosition = new Vector2(0f, -height * 0.5f - GrabBarOffset);
            m_grabBar = CreateImage("Bar", m_grabArea, new Color(1f, 1f, 1f, 0.45f));
            m_grabBar.sprite = rounded;
            m_grabBar.type = Image.Type.Sliced;
            m_grabBar.pixelsPerUnitMultiplier = 3f;
            m_grabBar.rectTransform.sizeDelta = new Vector2(260f, 16f);

            // Resize handles at the bottom corners
            m_resizeLeft = CreateCorner("ResizeLeft", new Vector2(-width * 0.5f - 6f, -height * 0.5f - 6f), rounded);
            m_resizeRight = CreateCorner("ResizeRight", new Vector2(width * 0.5f + 6f, -height * 0.5f - 6f), rounded);

            // Cursor: dot with a ring that closes while pinching
            var cursorGo = new GameObject("Cursor", typeof(RectTransform));
            cursorGo.transform.SetParent(m_root, false);
            m_cursor = (RectTransform)cursorGo.transform;
            m_cursorRing = CreateImage("Ring", m_cursor, Color.white);
            m_cursorRing.sprite = RingSprite();
            m_cursorRing.rectTransform.sizeDelta = new Vector2(40f, 40f);
            m_cursorDot = CreateImage("Dot", m_cursor, Color.white);
            m_cursorDot.sprite = rounded;
            m_cursorDot.type = Image.Type.Sliced;
            m_cursorDot.pixelsPerUnitMultiplier = 4f;
            m_cursorDot.rectTransform.sizeDelta = new Vector2(12f, 12f);
            cursorGo.SetActive(false);
        }

        private Image CreateCorner(string name, Vector2 position, Sprite sprite)
        {
            var corner = CreateImage(name, m_root, new Color(1f, 1f, 1f, 0.15f));
            corner.sprite = sprite;
            corner.type = Image.Type.Sliced;
            corner.pixelsPerUnitMultiplier = 2f;
            corner.rectTransform.sizeDelta = new Vector2(64f, 64f);
            corner.rectTransform.anchoredPosition = position;
            return corner;
        }

        private void BuildKeyboard()
        {
            var width = KeyUnit * 10f + Padding * 2f;
            var height = KeyHeight * 5f + KeyGap * 4f + Padding * 2f;
            var keyboardGo = new GameObject("Keyboard", typeof(RectTransform), typeof(Image));
            keyboardGo.transform.SetParent(m_root, false);
            m_keyboard = (RectTransform)keyboardGo.transform;
            m_keyboard.sizeDelta = new Vector2(width, height);
            m_keyboard.anchoredPosition = new Vector2(0f, -m_root.sizeDelta.y * 0.5f - GrabBarOffset * 2f - height * 0.5f);
            var background = keyboardGo.GetComponent<Image>();
            background.sprite = ObjectInfoView.RoundedRectSprite(28);
            background.type = Image.Type.Sliced;
            background.color = PanelColor;
            background.raycastTarget = false;

            // Rows: (letter, symbol) pairs; special keys are added by hand
            string[][] letters =
            {
                new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
                new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
                new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l" },
                new[] { "z", "x", "c", "v", "b", "n", "m" }
            };
            string[][] symbols =
            {
                new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
                new[] { "@", "#", "$", "%", "&", "*", "-", "+", "(", ")" },
                new[] { "!", "\"", "'", ":", ";", "/", "?", "_", "=" },
                new[] { ",", ".", "~", "|", "\\", "<", ">" }
            };

            var top = height * 0.5f - Padding - KeyHeight * 0.5f;
            float RowY(int row) => top - row * (KeyHeight + KeyGap);
            var left = -KeyUnit * 5f;
            Button AddKey(string label, int row, float unitX, float units, Action click)
            {
                var size = new Vector2(KeyUnit * units - KeyGap, KeyHeight);
                var position = new Vector2(left + KeyUnit * (unitX + units * 0.5f), RowY(row));
                return CreateButton(m_keyboard, label, position, size, click);
            }

            for (var row = 0; row < 4; row++)
            {
                var offset = row switch { 2 => 0.5f, 3 => 1.5f, _ => 0f };
                for (var i = 0; i < letters[row].Length; i++)
                {
                    var letter = letters[row][i];
                    var symbol = symbols[row][i];
                    Button key = null;
                    key = AddKey(letter, row, offset + i, 1f, () => Type(key.Label.text));
                    m_charKeys.Add((key, letter, symbol));
                }
            }
            m_shiftKey = AddKey("Shift", 3, 0f, 1.5f, () =>
            {
                m_shift = !m_shift;
                UpdateKeyLabels();
            });
            _ = AddKey("Del", 3, 8.5f, 1.5f, Backspace);
            m_symbolsKey = AddKey("?123", 4, 0f, 1.5f, () =>
            {
                m_symbols = !m_symbols;
                m_shift = false;
                UpdateKeyLabels();
            });
            _ = AddKey("Hide", 4, 1.5f, 1.5f, HideKeyboard);
            _ = AddKey("space", 4, 3f, 4f, () => Type(" "));
            _ = AddKey(".", 4, 7f, 1f, () => Type("."));
            _ = AddKey("Enter", 4, 8f, 2f, Enter);

            m_keyboard.gameObject.SetActive(false);
        }

        private Button CreateButton(RectTransform parent, string label, Vector2 position, Vector2 size, Action click)
        {
            var go = new GameObject(label.Length > 0 ? label : "Button", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var background = go.GetComponent<Image>();
            background.sprite = ObjectInfoView.RoundedRectSprite(28);
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 2f;
            background.color = ButtonColor;
            background.raycastTarget = false;
            var text = CreateText("Label", rect, 26, Color.white, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            text.text = label;
            var button = new Button { Rect = rect, Background = background, Label = text, Click = click };
            m_buttons.Add(button);
            return button;
        }

        private static Text CreateText(string name, RectTransform parent, int size, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// One Euro filter (Casiez et al. 2012): strong smoothing when the pointer moves slowly (hand tremor),
        /// little lag when it moves fast. Used for dragging the page.
        /// </summary>
        private class OneEuroFilter
        {
            private const float Beta = 0.004f;
            private const float DerivativeCutoff = 1f;
            private float m_minCutoff = 1f;
            private Vector2 m_value;
            private Vector2 m_derivative;
            private float m_time;

            public void Reset(Vector2 value, float minCutoff)
            {
                m_value = value;
                m_derivative = Vector2.zero;
                m_time = Time.time;
                m_minCutoff = minCutoff;
            }

            public Vector2 Filter(Vector2 value, float time)
            {
                var dt = Mathf.Max(1e-4f, time - m_time);
                m_time = time;
                var derivative = (value - m_value) / dt;
                m_derivative = Vector2.Lerp(m_derivative, derivative, Alpha(DerivativeCutoff, dt));
                var cutoff = m_minCutoff + Beta * m_derivative.magnitude;
                m_value = Vector2.Lerp(m_value, value, Alpha(cutoff, dt));
                return m_value;
            }

            private static float Alpha(float cutoff, float dt)
            {
                var tau = 1f / (2f * Mathf.PI * cutoff);
                return 1f / (1f + tau / dt);
            }
        }

        /// <summary>Thin anti-aliased ring for the cursor.</summary>
        private static Sprite RingSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            const float radius = size * 0.5f - 1.5f;
            const float thickness = 3.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                    var alpha = Mathf.Clamp01(thickness * 0.5f + 0.5f - Mathf.Abs(d - (radius - thickness * 0.5f)));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
