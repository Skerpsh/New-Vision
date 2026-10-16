// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Minimal Gemini API client: one JPEG + prompt in, streamed text out, optionally grounded with Google Search.
    /// The API key is read from a file on the device (never stored in the APK or in git), see <see cref="KeyFilePath"/>.
    /// </summary>
    public class GeminiClient
    {
        public const string KeyFileName = "gemini_api_key.txt";
        private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:streamGenerateContent?alt=sse";
        private const int TimeoutSeconds = 25;

        public string Model = "gemini-3.5-flash-lite";
        /// <summary>Used when the main model is overloaded or fails (5xx, timeout). Empty = no fallback.</summary>
        public string FallbackModel = "gemini-3.8-flash";
        /// <summary>Less thinking = faster answer. Empty = model default.</summary>
        public string ThinkingLevel = "low";
        public bool UseGoogleSearch = true;

        private string m_apiKey;
        private UnityWebRequest m_request;
        // Google Search grounding has no free quota: after a 429 with search, ask without it for the rest of the session
        private bool m_searchUnavailable;

        /// <summary>On Quest: /sdcard/Android/data/&lt;package&gt;/files/gemini_api_key.txt</summary>
        public static string KeyFilePath => Path.Combine(Application.persistentDataPath, KeyFileName);

        public bool HasKey => !string.IsNullOrEmpty(ApiKey);
        public bool IsBusy => m_request != null;

        private string ApiKey => m_apiKey ??= LoadKey();

        private static string LoadKey()
        {
            try
            {
                if (File.Exists(KeyFilePath))
                {
                    return File.ReadAllText(KeyFilePath).Trim();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] can't read {KeyFilePath}: {e.Message}");
            }
            // Editor / desktop fallback
            return Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim() ?? "";
        }

        /// <summary>Stops the running request, its callbacks won't be called anymore.</summary>
        public void Cancel()
        {
            if (m_request == null)
            {
                return;
            }
            var request = m_request;
            m_request = null;
            request.Abort();
            request.Dispose();
        }

        /// <summary>
        /// Sends the image and prompt. <paramref name="onText"/> gets the whole answer so far each time a piece arrives,
        /// then exactly one of <paramref name="onDone"/> / <paramref name="onError"/> is called.
        /// </summary>
        public void Ask(string systemPrompt, string userPrompt, byte[] jpeg, Action<string> onText, Action<string> onDone, Action<string> onError)
        {
            Cancel();
            if (!HasKey)
            {
                onError?.Invoke("API key missing");
                return;
            }
            Send(Model, systemPrompt, userPrompt, jpeg, onText, onDone, onError);
        }

        private void Send(string model, string systemPrompt, string userPrompt, byte[] jpeg, Action<string> onText, Action<string> onDone, Action<string> onError)
        {
            var useSearch = UseGoogleSearch && !m_searchUnavailable;
            var answer = new StringBuilder();
            string apiError = null;
            var handler = new SseHandler(json =>
            {
                var chunk = ParseChunk(json);
                if (chunk?.error != null && !string.IsNullOrEmpty(chunk.error.message))
                {
                    apiError = chunk.error.message;
                    return;
                }
                if (chunk?.candidates == null || chunk.candidates.Length == 0 || chunk.candidates[0].content?.parts == null)
                {
                    return;
                }
                var added = false;
                foreach (var part in chunk.candidates[0].content.parts)
                {
                    if (!part.thought && !string.IsNullOrEmpty(part.text))
                    {
                        _ = answer.Append(part.text);
                        added = true;
                    }
                }
                if (added)
                {
                    onText?.Invoke(answer.ToString());
                }
            });

            var body = Encoding.UTF8.GetBytes(BuildBody(systemPrompt, userPrompt, jpeg, useSearch));
            var request = new UnityWebRequest(string.Format(Endpoint, model), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" },
                downloadHandler = handler,
                timeout = TimeoutSeconds
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-goog-api-key", ApiKey);
            m_request = request;

            var startTime = Time.realtimeSinceStartup;
            request.SendWebRequest().completed += _ =>
            {
                if (m_request != request)
                {
                    return; // cancelled or replaced by a newer request
                }
                m_request = null;
                var seconds = Time.realtimeSinceStartup - startTime;
                var code = request.responseCode;
                var success = request.result == UnityWebRequest.Result.Success && apiError == null && answer.Length > 0;
                var message = success ? null : apiError ?? ErrorMessage(request, handler.Raw.ToString()) ?? "Empty answer";
                request.Dispose();
                if (success)
                {
                    Debug.Log($"[NewVision] gemini {model}{(useSearch ? " +search" : "")}: {seconds:F1} s, {jpeg.Length / 1024} KB image");
                    onDone?.Invoke(answer.ToString());
                    return;
                }

                Debug.LogWarning($"[NewVision] gemini {model}{(useSearch ? " +search" : "")} error {code} after {seconds:F1} s: {message}");
                // Retry only if nothing has been shown yet
                if (answer.Length == 0)
                {
                    if (code == 429 && useSearch)
                    {
                        m_searchUnavailable = true;
                        Debug.LogWarning("[NewVision] Google Search grounding unavailable (no quota), continuing without it");
                        Send(model, systemPrompt, userPrompt, jpeg, onText, onDone, onError);
                        return;
                    }
                    var overloaded = code >= 500 || code == 0; // server error, timeout or no connection
                    if (overloaded && model != FallbackModel && !string.IsNullOrEmpty(FallbackModel))
                    {
                        Send(FallbackModel, systemPrompt, userPrompt, jpeg, onText, onDone, onError);
                        return;
                    }
                }
                onError?.Invoke(message);
            };
        }

        private string BuildBody(string systemPrompt, string userPrompt, byte[] jpeg, bool useSearch)
        {
            var sb = new StringBuilder(jpeg.Length * 4 / 3 + 2048);
            _ = sb.Append("{\"systemInstruction\":{\"parts\":[{\"text\":").Append(Quote(systemPrompt)).Append("}]},");
            _ = sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[");
            _ = sb.Append("{\"inline_data\":{\"mime_type\":\"image/jpeg\",\"data\":\"").Append(Convert.ToBase64String(jpeg)).Append("\"}},");
            _ = sb.Append("{\"text\":").Append(Quote(userPrompt)).Append("}]}]");
            if (useSearch)
            {
                _ = sb.Append(",\"tools\":[{\"google_search\":{}}]");
            }
            if (!string.IsNullOrEmpty(ThinkingLevel))
            {
                _ = sb.Append(",\"generationConfig\":{\"thinkingConfig\":{\"thinkingLevel\":").Append(Quote(ThinkingLevel)).Append("}}");
            }
            _ = sb.Append('}');
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (var c in s)
            {
                _ = c switch
                {
                    '"' => sb.Append("\\\""),
                    '\\' => sb.Append("\\\\"),
                    '\n' => sb.Append("\\n"),
                    '\r' => sb.Append("\\r"),
                    '\t' => sb.Append("\\t"),
                    _ when c < ' ' => sb.Append("\\u").Append(((int)c).ToString("x4")),
                    _ => sb.Append(c)
                };
            }
            return sb.Append('"').ToString();
        }

        private static string ErrorMessage(UnityWebRequest request, string body)
        {
            // Error bodies are plain JSON (sometimes wrapped in [ ]), not server-sent events
            var trimmed = body.Trim().TrimStart('[').TrimEnd(']');
            var parsed = ParseChunk(trimmed);
            if (parsed?.error != null && !string.IsNullOrEmpty(parsed.error.message))
            {
                return $"{parsed.error.code}: {parsed.error.message}";
            }
            return request.result == UnityWebRequest.Result.Success ? null : request.error;
        }

        private static Chunk ParseChunk(string json)
        {
            try
            {
                return JsonUtility.FromJson<Chunk>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Splits the response stream into "data: {...}" events as bytes arrive (called on the main thread).</summary>
        private class SseHandler : DownloadHandlerScript
        {
            private const int MaxRawChars = 8192;
            private readonly Action<string> m_onEvent;
            private readonly Decoder m_decoder = Encoding.UTF8.GetDecoder();
            private readonly StringBuilder m_line = new();
            public readonly StringBuilder Raw = new();

            public SseHandler(Action<string> onEvent) : base(new byte[16 * 1024]) => m_onEvent = onEvent;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                var chars = new char[dataLength];
                var count = m_decoder.GetChars(data, 0, dataLength, chars, 0);
                if (Raw.Length < MaxRawChars)
                {
                    _ = Raw.Append(chars, 0, Mathf.Min(count, MaxRawChars - Raw.Length));
                }
                for (var i = 0; i < count; i++)
                {
                    var c = chars[i];
                    if (c != '\n')
                    {
                        _ = m_line.Append(c);
                        continue;
                    }
                    var line = m_line.ToString().TrimEnd('\r');
                    _ = m_line.Clear();
                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        m_onEvent(line.Substring(5).Trim());
                    }
                }
                return true;
            }
        }

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] private class Chunk { public Candidate[] candidates; public ApiError error; }
        [Serializable] private class Candidate { public Content content; public string finishReason; }
        [Serializable] private class Content { public Part[] parts; }
        [Serializable] private class Part { public string text; public bool thought; }
        [Serializable] private class ApiError { public int code; public string message; public string status; }
#pragma warning restore 0649
    }
}
