// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Sharper picture for the UI: the eye buffers are rendered a bit above the display resolution (thin lines and text
    /// stop stair-stepping), the compositor sharpens / supersamples them, and 4x MSAA comes from the quality settings.
    /// The scene itself is nearly empty (passthrough + a few panels), so the GPU can afford it — but if frames start
    /// dropping anyway, the resolution steps back down towards 1.0 so 72 FPS always wins.
    /// </summary>
    public class RenderQuality : MonoBehaviour
    {
        private const float StartScale = 1.25f;
        private const float StepDown = 0.1f;
        private const float MinScale = 1f;
        private const float WarmupSeconds = 8f;
        private const float WindowSeconds = 10f;
        private const float MaxDropShare = 0.04f;

        private float m_windowStart;
        private int m_frames;
        private int m_dropped;

        private void Start()
        {
            if (OVRManager.instance != null)
            {
                OVRManager.instance.enableDynamicResolution = false;
                OVRManager.instance.sharpenType = OVRPlugin.LayerSharpenType.Automatic;
            }
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = StartScale;
            m_windowStart = Time.unscaledTime + WarmupSeconds;
            Debug.Log($"[NewVision] render: eye scale {StartScale:F2}, MSAA {QualitySettings.antiAliasing}x, " +
                      $"foveation {OVRManager.foveatedRenderingLevel}");
        }

        private void Update()
        {
            var now = Time.unscaledTime;
            if (now < m_windowStart)
            {
                return;
            }
            var refresh = OVRManager.display != null && OVRManager.display.displayFrequency > 1f ? OVRManager.display.displayFrequency : 72f;
            m_frames++;
            if (Time.unscaledDeltaTime > 1.5f / refresh)
            {
                m_dropped++;
            }
            if (now - m_windowStart < WindowSeconds)
            {
                return;
            }

            var share = m_dropped / (float)Mathf.Max(1, m_frames);
            var scale = UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
            if (share > MaxDropShare && scale > MinScale + 0.01f)
            {
                scale = Mathf.Max(MinScale, scale - StepDown);
                UnityEngine.XR.XRSettings.eyeTextureResolutionScale = scale;
                Debug.Log($"[NewVision] render: dropped {m_dropped}/{m_frames} frames, eye scale down to {scale:F2}");
                m_windowStart = now + 2f;   // the resize itself hitches: don't count it
            }
            else
            {
                Debug.Log($"[NewVision] render: eye scale {scale:F2}, dropped {m_dropped}/{m_frames} frames");
                m_windowStart = now;
            }
            m_frames = 0;
            m_dropped = 0;
        }
    }
}
