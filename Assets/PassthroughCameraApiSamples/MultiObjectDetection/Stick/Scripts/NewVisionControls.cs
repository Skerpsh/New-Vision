// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Everything of New Vision the wrist panel can change, wired up by SentisInferenceRunManager.
    /// (Controllers are not used anywhere: the panel and hand gestures are the only controls.)
    /// </summary>
    public class NewVisionControls
    {
        public ClassFilter Classes;

        public string[] ModelNames;
        public Func<int> GetModel;
        public Action<int> SetModel;

        public Func<bool> GetDetecting;
        public Action<bool> SetDetecting;

        public Func<int> GetMaxBoxes;
        public Action<int> SetMaxBoxes;

        public CompanionController Companion;

        /// <summary>Where objects were seen; set up by StickController (it beeps the stick when one is found).</summary>
        public ObjectMemory Memory;

        /// <summary>Live Russian subtitles (sherpa-onnx, on the headset); set up by StickController.</summary>
        public SpeechRecognizer Speech;
    }
}
