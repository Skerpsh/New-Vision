// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// "What is this?": look at anything with the reticle in the middle of the view for a moment (or pinch / pull a trigger)
    /// and Gemini tells what it is, using the headset camera image and Google Search. Works for any object,
    /// not only the classes the on-device model knows; the on-device guess is passed along as a hint.
    /// </summary>
    public class ObjectInfoLookup : MonoBehaviour
    {
        private const string SystemPrompt =
            "You help a person wearing mixed reality glasses. The photo comes from the headset camera. " +
            "The person is looking at the object marked by the small red circle. " +
            "Identify that object as specifically as you can (brand, model, species, breed, landmark, artwork, etc.). " +
            "Use Google Search when it helps to give accurate, current facts. " +
            "Answer in English, plain text, no markdown except the section headings described below. " +
            "Line 1: the name of the object, at most 6 words. " +
            "Line 2: one sentence saying what it is. " +
            "Then 2 to 4 sections that fit this object best, chosen from: Key facts, Specs, Price, How to use, Tips, Care, " +
            "Safety, History, Fun fact. Each section starts with a line \"## <section name>\" followed by 1 to 3 lines, " +
            "each starting with \"• \". Prefer concrete, specific facts (numbers, materials, model or year, prices with currency) " +
            "over generic statements, and only what is useful to someone looking at it right now. 120 to 180 words in total. " +
            "If you are not sure what it is, give the most likely answer and say how sure you are in line 2.";

        [Header("Gemini")]
        [SerializeField] private string m_model = "gemini-3.5-flash-lite";
        [Tooltip("Used when the main model is overloaded.")]
        [SerializeField] private string m_fallbackModel = "gemini-3.8-flash";
        [Tooltip("Needs a paid Gemini API plan; without one it switches itself off after the first try.")]
        [SerializeField] private bool m_useGoogleSearch = true;

        [Header("Dwell")]
        [Tooltip("Seconds of looking at the same spot that start a lookup.")]
        [SerializeField] private float m_dwellSeconds = 2f;
        [Tooltip("The ring starts filling only after this many seconds, so normal looking around shows nothing.")]
        [SerializeField] private float m_ringDelay = 0.5f;
        [Tooltip("How far (degrees) the head may drift and still count as looking at the same spot.")]
        [SerializeField] private float m_dwellToleranceDeg = 3f;

        [Header("Capture")]
        [SerializeField] private int m_imageSize = 512;
        [Tooltip("Part of the camera image height sent when the object size is unknown.")]
        [SerializeField] private float m_defaultCropFraction = 0.45f;
        [Tooltip("Distance used for the reticle and the image crop when the depth sensor sees nothing (sky, far away).")]
        [SerializeField] private float m_noDepthDistance = 15f;

        private PassthroughCameraAccess m_camera;
        private SentisInferenceUiManager m_uiInference;
        private EnvironmentRayCastSampleManager m_raycast;
        private Func<bool> m_isPaused;
        private Transform m_head;
        private ObjectInfoView m_view;
        private readonly GeminiClient m_gemini = new();

        private Vector3 m_dwellDirection;
        private float m_dwellTime;
        private bool m_armed;
        private float m_nextLookupTime;
        private bool m_busy;
        private int m_lookupId;
        private Coroutine m_lookup;

        // QR codes / barcodes and the action of the open card (A / index pinch)
        private CodeScanner m_scanner;
        private BrowserPanel m_browser;
        private Coroutine m_preview;
        private Texture2D m_previewImage;
        private Action m_primaryAction;
        private float m_panelOpenTime;
        private Ray m_lastRay;
        private Vector3 m_lastTarget;

        /// <summary>How long the last full Gemini answer took, seconds; 0 before the first one (wrist panel > SYS).</summary>
        public static float LastAnswerSeconds { get; private set; }
        private float m_lookupStartTime;

        private const string DwellKey = "NewVision.Dwell";
        private const string QrKey = "NewVision.Qr";

        /// <summary>Looking at the same spot for a moment asks Gemini (wrist panel > ASK); a pinch always does.</summary>
        public static bool DwellEnabled
        {
            get => PlayerPrefs.GetInt(DwellKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(DwellKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>QR codes and barcodes at the reticle are read (wrist panel > QR).</summary>
        public static bool QrEnabled
        {
            get => PlayerPrefs.GetInt(QrKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(QrKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public void Init(PassthroughCameraAccess cameraAccess, SentisInferenceUiManager uiInference, Func<bool> isPaused)
        {
            m_camera = cameraAccess;
            m_uiInference = uiInference;
            m_isPaused = isPaused;
            m_raycast = FindAnyObjectByType<EnvironmentRayCastSampleManager>();
            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            m_head = cameraRig != null ? cameraRig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : null;

            m_gemini.Model = m_model;
            m_gemini.FallbackModel = m_fallbackModel;
            m_gemini.UseGoogleSearch = m_useGoogleSearch;
            Debug.Log($"[NewVision] object info: model {m_model}, key {(m_gemini.HasKey ? "found" : "MISSING")} ({GeminiClient.KeyFilePath})");

            var viewGo = new GameObject("ObjectInfoView");
            viewGo.transform.SetParent(transform, false);
            m_view = viewGo.AddComponent<ObjectInfoView>();
            m_view.Init(m_head);

            m_scanner = gameObject.AddComponent<CodeScanner>();
            m_scanner.Init(m_camera);

            var browserGo = new GameObject("Browser");
            browserGo.transform.SetParent(transform, false);
            m_browser = browserGo.AddComponent<BrowserPanel>();
            m_browser.Init(m_head, cameraRig != null ? cameraRig.trackingSpace : null);
        }

        private void OnDestroy()
        {
            m_gemini.Cancel();
            DestroyPreviewImage();
        }

        private void Update()
        {
            if (m_view == null || m_head == null)
            {
                return;
            }

            // The browser has the input to itself
            if (BrowserPanel.IsOpen)
            {
                m_view.SetReticleVisible(false);
                m_dwellTime = 0f;
                m_armed = false;
                return;
            }

            // While the info card is open, the reticle is hidden and no new lookups start; a middle finger pinch closes it,
            // an index pinch runs the card's action (open link, connect to Wi-Fi, ...). The wrist panel has the hands meanwhile.
            if (m_view.PanelVisible)
            {
                if (!WristPanel.IsVisible && InputManager.IsMiddleFingerPinchStarted())
                {
                    ClosePanel();
                }
                else if (m_primaryAction != null && Time.time > m_panelOpenTime + 0.4f && !WristPanel.IsVisible &&
                         InputManager.IsIndexPinchStarted())
                {
                    m_primaryAction();
                }
                m_view.SetReticleVisible(false);
                m_dwellTime = 0f;
                return;
            }

            if (!m_camera.IsPlaying || m_isPaused())
            {
                m_view.SetReticleVisible(false);
                m_dwellTime = 0f;
                m_armed = false;
                return;
            }
            m_view.SetReticleVisible(true);

            var ray = new Ray(m_head.position, m_head.forward);
            var hit = m_raycast != null ? m_raycast.Raycast(ray) : null;
            var target = hit ?? ray.GetPoint(m_noDepthDistance);
            m_lastRay = ray;
            m_lastTarget = target;

            // A QR code / barcode at the reticle takes priority over asking Gemini about the object
            CodeContent code = null;
            if (QrEnabled)
            {
                m_scanner.Scan(target);
                code = m_scanner.Current;
            }
            m_view.SetChip(code?.ChipText);

            // Dwell: looking at the same spot. After a lookup the head has to move away first to start the next one.
            if (Vector3.Angle(ray.direction, m_dwellDirection) > m_dwellToleranceDeg)
            {
                m_dwellDirection = ray.direction;
                m_dwellTime = 0f;
                m_armed = true;
            }
            else
            {
                m_dwellTime += Time.deltaTime;
            }
            // Looking at the laptop is for stick gestures, not a question to Gemini (a pinch still asks)
            var canDwell = DwellEnabled && m_armed && !m_busy && !LaptopGestures.LookingAtLaptop && !WristPanel.IsVisible;
            var fill = canDwell ? Mathf.Clamp01((m_dwellTime - m_ringDelay) / Mathf.Max(0.01f, m_dwellSeconds - m_ringDelay)) : 0f;
            m_view.UpdateReticle(Vector3.Distance(ray.origin, target), fill, m_busy);

            // A thumb tap microgesture on the laptop can look like a pinch: hand pinches don't ask Gemini there
            var handPinch = InputManager.IsIndexPinchStarted() && !LaptopGestures.LookingAtLaptop && !WristPanel.IsVisible;
            var pressed = handPinch;
            if (pressed || (canDwell && fill >= 1f))
            {
                m_armed = false;
                if (code != null)
                {
                    OpenCode(code);
                }
                else
                {
                    StartLookup(ray, target);
                }
            }
        }

        /// <summary>Closes the card and drops the answer still coming in. The head has to move before the next dwell lookup.</summary>
        private void ClosePanel()
        {
            StopRunning();
            m_armed = false;
            m_dwellTime = 0f;
            m_scanner.Clear();
            m_view.HidePanel();
        }

        /// <summary>Stops the Gemini request and the link preview still running for the card.</summary>
        private void StopRunning()
        {
            m_lookupId++;
            m_gemini.Cancel();
            if (m_lookup != null)
            {
                StopCoroutine(m_lookup);
                m_lookup = null;
            }
            if (m_preview != null)
            {
                StopCoroutine(m_preview);
                m_preview = null;
            }
            m_busy = false;
            m_primaryAction = null;
        }

        /// <summary>Opens a fresh card: no picture, no action, default footer.</summary>
        private void OpenCard()
        {
            StopRunning();
            m_view.SetImage(null);
            DestroyPreviewImage();
            m_view.SetFooter(null);
            m_view.ShowPanel();
            m_panelOpenTime = Time.time;
        }

        private void DestroyPreviewImage()
        {
            if (m_previewImage != null)
            {
                Destroy(m_previewImage);
                m_previewImage = null;
            }
        }

        private void SetAction(string label, Action action)
        {
            m_primaryAction = action;
            m_view.SetFooter(action != null ? $"PINCH // {label.ToUpperInvariant()}      MIDDLE // CLOSE" : null);
        }

        private void OpenCode(CodeContent code)
        {
            if (Time.time < m_nextLookupTime)
            {
                return;
            }
            m_nextLookupTime = Time.time + 1f;
            OpenCard();

            switch (code.Kind)
            {
                case CodeContent.ContentKind.Url:
                    ShowLink(code.Url, null, null);
                    m_preview = StartCoroutine(PreviewLink(m_lookupId, code.Url));
                    break;

                case CodeContent.ContentKind.Wifi:
                    ShowWifi(code, null);
                    break;

                case CodeContent.ContentKind.Product:
                    m_view.SetContent($"Barcode {code.Raw}", $"Product code ({code.Format.Replace('_', '-')})", null);
                    var ray = m_lastRay;
                    var target = m_lastTarget;
                    SetAction("ask Gemini what it is", () =>
                    {
                        m_nextLookupTime = 0f;
                        StartLookup(ray, target, $"The photo shows a product with the barcode {code.Format} {code.Raw}. " +
                                                 "Identify the product by its barcode and the photo.");
                    });
                    break;

                default:
                    var title = code.Kind switch
                    {
                        CodeContent.ContentKind.Phone => "Phone number",
                        CodeContent.ContentKind.Email => "E-mail",
                        CodeContent.ContentKind.Contact => "Contact card",
                        _ => code.Format == "QR_CODE" ? "QR code" : $"Code ({code.Format})"
                    };
                    var text = code.Raw.Length > 600 ? code.Raw.Substring(0, 600) + "..." : code.Raw;
                    m_view.SetContent(title, null, ObjectInfoView.Escape(text));
                    break;
            }
        }

        /// <summary>Link card: the domain big, the full address, warnings; the preview fills in title, text and picture.</summary>
        private void ShowLink(Uri url, Uri finalUrl, LinkPreview.Data preview)
        {
            var target = finalUrl ?? url;
            var body = new StringBuilder();
            foreach (var warning in CodeContent.LinkWarnings(url, finalUrl))
            {
                _ = body.Append(ObjectInfoView.Warning(warning)).Append('\n');
            }
            var address = target.ToString();
            _ = body.Append(ObjectInfoView.Escape(address.Length > 160 ? address.Substring(0, 160) + "..." : address));

            string title;
            string summary;
            if (preview == null)
            {
                title = target.Host;
                summary = "Loading preview...";
            }
            else
            {
                title = preview.Title ?? target.Host;
                summary = preview.Description ?? preview.Error;
                var site = preview.SiteName != null && preview.SiteName != target.Host ? $"{preview.SiteName}  ·  " : "";
                _ = body.Insert(0, ObjectInfoView.Heading(site + target.Host) + "\n");
            }
            m_view.SetContent(title, summary, body.ToString());
            SetAction("open", () =>
            {
                ClosePanel();
                m_browser.Open(target.AbsoluteUri);
            });
        }

        private IEnumerator PreviewLink(int id, Uri url)
        {
            Uri finalUrl = null;
            yield return LinkPreview.Fetch(url,
                resolved =>
                {
                    finalUrl = resolved;
                    if (id == m_lookupId)
                    {
                        ShowLink(url, resolved, null);
                    }
                },
                data =>
                {
                    if (id != m_lookupId)
                    {
                        if (data.Image != null)
                        {
                            Destroy(data.Image);
                        }
                        return;
                    }
                    DestroyPreviewImage();
                    m_previewImage = data.Image;
                    m_view.SetImage(data.Image);
                    ShowLink(url, finalUrl, data);
                });
            if (id == m_lookupId)
            {
                m_preview = null;
            }
        }

        private void ShowWifi(CodeContent wifi, string status)
        {
            var body = new StringBuilder();
            _ = body.Append("Security:  ").Append(wifi.WifiSecurity.Length > 0 ? ObjectInfoView.Escape(wifi.WifiSecurity) : "open network");
            if (!string.IsNullOrEmpty(wifi.WifiPassword))
            {
                _ = body.Append("\nPassword:  ").Append(ObjectInfoView.Escape(wifi.WifiPassword));
            }
            if (wifi.WifiHidden)
            {
                _ = body.Append("\nHidden network");
            }
            if (status != null)
            {
                _ = body.Append("\n\n").Append(ObjectInfoView.Heading(status));
            }
            m_view.SetContent(wifi.WifiSsid.Length > 0 ? wifi.WifiSsid : "Wi-Fi network", "Wi-Fi network", body.ToString());
            if (status == null)
            {
                SetAction("connect", () =>
                {
                    var suggested = WifiConnector.Suggest(wifi, out var message);
                    ShowWifi(wifi, message);
                    if (!suggested)
                    {
                        // Nothing else to try from the app: straight to Settings with the password on the clipboard
                        ShowWifi(wifi, WifiConnector.OpenSettings(wifi));
                    }
                });
            }
            else
            {
                SetAction("open Wi-Fi settings", () => ShowWifi(wifi, WifiConnector.OpenSettings(wifi)));
            }
        }

        // No answer cache on purpose: the same spot can hold a different object a moment later (one is put down, another picked up)
        private void StartLookup(Ray ray, Vector3 target, string extraPrompt = null)
        {
            if (Time.time < m_nextLookupTime)
            {
                return;
            }
            m_nextLookupTime = Time.time + 1f;
            OpenCard();

            if (!m_gemini.HasKey)
            {
                m_view.SetText("Gemini API key missing", $"Put your key into {GeminiClient.KeyFileName} on the headset (see README).");
                return;
            }

            var box = FindBoxAt(ray);
            var hint = box != null ? $"{box.ClassName} ({box.Score * 100f:0}%)" : null;
            m_view.SetWaiting(box != null ? $"{box.ClassName}?" : "Looking", "Asking Gemini");
            m_lookupStartTime = Time.time;
            m_lookup = StartCoroutine(Lookup(m_lookupId, target, box, hint, extraPrompt));
        }

        private IEnumerator Lookup(int id, Vector3 target, SentisInferenceUiManager.BoundingBoxData box, string hint, string extraPrompt)
        {
            m_busy = true;
            byte[] jpeg = null;
            yield return Capture(target, box, result => jpeg = result);
            m_lookup = null;
            if (id != m_lookupId)
            {
                yield break;
            }
            if (jpeg == null)
            {
                m_busy = false;
                m_view.SetText("No camera image", "Try again in a moment.");
                yield break;
            }

            var prompt = "What is the object under the red circle?";
            if (hint != null)
            {
                prompt += $" An on-device detector guesses: {hint}.";
            }
            if (extraPrompt != null)
            {
                prompt += " " + extraPrompt;
            }
            m_gemini.Ask(SystemPrompt, prompt, jpeg,
                text =>
                {
                    if (id == m_lookupId)
                    {
                        ShowAnswer(text, true);
                    }
                },
                text =>
                {
                    if (id != m_lookupId)
                    {
                        return;
                    }
                    m_busy = false;
                    LastAnswerSeconds = Time.time - m_lookupStartTime;
                    ShowAnswer(text, false);
                },
                error =>
                {
                    if (id != m_lookupId)
                    {
                        return;
                    }
                    m_busy = false;
                    m_view.SetText("No answer", error.Length > 160 ? error.Substring(0, 160) + "..." : error);
                });
        }

        /// <summary>
        /// Line 1 becomes the title, line 2 the summary, then "## Heading" sections with bullets.
        /// Copes with stray markdown and with answers that don't follow the format.
        /// </summary>
        private void ShowAnswer(string text, bool streaming)
        {
            // '<' would start a rich text tag in the body
            var lines = text.Replace("**", "").Replace("__", "").Replace('<', '‹').Replace('>', '›').Split('\n');
            string title = null;
            string summary = null;
            var body = new StringBuilder();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                var isHeading = line.StartsWith("#");
                line = line.TrimStart('#').Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (title == null)
                {
                    title = line;
                    continue;
                }
                if (isHeading)
                {
                    if (body.Length > 0)
                    {
                        _ = body.Append("\n\n");
                    }
                    _ = body.Append(ObjectInfoView.Heading(line.TrimEnd(':')));
                    continue;
                }
                if (line.StartsWith("* ") || line.StartsWith("- "))
                {
                    line = "• " + line.Substring(2);
                }
                if (summary == null && body.Length == 0 && !line.StartsWith("•"))
                {
                    summary = line;
                    continue;
                }
                if (body.Length > 0)
                {
                    _ = body.Append('\n');
                }
                _ = body.Append(line);
            }
            m_view.SetContent(title ?? "...", summary, body.Length > 0 ? body.ToString() : streaming ? "..." : "");
        }

        /// <summary>Smallest confirmed detection box the view ray goes through, or null.</summary>
        private SentisInferenceUiManager.BoundingBoxData FindBoxAt(Ray ray)
        {
            SentisInferenceUiManager.BoundingBoxData best = null;
            var bestArea = float.MaxValue;
            foreach (var box in m_uiInference.m_boxDrawn)
            {
                if (!box.Confirmed)
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
                if (Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && size.x * size.y < bestArea)
                {
                    best = box;
                    bestArea = size.x * size.y;
                }
            }
            return best;
        }

        /// <summary>
        /// Cuts a square around the looked-at point out of the camera image (sized to the detected object when there is one),
        /// marks the point with a red circle and encodes it as JPEG.
        /// </summary>
        private IEnumerator Capture(Vector3 target, SentisInferenceUiManager.BoundingBoxData box, Action<byte[]> done)
        {
            var texture = m_camera.GetTexture();
            if (texture == null)
            {
                done(null);
                yield break;
            }
            var pose = m_camera.GetCameraPose();
            var aspect = (float)texture.width / texture.height;

            // Where the looked-at point is in the camera image (the camera sits a bit off the eyes)
            var inFront = (Quaternion.Inverse(pose.rotation) * (target - pose.position)).z > 0.05f;
            var point = inFront ? m_camera.WorldToViewportPoint(target, pose) : new Vector2(0.5f, 0.5f);
            point = new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));

            // Crop size as a fraction of the image height
            var cropHeight = m_defaultCropFraction;
            if (box != null)
            {
                var rect = box.BoxRectTransform;
                var halfSize = Vector2.Scale(rect.sizeDelta, rect.lossyScale) * 0.5f;
                var top = m_camera.WorldToViewportPoint(rect.position + rect.up * halfSize.y, pose);
                var bottom = m_camera.WorldToViewportPoint(rect.position - rect.up * halfSize.y, pose);
                var left = m_camera.WorldToViewportPoint(rect.position - rect.right * halfSize.x, pose);
                var right = m_camera.WorldToViewportPoint(rect.position + rect.right * halfSize.x, pose);
                var objectSize = Mathf.Max(Mathf.Abs(top.y - bottom.y), Mathf.Abs(right.x - left.x) * aspect);
                cropHeight = Mathf.Clamp(objectSize * 1.4f, 0.2f, 1f);
            }
            var cropWidth = Mathf.Min(1f, cropHeight / aspect);
            var x0 = Mathf.Clamp(point.x - cropWidth * 0.5f, 0f, 1f - cropWidth);
            var y0 = Mathf.Clamp(point.y - cropHeight * 0.5f, 0f, 1f - cropHeight);

            var size = m_imageSize;
            var renderTexture = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, renderTexture, new Vector2(cropWidth, cropHeight), new Vector2(x0, y0));
            var request = AsyncGPUReadback.Request(renderTexture, 0, TextureFormat.RGBA32);
            while (!request.done)
            {
                yield return null;
            }
            RenderTexture.ReleaseTemporary(renderTexture);
            if (request.hasError)
            {
                done(null);
                yield break;
            }

            var pixels = request.GetData<Color32>().ToArray();
            DrawCircle(pixels, size, (point.x - x0) / cropWidth * size, (point.y - y0) / cropHeight * size);
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            image.SetPixels32(pixels);
            var jpeg = image.EncodeToJPG(85);
            Destroy(image);

            // Last image sent, handy to check what Gemini saw: adb pull /sdcard/Android/data/<package>/files/last_lookup.jpg
            try
            {
                File.WriteAllBytes(Path.Combine(Application.persistentDataPath, "last_lookup.jpg"), jpeg);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] can't save last_lookup.jpg: {e.Message}");
            }
            done(jpeg);
        }

        /// <summary>Red ring with a dark edge, visible on any background.</summary>
        private static void DrawCircle(Color32[] pixels, int size, float cx, float cy)
        {
            const float radius = 14f;
            const float halfWidth = 2.5f;
            const float edge = 1.5f;
            var red = new Color32(255, 30, 30, 255);
            var dark = new Color32(0, 0, 0, 255);
            var reach = Mathf.CeilToInt(radius + halfWidth + edge + 1f);
            for (var y = Mathf.Max(0, (int)cy - reach); y < Mathf.Min(size, (int)cy + reach); y++)
            {
                for (var x = Mathf.Max(0, (int)cx - reach); x < Mathf.Min(size, (int)cx + reach); x++)
                {
                    var d = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius);
                    if (d <= halfWidth)
                    {
                        pixels[y * size + x] = red;
                    }
                    else if (d <= halfWidth + edge)
                    {
                        pixels[y * size + x] = dark;
                    }
                }
            }
        }
    }
}
