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
            "Answer in English, plain text, no markdown. " +
            "Line 1: the name of the object, at most 6 words. " +
            "Then 2 to 4 lines, each starting with \"• \", with the most useful facts for someone looking at it right now " +
            "(what it is or does, notable details, typical price, tips). At most 70 words in total. " +
            "If you are not sure, give the most likely answer and say how sure you are.";

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

        [Header("Answers")]
        [Tooltip("The panel hides after looking this many degrees away from it for a few seconds.")]
        [SerializeField] private float m_hidePanelAngle = 50f;

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
        private float m_lookAwayTime;
        private float m_nextLookupTime;
        private bool m_busy;
        private int m_lookupId;
        private Coroutine m_lookup;

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
        }

        private void OnDestroy() => m_gemini.Cancel();

        private void Update()
        {
            if (m_view == null || m_head == null)
            {
                return;
            }

            UpdatePanelAutoHide();

            if (!m_camera.IsPlaying || m_isPaused() || ClassFilterMenu.IsOpen)
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
            // Reading the answer panel is not looking at an object
            var canDwell = m_armed && !m_busy && !m_view.IsLookedAt(ray);
            var fill = canDwell ? Mathf.Clamp01((m_dwellTime - m_ringDelay) / Mathf.Max(0.01f, m_dwellSeconds - m_ringDelay)) : 0f;
            m_view.UpdateReticle(Vector3.Distance(ray.origin, target), fill, m_busy);

            var pressed = !ClassFilterMenu.BlocksInput && (InputManager.IsIndexPinchStarted() ||
                OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger, OVRInput.Controller.Touch) ||
                OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger, OVRInput.Controller.Touch));
            if (pressed || (canDwell && fill >= 1f))
            {
                m_armed = false;
                StartLookup(ray, target);
            }
        }

        private void UpdatePanelAutoHide()
        {
            if (!m_view.PanelVisible)
            {
                return;
            }
            var lookingAway = Vector3.Angle(m_head.forward, m_view.PanelPosition - m_head.position) > m_hidePanelAngle;
            m_lookAwayTime = lookingAway ? m_lookAwayTime + Time.deltaTime : 0f;
            if (m_lookAwayTime > 2.5f)
            {
                m_view.HidePanel();
            }
        }

        // No answer cache on purpose: the same spot can hold a different object a moment later (one is put down, another picked up)
        private void StartLookup(Ray ray, Vector3 target)
        {
            if (Time.time < m_nextLookupTime)
            {
                return;
            }
            m_nextLookupTime = Time.time + 1f;
            m_lookAwayTime = 0f;

            m_lookupId++;
            m_gemini.Cancel();
            if (m_lookup != null)
            {
                StopCoroutine(m_lookup);
                m_lookup = null;
            }
            m_busy = false;
            m_view.ShowPanel(target);

            if (!m_gemini.HasKey)
            {
                m_view.SetText("Gemini API key missing", $"Put your key into {GeminiClient.KeyFileName} on the headset (see README).");
                return;
            }

            var box = FindBoxAt(ray);
            var hint = box != null ? $"{box.ClassName} ({box.Score * 100f:0}%)" : null;
            m_view.SetText(box != null ? $"{box.ClassName}?" : "Looking...", "Asking Gemini...");
            m_lookup = StartCoroutine(Lookup(m_lookupId, target, box, hint));
        }

        private IEnumerator Lookup(int id, Vector3 target, SentisInferenceUiManager.BoundingBoxData box, string hint)
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

        /// <summary>First line becomes the title, the rest the body; markdown leftovers are cleaned up.</summary>
        private void ShowAnswer(string text, bool streaming)
        {
            var lines = text.Replace("**", "").Replace("__", "").Replace("#", "").Split('\n');
            string title = null;
            var body = new StringBuilder();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (title == null)
                {
                    title = line;
                    continue;
                }
                if (line.StartsWith("* ") || line.StartsWith("- "))
                {
                    line = "• " + line.Substring(2);
                }
                if (body.Length > 0)
                {
                    _ = body.Append('\n');
                }
                _ = body.Append(line);
            }
            m_view.SetText(title ?? "...", body.Length > 0 ? body.ToString() : streaming ? "..." : "");
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
