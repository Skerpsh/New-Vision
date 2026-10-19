// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// C# side of the off-screen Android WebView (Assets/Plugins/Android/NewVisionWebView.java).
    /// The page image lives in two direct ByteBuffers on the Java side; a new one is uploaded into <see cref="Texture"/>
    /// straight from that native memory, without copying it through JNI. Row 0 is the top of the page.
    /// Lives in its own assembly (NewVision.WebView) because reading the buffer address needs unsafe code.
    /// </summary>
    public class WebViewBridge : IDisposable
    {
        public const int KeyDelete = 67;
        public const int KeyEnter = 66;

        private readonly AndroidJavaObject m_plugin;
        private readonly IntPtr[] m_bufferAddress = new IntPtr[2];
        private readonly AndroidJavaObject[] m_buffers = new AndroidJavaObject[2];
        private int m_lastFrame;

        /// <summary>Upload target for the raw page image (no mipmaps).</summary>
        private Texture2D Upload { get; }
        /// <summary>What to show: the page with GPU-made mipmaps, so text doesn't shimmer when the panel is far or small.</summary>
        public RenderTexture Texture { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>False in the editor and on anything that isn't Android.</summary>
        public bool Available => m_plugin != null;

        public WebViewBridge(int width, int height)
        {
            Width = width;
            Height = height;
            Upload = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "WebViewUpload"
            };
            var white = new Color32[width * height];
            Array.Fill(white, new Color32(255, 255, 255, 255));
            Upload.SetPixels32(white);
            Upload.Apply(false);
            Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                wrapMode = TextureWrapMode.Clamp,
                name = "WebView"
            };
            Texture.Create();
            Graphics.Blit(Upload, Texture);
            Texture.GenerateMips();

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                m_plugin = new AndroidJavaObject("com.newvision.webview.NewVisionWebView", activity, width, height);
                for (var i = 0; i < 2; i++)
                {
                    m_buffers[i] = m_plugin.Call<AndroidJavaObject>("getBuffer", i);
                    unsafe
                    {
                        m_bufferAddress[i] = (IntPtr)AndroidJNI.GetDirectBufferAddress(m_buffers[i].GetRawObject());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[NewVision] browser: can't create the WebView: {e.Message}");
                m_plugin = null;
            }
#endif
        }

        /// <summary>Uploads the newest page image, if there is one. Call once per frame while visible.</summary>
        public void UpdateTexture()
        {
            if (m_plugin == null)
            {
                return;
            }
            var frame = m_plugin.Call<int>("getFrameNumber");
            if (frame == m_lastFrame)
            {
                return;
            }
            m_lastFrame = frame;
            var index = m_plugin.Call<int>("acquireFrame");
            try
            {
                var address = m_bufferAddress[index];
                if (address != IntPtr.Zero)
                {
                    Upload.LoadRawTextureData(address, Width * Height * 4);
                }
            }
            finally
            {
                m_plugin.Call("releaseFrame");
            }
            Upload.Apply(false);
            Graphics.Blit(Upload, Texture);
            Texture.GenerateMips();
        }

        public string Url => m_plugin?.Call<string>("getUrl") ?? "";
        public string Title => m_plugin?.Call<string>("getTitle") ?? "";
        public int Progress => m_plugin?.Call<int>("getProgress") ?? 100;
        public bool CanGoBack => m_plugin != null && m_plugin.Call<bool>("canGoBack");
        public bool CanGoForward => m_plugin != null && m_plugin.Call<bool>("canGoForward");
        public bool InputFocused => m_plugin != null && m_plugin.Call<bool>("isInputFocused");
        /// <summary>Changes each time a text field of the page gets the focus.</summary>
        public int FocusVersion => m_plugin?.Call<int>("getFocusVersion") ?? 0;
        /// <summary>HTML input type of the focused field (text, password, email, url, number, search, tel, textarea).</summary>
        public string InputType => m_plugin?.Call<string>("getInputType") ?? "";
        /// <summary>Text of the focused field when it got the focus.</summary>
        public string InputValue => m_plugin?.Call<string>("getInputValue") ?? "";
        public string TakeMessage() => m_plugin?.Call<string>("takeMessage") ?? "";

        public void LoadUrl(string url) => m_plugin?.Call("loadUrl", url);
        public void GoBack() => m_plugin?.Call("goBack");
        public void GoForward() => m_plugin?.Call("goForward");
        public void Reload() => m_plugin?.Call("reload");
        public void SetActive(bool active) => m_plugin?.Call("setActive", active);

        /// <summary>action 0 = down, 1 = up, 2 = move; position in page pixels from the top-left.</summary>
        public void Touch(int action, Vector2 pixel) => m_plugin?.Call("touch", action, pixel.x, pixel.y);
        /// <summary>Touch up stamped just after the last move, so the fling keeps the speed of the gesture.</summary>
        public void TouchUpAtLastMove(Vector2 pixel) => m_plugin?.Call("touchAt", 1, pixel.x, pixel.y, true);
        /// <summary>Scrolls the scrollable part under the point by dx / dy page pixels (no fling).</summary>
        public void ScrollAt(Vector2 pixel, Vector2 delta) => m_plugin?.Call("scrollAt", pixel.x, pixel.y, delta.x, delta.y);
        public void Scroll(Vector2 pixel, float amount) => m_plugin?.Call("scroll", pixel.x, pixel.y, amount);
        public void InsertText(string text) => m_plugin?.Call("insertText", text);
        public void SetInputValue(string text) => m_plugin?.Call("setInputValue", text);
        public void Hover(Vector2 pixel, bool exit) => m_plugin?.Call("hover", pixel.x, pixel.y, exit);
        public void Key(int keyCode) => m_plugin?.Call("key", keyCode);
        public void BlurInput() => m_plugin?.Call("blurInput");

        public void Dispose()
        {
            m_plugin?.Call("destroy");
            m_plugin?.Dispose();
            foreach (var buffer in m_buffers)
            {
                buffer?.Dispose();
            }
            UnityEngine.Object.Destroy(Upload);
            Texture.Release();
            UnityEngine.Object.Destroy(Texture);
        }
    }
}
