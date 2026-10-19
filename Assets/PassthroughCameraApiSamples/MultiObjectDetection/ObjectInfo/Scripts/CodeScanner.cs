// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;
using ZXing;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Looks for QR codes and barcodes around the reticle a few times per second. Decoding runs on a worker thread
    /// with ZXing.Net, on the camera pixels around the looked-at point (full camera resolution, no scaling).
    /// </summary>
    public class CodeScanner : MonoBehaviour
    {
        private const float ScanInterval = 0.3f;
        /// <summary>A code stays "seen" this long after the last successful decode, so the hint doesn't flicker.</summary>
        private const float KeepSeconds = 1.2f;
        /// <summary>Square crop around the looked-at point, in camera pixels.</summary>
        private const int CropPixels = 600;

        private PassthroughCameraAccess m_camera;
        private RenderTexture m_crop;
        private float m_nextScanTime;
        private bool m_scanning;
        private CodeContent m_current;
        private float m_currentTime = float.NegativeInfinity;

        private readonly BarcodeReaderGeneric m_reader = new()
        {
            AutoRotate = true,
            Options =
            {
                TryHarder = false,
                TryInverted = true,
                PossibleFormats = new List<BarcodeFormat>
                {
                    BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC,
                    BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E, BarcodeFormat.CODE_128
                }
            }
        };

        /// <summary>The code seen around the reticle right now, or null.</summary>
        public CodeContent Current => Time.time - m_currentTime <= KeepSeconds ? m_current : null;

        public void Init(PassthroughCameraAccess cameraAccess) => m_camera = cameraAccess;

        public void Clear() => m_currentTime = float.NegativeInfinity;

        private void OnDestroy()
        {
            if (m_crop != null)
            {
                m_crop.Release();
                Destroy(m_crop);
            }
        }

        /// <summary>Call every frame while scanning should happen; it throttles itself.</summary>
        public void Scan(Vector3 target)
        {
            if (m_scanning || Time.time < m_nextScanTime || m_camera == null || !m_camera.IsPlaying)
            {
                return;
            }
            var texture = m_camera.GetTexture();
            if (texture == null)
            {
                return;
            }
            m_nextScanTime = Time.time + ScanInterval;

            // Where the looked-at point is in the camera image
            var pose = m_camera.GetCameraPose();
            var inFront = (Quaternion.Inverse(pose.rotation) * (target - pose.position)).z > 0.05f;
            var point = inFront ? m_camera.WorldToViewportPoint(target, pose) : new Vector2(0.5f, 0.5f);

            var cropWidth = Mathf.Min(1f, (float)CropPixels / texture.width);
            var cropHeight = Mathf.Min(1f, (float)CropPixels / texture.height);
            var x0 = Mathf.Clamp(point.x - cropWidth * 0.5f, 0f, 1f - cropWidth);
            var y0 = Mathf.Clamp(point.y - cropHeight * 0.5f, 0f, 1f - cropHeight);

            var width = Mathf.RoundToInt(cropWidth * texture.width);
            var height = Mathf.RoundToInt(cropHeight * texture.height);
            if (m_crop == null || m_crop.width != width || m_crop.height != height)
            {
                if (m_crop != null)
                {
                    m_crop.Release();
                    Destroy(m_crop);
                }
                m_crop = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            }
            Graphics.Blit(texture, m_crop, new Vector2(cropWidth, cropHeight), new Vector2(x0, y0));

            m_scanning = true;
            _ = AsyncGPUReadback.Request(m_crop, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError || this == null)
                {
                    m_scanning = false;
                    return;
                }
                var rgba = request.GetData<byte>().ToArray();
                _ = DecodeAsync(rgba, request.width, request.height);
            });
        }

        private async Task DecodeAsync(byte[] rgba, int width, int height)
        {
            Result result = null;
            try
            {
                result = await Task.Run(() =>
                {
                    // Texture rows go bottom-up; ZXing wants top-down (a mirrored QR code doesn't decode)
                    var gray = new byte[width * height];
                    for (var y = 0; y < height; y++)
                    {
                        var src = (height - 1 - y) * width * 4;
                        var dst = y * width;
                        for (var x = 0; x < width; x++, src += 4)
                        {
                            gray[dst + x] = (byte)((rgba[src] * 77 + rgba[src + 1] * 150 + rgba[src + 2] * 29) >> 8);
                        }
                    }
                    return m_reader.Decode(gray, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
                });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[NewVision] code scan failed: {e.Message}");
            }

            // Back on the main thread (Unity's synchronization context)
            m_scanning = false;
            if (this == null || result == null || string.IsNullOrEmpty(result.Text))
            {
                return;
            }
            if (m_current == null || m_current.Raw != result.Text.Trim() || Current == null)
            {
                m_current = CodeContent.Parse(result.Text, result.BarcodeFormat.ToString());
                Debug.Log($"[NewVision] code {m_current.Format} {m_current.Kind}: {m_current.Raw.Length} chars");
            }
            m_currentTime = Time.time;
        }
    }
}
