// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// M5StickC Plus2 mounted on the headset, linked over Bluetooth LE and used as a Bluetooth keyboard bridge to the laptop.
    /// Sets up the link, the hand tracking, laptop microgestures and the left wrist panel that controls New Vision and the stick.
    /// </summary>
    public class StickController : MonoBehaviour
    {
        public void Init(SentisInferenceUiManager ui, NewVisionControls controls)
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            var head = rig != null ? rig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : null;
            var trackingSpace = rig != null ? rig.trackingSpace : null;

            var root = new GameObject("Stick");
            root.transform.SetParent(transform, false);
            var stick = root.AddComponent<StickClient>();
            var hands = root.AddComponent<HandTracker>();
            hands.Init(trackingSpace);
            var gestures = root.AddComponent<LaptopGestures>();
            gestures.Init(stick, hands, ui, head);
            var memory = root.AddComponent<ObjectMemory>();
            memory.Init(ui, head, stick);
            controls.Memory = memory;
            var speech = root.AddComponent<SpeechRecognizer>();
            root.AddComponent<SubtitleView>().Init(speech, head);
            controls.Speech = speech;
            root.AddComponent<WristPanel>().Init(stick, hands, gestures, controls, head);
            root.AddComponent<HandOutline>();   // the virtual hands as a faint outline in the theme colour
        }
    }
}
