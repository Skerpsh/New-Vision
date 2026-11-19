// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using SherpaOnnx;
using UnityEngine;
using UnityEngine.Networking;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Russian speech to text in real time, on the headset, offline: sherpa-onnx with the streaming T-One model
    /// (StreamingAssets/Speech/t-one-ru, copied to the app's files on first use: native code needs real files).
    /// The microphone is read every frame; recognition runs on its own thread. Off by default (wrist panel > SUBS).
    /// </summary>
    public class SpeechRecognizer : MonoBehaviour
    {
        public enum State { Off, NoPermission, Installing, Loading, Listening, NoModel, Error }

        private const string EnabledKey = "NewVision.Subtitles";
        private const string ModelFolder = "Speech/t-one-ru";
        private static readonly string[] s_modelFiles = { "model.onnx", "tokens.txt" };
        private const int RequestedRate = 16000;

        /// <summary>Text of the utterance still being spoken (changes as words come in).</summary>
        public event Action<string> Partial;
        /// <summary>A finished utterance (after a pause).</summary>
        public event Action<string> Final;

        public State Current { get; private set; } = State.Off;

        /// <summary>Microphone level 0..1 of the last frame (for a little meter).</summary>
        public float Level { get; private set; }

        public bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (value)
                {
                    Begin();
                }
                else
                {
                    Stop();
                }
            }
        }

        private AudioClip m_clip;
        private string m_device;
        private int m_readPosition;
        private int m_sampleRate;
        private Coroutine m_starting;

        private Thread m_thread;
        private volatile bool m_running;
        private readonly ConcurrentQueue<float[]> m_audio = new();
        private readonly ConcurrentQueue<(bool final, string text)> m_results = new();
        private readonly AutoResetEvent m_wake = new(false);
        private volatile string m_threadError;
        private volatile bool m_modelLoaded;
        private volatile int m_generation;
        private State m_loggedState = State.Off;

        private void Start()
        {
            if (Enabled)
            {
                Begin();
            }
        }

        private void OnDestroy() => Stop();

        private void OnApplicationPause(bool paused)
        {
            // Headset taken off / app in the background: release the microphone, come back when resumed
            if (paused)
            {
                StopMicrophone();
            }
            else if (Enabled && m_running)
            {
                StartMicrophone();
            }
        }

        private void Begin()
        {
            if (m_starting == null && !m_running)
            {
                m_starting = StartCoroutine(Starting());
            }
        }

        private IEnumerator Starting()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                var deadline = Time.time + 30f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) &&
                       Time.time < deadline)
                {
                    yield return new WaitForSeconds(0.25f);
                }
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
                    Current = State.NoPermission;
                    m_starting = null;
                    yield break;
                }
            }
#endif
            // Native code reads the model from a real file: copy it out of the APK once
            var folder = Path.Combine(Application.persistentDataPath, ModelFolder);
            if (!Installed(folder))
            {
                Current = State.Installing;
                Directory.CreateDirectory(folder);
                foreach (var file in s_modelFiles)
                {
                    var source = Path.Combine(Application.streamingAssetsPath, ModelFolder, file);
                    if (!source.Contains("://"))
                    {
                        source = "file://" + source;
                    }
                    using var request = UnityWebRequest.Get(source);
                    request.downloadHandler = new DownloadHandlerFile(Path.Combine(folder, file)) { removeFileOnAbort = true };
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[NewVision] speech: no model in the app ({source}): {request.error}");
                        Current = State.NoModel;
                        m_starting = null;
                        yield break;
                    }
                }
                File.WriteAllText(Path.Combine(folder, ".installed"), DateTime.UtcNow.ToString("o"));
                Debug.Log($"[NewVision] speech: model installed to {folder}");
            }

            Current = State.Loading;
            m_running = true;
            m_modelLoaded = false;
            m_threadError = null;
            var model = Path.Combine(folder, "model.onnx");
            var tokens = Path.Combine(folder, "tokens.txt");
            var generation = ++m_generation;
            m_thread = new Thread(() => Recognize(model, tokens, generation)) { IsBackground = true, Name = "NewVisionSpeech" };
            m_thread.Start();
            StartMicrophone();
            m_starting = null;
        }

        private static bool Installed(string folder) =>
            File.Exists(Path.Combine(folder, ".installed")) && Array.TrueForAll(s_modelFiles, f => File.Exists(Path.Combine(folder, f)));

        private void StartMicrophone()
        {
            if (m_clip != null)
            {
                return;
            }
            if (Microphone.devices.Length == 0)
            {
                Current = State.Error;
                Debug.LogWarning("[NewVision] speech: no microphone");
                return;
            }
            m_device = Microphone.devices[0];
            m_clip = Microphone.Start(m_device, true, 2, RequestedRate);
            m_sampleRate = m_clip.frequency;   // the rate actually delivered: sherpa-onnx resamples to the model's 8 kHz
            m_readPosition = 0;
            Debug.Log($"[NewVision] speech: microphone '{m_device}' at {m_sampleRate} Hz");
        }

        private void StopMicrophone()
        {
            if (m_clip != null)
            {
                Microphone.End(m_device);
                m_clip = null;
            }
        }

        private void Stop()
        {
            if (m_starting != null)
            {
                StopCoroutine(m_starting);
                m_starting = null;
            }
            StopMicrophone();
            m_running = false;
            m_generation++;   // a thread still loading or decoding sees it isn't current any more and ends
            _ = m_wake.Set();
            m_thread = null;   // it ends by itself and frees the recognizer
            while (m_audio.TryDequeue(out _)) { }
            Level = 0f;
            Current = State.Off;
            Partial?.Invoke("");
        }

        private void Update()
        {
            if (m_threadError != null)
            {
                Debug.LogWarning("[NewVision] speech: " + m_threadError);
                m_threadError = null;
                Current = State.Error;
                StopMicrophone();
            }
            if (m_running && m_modelLoaded && Current == State.Loading)
            {
                Current = State.Listening;
            }

            ReadMicrophone();

            if (Current != m_loggedState)
            {
                m_loggedState = Current;
                Debug.Log($"[NewVision] speech: state {Current}");
            }

            while (m_results.TryDequeue(out var result))
            {
                if (result.final)
                {
                    Debug.Log($"[NewVision] speech: \"{result.text}\"");
                    Final?.Invoke(result.text);
                }
                else
                {
                    Partial?.Invoke(result.text);
                }
            }
        }

        private void ReadMicrophone()
        {
            if (m_clip == null || !m_running)
            {
                return;
            }
            var position = Microphone.GetPosition(m_device);
            var total = m_clip.samples;
            var count = (position - m_readPosition + total) % total;
            if (count <= 0)
            {
                return;
            }
            var samples = new float[count];
            _ = m_clip.GetData(samples, m_readPosition);   // wraps around the end of the looping clip
            m_readPosition = position;

            var peak = 0f;
            foreach (var s in samples)
            {
                peak = Mathf.Max(peak, Mathf.Abs(s));
            }
            Level = Mathf.Lerp(Level, peak, 0.5f);

            // While the model is still loading the audio waits (at most ~4 s of it)
            if (m_audio.Count < 120)
            {
                m_audio.Enqueue(samples);
                _ = m_wake.Set();
            }
        }

        /// <summary>The recognition thread: load the model, then decode audio as it arrives.</summary>
        private void Recognize(string model, string tokens, int generation)
        {
            OnlineRecognizer recognizer = null;
            OnlineStream stream = null;
            try
            {
                var config = OnlineRecognizerConfig.Defaults();
                config.FeatConfig.SampleRate = 8000;   // T-One works at 8 kHz (sherpa-onnx forces it anyway)
                config.ModelConfig.ToneCtc.Model = model;
                config.ModelConfig.Tokens = tokens;
                config.ModelConfig.NumThreads = 2;
                config.EnableEndpoint = 1;
                config.Rule1MinTrailingSilence = 2.4f;   // silence with nothing said: end
                config.Rule2MinTrailingSilence = 0.9f;   // pause after words: the phrase is finished
                config.Rule3MinUtteranceLength = 20f;    // a monologue is cut every 20 s
                recognizer = new OnlineRecognizer(config);
                stream = recognizer.CreateStream();
                if (generation != m_generation)
                {
                    return;   // switched off while the model was loading
                }
                m_modelLoaded = true;

                var last = "";
                var rate = 0;
                while (m_running && generation == m_generation)
                {
                    if (!m_audio.TryDequeue(out var samples))
                    {
                        _ = m_wake.WaitOne(100);
                        continue;
                    }
                    if (rate == 0)
                    {
                        rate = m_sampleRate;
                    }
                    else if (rate != m_sampleRate)
                    {
                        continue;   // sherpa-onnx exits the process if a stream's sample rate changes: drop such audio
                    }
                    stream.AcceptWaveform(rate, samples, samples.Length);
                    while (recognizer.IsReady(stream))
                    {
                        recognizer.Decode(stream);
                    }
                    var text = recognizer.GetText(stream).Trim();
                    if (text != last)
                    {
                        last = text;
                        m_results.Enqueue((false, text));
                    }
                    if (recognizer.IsEndpoint(stream))
                    {
                        if (text.Length > 0)
                        {
                            m_results.Enqueue((true, text));
                        }
                        recognizer.Reset(stream);
                        last = "";
                        m_results.Enqueue((false, ""));
                    }
                }
            }
            catch (Exception e)
            {
                m_threadError = e.Message;
            }
            finally
            {
                stream?.Dispose();
                recognizer?.Dispose();
                if (generation == m_generation)
                {
                    m_modelLoaded = false;
                }
            }
        }
    }
}
