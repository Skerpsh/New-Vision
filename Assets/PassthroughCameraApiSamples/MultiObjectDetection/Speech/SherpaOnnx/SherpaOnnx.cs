// Streaming speech recognition API of sherpa-onnx (https://github.com/k2-fsa/sherpa-onnx), C API v2.0.2.
// Condensed from sherpa-onnx's scripts/dotnet (Copyright (c) 2023 Xiaomi Corporation and contributors,
// Apache License 2.0) for Unity: C# 9 (no struct constructors, Defaults() instead) and no unsafe code.
// The struct layouts must match sherpa-onnx/c-api/c-api.h of the bundled libsherpa-onnx-c-api.so exactly.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SherpaOnnx
{
    internal static class Dll
    {
        public const string Filename = "sherpa-onnx-c-api";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FeatureConfig
    {
        public int SampleRate;
        public int FeatureDim;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineTransducerModelConfig
    {
        [MarshalAs(UnmanagedType.LPStr)] public string Encoder;
        [MarshalAs(UnmanagedType.LPStr)] public string Decoder;
        [MarshalAs(UnmanagedType.LPStr)] public string Joiner;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineParaformerModelConfig
    {
        [MarshalAs(UnmanagedType.LPStr)] public string Encoder;
        [MarshalAs(UnmanagedType.LPStr)] public string Decoder;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineSingleModelConfig
    {
        [MarshalAs(UnmanagedType.LPStr)] public string Model;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineModelConfig
    {
        public OnlineTransducerModelConfig Transducer;
        public OnlineParaformerModelConfig Paraformer;
        public OnlineSingleModelConfig Zipformer2Ctc;
        [MarshalAs(UnmanagedType.LPStr)] public string Tokens;
        public int NumThreads;
        [MarshalAs(UnmanagedType.LPStr)] public string Provider;
        public int Debug;
        [MarshalAs(UnmanagedType.LPStr)] public string ModelType;
        [MarshalAs(UnmanagedType.LPStr)] public string ModelingUnit;
        [MarshalAs(UnmanagedType.LPStr)] public string BpeVocab;
        [MarshalAs(UnmanagedType.LPStr)] public string TokensBuf;
        public int TokensBufSize;
        public OnlineSingleModelConfig NemoCtc;
        public OnlineSingleModelConfig ToneCtc;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineCtcFstDecoderConfig
    {
        [MarshalAs(UnmanagedType.LPStr)] public string Graph;
        public int MaxActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HomophoneReplacerConfig
    {
        [MarshalAs(UnmanagedType.LPStr)] public string DictDir;
        [MarshalAs(UnmanagedType.LPStr)] public string Lexicon;
        [MarshalAs(UnmanagedType.LPStr)] public string RuleFsts;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineRecognizerConfig
    {
        public FeatureConfig FeatConfig;
        public OnlineModelConfig ModelConfig;
        [MarshalAs(UnmanagedType.LPStr)] public string DecodingMethod;
        public int MaxActivePaths;
        public int EnableEndpoint;
        public float Rule1MinTrailingSilence;
        public float Rule2MinTrailingSilence;
        public float Rule3MinUtteranceLength;
        [MarshalAs(UnmanagedType.LPStr)] public string HotwordsFile;
        public float HotwordsScore;
        public OnlineCtcFstDecoderConfig CtcFstDecoderConfig;
        [MarshalAs(UnmanagedType.LPStr)] public string RuleFsts;
        [MarshalAs(UnmanagedType.LPStr)] public string RuleFars;
        public float BlankPenalty;
        [MarshalAs(UnmanagedType.LPStr)] public string HotwordsBuf;
        public int HotwordsBufSize;
        public HomophoneReplacerConfig Hr;

        /// <summary>The defaults of sherpa-onnx's C# constructors; every string set (the C side expects no null pointers).</summary>
        public static OnlineRecognizerConfig Defaults() => new()
        {
            FeatConfig = new FeatureConfig { SampleRate = 16000, FeatureDim = 80 },
            ModelConfig = new OnlineModelConfig
            {
                Transducer = new OnlineTransducerModelConfig { Encoder = "", Decoder = "", Joiner = "" },
                Paraformer = new OnlineParaformerModelConfig { Encoder = "", Decoder = "" },
                Zipformer2Ctc = new OnlineSingleModelConfig { Model = "" },
                Tokens = "",
                NumThreads = 1,
                Provider = "cpu",
                Debug = 0,
                ModelType = "",
                ModelingUnit = "cjkchar",
                BpeVocab = "",
                TokensBuf = "",
                TokensBufSize = 0,
                NemoCtc = new OnlineSingleModelConfig { Model = "" },
                ToneCtc = new OnlineSingleModelConfig { Model = "" }
            },
            DecodingMethod = "greedy_search",
            MaxActivePaths = 4,
            EnableEndpoint = 0,
            Rule1MinTrailingSilence = 2.4f,
            Rule2MinTrailingSilence = 1.2f,
            Rule3MinUtteranceLength = 20f,
            HotwordsFile = "",
            HotwordsScore = 1.5f,
            CtcFstDecoderConfig = new OnlineCtcFstDecoderConfig { Graph = "", MaxActive = 3000 },
            RuleFsts = "",
            RuleFars = "",
            BlankPenalty = 0f,
            HotwordsBuf = "",
            HotwordsBufSize = 0,
            Hr = new HomophoneReplacerConfig { DictDir = "", Lexicon = "", RuleFsts = "" }
        };
    }

    /// <summary>One streaming recognizer; feed audio to an <see cref="OnlineStream"/>, decode while ready, read the text.</summary>
    public sealed class OnlineRecognizer : IDisposable
    {
        private IntPtr m_handle;

        public OnlineRecognizer(OnlineRecognizerConfig config)
        {
            m_handle = SherpaOnnxCreateOnlineRecognizer(ref config);
            if (m_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("sherpa-onnx could not create the recognizer (check the model paths)");
            }
        }

        public OnlineStream CreateStream() => new(SherpaOnnxCreateOnlineStream(m_handle));

        public bool IsReady(OnlineStream stream) => SherpaOnnxIsOnlineStreamReady(m_handle, stream.Handle) != 0;

        public void Decode(OnlineStream stream) => SherpaOnnxDecodeOnlineStream(m_handle, stream.Handle);

        public bool IsEndpoint(OnlineStream stream) => SherpaOnnxOnlineStreamIsEndpoint(m_handle, stream.Handle) != 0;

        /// <summary>Starts a new utterance (call after an endpoint).</summary>
        public void Reset(OnlineStream stream) => SherpaOnnxOnlineStreamReset(m_handle, stream.Handle);

        /// <summary>Text recognized so far in the current utterance.</summary>
        public string GetText(OnlineStream stream)
        {
            var result = SherpaOnnxGetOnlineStreamResult(m_handle, stream.Handle);
            if (result == IntPtr.Zero)
            {
                return "";
            }
            try
            {
                // The first field of SherpaOnnxOnlineRecognizerResult is "const char *text" (UTF-8)
                return Utf8(Marshal.ReadIntPtr(result));
            }
            finally
            {
                SherpaOnnxDestroyOnlineRecognizerResult(result);
            }
        }

        private static string Utf8(IntPtr text)
        {
            if (text == IntPtr.Zero)
            {
                return "";
            }
            var length = 0;
            while (Marshal.ReadByte(text, length) != 0)
            {
                length++;
            }
            var bytes = new byte[length];
            Marshal.Copy(text, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        public void Dispose()
        {
            if (m_handle != IntPtr.Zero)
            {
                SherpaOnnxDestroyOnlineRecognizer(m_handle);
                m_handle = IntPtr.Zero;
            }
        }

        [DllImport(Dll.Filename)] private static extern IntPtr SherpaOnnxCreateOnlineRecognizer(ref OnlineRecognizerConfig config);
        [DllImport(Dll.Filename)] private static extern void SherpaOnnxDestroyOnlineRecognizer(IntPtr handle);
        [DllImport(Dll.Filename)] private static extern IntPtr SherpaOnnxCreateOnlineStream(IntPtr handle);
        [DllImport(Dll.Filename)] private static extern int SherpaOnnxIsOnlineStreamReady(IntPtr handle, IntPtr stream);
        [DllImport(Dll.Filename)] private static extern void SherpaOnnxDecodeOnlineStream(IntPtr handle, IntPtr stream);
        [DllImport(Dll.Filename)] private static extern IntPtr SherpaOnnxGetOnlineStreamResult(IntPtr handle, IntPtr stream);
        [DllImport(Dll.Filename)] private static extern void SherpaOnnxDestroyOnlineRecognizerResult(IntPtr result);
        [DllImport(Dll.Filename)] private static extern void SherpaOnnxOnlineStreamReset(IntPtr handle, IntPtr stream);
        [DllImport(Dll.Filename)] private static extern int SherpaOnnxOnlineStreamIsEndpoint(IntPtr handle, IntPtr stream);
    }

    public sealed class OnlineStream : IDisposable
    {
        public IntPtr Handle { get; private set; }

        public OnlineStream(IntPtr handle) => Handle = handle;

        /// <summary>Audio as floats in [-1, 1]; always the same sample rate for one stream (it resamples to the model's rate).</summary>
        public void AcceptWaveform(int sampleRate, float[] samples, int count) =>
            SherpaOnnxOnlineStreamAcceptWaveform(Handle, sampleRate, samples, count);

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                SherpaOnnxDestroyOnlineStream(Handle);
                Handle = IntPtr.Zero;
            }
        }

        [DllImport(Dll.Filename)] private static extern void SherpaOnnxDestroyOnlineStream(IntPtr handle);
        [DllImport(Dll.Filename)] private static extern void SherpaOnnxOnlineStreamAcceptWaveform(IntPtr handle, int sampleRate, float[] samples, int n);
    }
}
