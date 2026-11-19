// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Both tracked hands in world space, read straight from OVRPlugin once per frame (no hand prefabs needed),
    /// plus the system microgestures (thumb tap / swipes on the side of the index finger).
    /// Needs the OpenXR hand skeleton (Meta XR settings: Hand Skeleton Version = OpenXR) — then the joint positions come ready-made.
    /// </summary>
    public class HandTracker : MonoBehaviour
    {
        public const int Left = 0;
        public const int Right = 1;

        public struct Hand
        {
            public bool Valid;
            public bool HighConfidence;
            public Vector3 Wrist;
            public Vector3 Palm;
            /// <summary>Out of the palm (inner side of the hand).</summary>
            public Vector3 PalmNormal;
            /// <summary>From the wrist towards the fingers.</summary>
            public Vector3 FingerDirection;
            public Vector3 IndexTip;
            /// <summary>Microgesture that started this frame, or NoGesture.</summary>
            public OVRHand.MicrogestureType Gesture;
        }

        private readonly Hand[] m_hands = new Hand[2];
        private readonly OVRPlugin.MicrogestureType[] m_lastGesture = new OVRPlugin.MicrogestureType[2];
        private OVRPlugin.HandState m_state;
        private OVRPlugin.HandTrackingState m_trackingState;
        private Transform m_trackingSpace;
        private bool m_warned;

        public void Init(Transform trackingSpace)
        {
            m_trackingSpace = trackingSpace;
            Application.onBeforeRender += LatePoses;
        }

        private void OnDestroy() => Application.onBeforeRender -= LatePoses;

        public Hand Get(int hand) => m_hands[hand];

        /// <summary>Raised right before rendering, after the poses were read again (the latest prediction for this frame).</summary>
        public event System.Action PosesUpdated;

        private void Update()
        {
            // Without this hint every frame the runtime doesn't run microgesture recognition
            _ = OVRPlugin.SendMicrogestureHint();
            if (OVRPlugin.HandSkeletonVersion != OVRHandSkeletonVersion.OpenXR && !m_warned)
            {
                m_warned = true;
                Debug.LogWarning("[NewVision] stick: hand skeleton is not OpenXR, the wrist panel can't track the hand");
            }
            Read(Left, OVRPlugin.Hand.HandLeft, true);
            Read(Right, OVRPlugin.Hand.HandRight, true);
        }

        /// <summary>
        /// The camera rig moves the head to its latest pose just before rendering; reading the hands again at that moment
        /// keeps things attached to them (the wrist panel) in step with the head instead of a moment behind while moving.
        /// </summary>
        private void LatePoses()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            Read(Left, OVRPlugin.Hand.HandLeft, false);
            Read(Right, OVRPlugin.Hand.HandRight, false);
            PosesUpdated?.Invoke();
        }

        private void Read(int index, OVRPlugin.Hand hand, bool gestures)
        {
            ref var h = ref m_hands[index];
            if (gestures)
            {
                h.Gesture = OVRHand.MicrogestureType.NoGesture;
            }
            if (OVRPlugin.HandSkeletonVersion != OVRHandSkeletonVersion.OpenXR ||
                !OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref m_state) ||
                (m_state.Status & OVRPlugin.HandStatus.HandTracked) == 0 ||
                m_state.BonePositions == null || m_state.BonePositions.Length < (int)OVRPlugin.BoneId.XRHand_LittleTip)
            {
                h.Valid = false;
                h.HighConfidence = false;
                if (gestures)
                {
                    m_lastGesture[index] = OVRPlugin.MicrogestureType.NoGesture;
                }
                return;
            }

            h.Valid = true;
            h.HighConfidence = m_state.HandConfidence == OVRPlugin.TrackingConfidence.High;
            h.Wrist = Joint(OVRPlugin.BoneId.XRHand_Wrist);
            h.Palm = Joint(OVRPlugin.BoneId.XRHand_Palm);
            h.IndexTip = Joint(OVRPlugin.BoneId.XRHand_IndexTip);
            // OpenXR joints: +Y out of the back of the hand, -Z towards the fingertips (Z is flipped into Unity space)
            var palmRotation = Rotation(OVRPlugin.BoneId.XRHand_Palm);
            h.PalmNormal = palmRotation * Vector3.down;
            var middle = Joint(OVRPlugin.BoneId.XRHand_MiddleProximal);
            h.FingerDirection = (middle - h.Wrist).sqrMagnitude > 1e-6f ? (middle - h.Wrist).normalized : palmRotation * Vector3.forward;

            // Microgestures: report the frame a gesture appears, not every frame it is held (once per frame, in Update)
            if (gestures && OVRPlugin.GetHandTrackingState(OVRPlugin.Step.Render, hand, ref m_trackingState))
            {
                var gesture = m_trackingState.Microgesture;
                if (gesture != m_lastGesture[index] && gesture > OVRPlugin.MicrogestureType.NoGesture &&
                    gesture <= OVRPlugin.MicrogestureType.ThumbTap)
                {
                    h.Gesture = (OVRHand.MicrogestureType)gesture;
                }
                m_lastGesture[index] = gesture;
            }
        }

        private Vector3 Joint(OVRPlugin.BoneId id)
        {
            var local = m_state.BonePositions[(int)id].FromFlippedZVector3f();
            return m_trackingSpace != null ? m_trackingSpace.TransformPoint(local) : local;
        }

        private Quaternion Rotation(OVRPlugin.BoneId id)
        {
            var local = m_state.BoneRotations[(int)id].FromFlippedZQuatf();
            return m_trackingSpace != null ? m_trackingSpace.rotation * local : local;
        }
    }
}
