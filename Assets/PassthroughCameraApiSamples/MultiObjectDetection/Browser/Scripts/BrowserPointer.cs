// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Input for the browser, close to how the Quest system UI feels:
    /// - a ray from a controller (trigger = press, thumbstick = scroll) or a hand (index pinch = press);
    /// - the press uses where the ray pointed a moment before the pinch closed, because pinching pulls the hand ray off target;
    /// - the index fingertips for touching the panel directly.
    /// The hand or controller that pressed last becomes the active ray.
    /// </summary>
    public class BrowserPointer : MonoBehaviour
    {
        private enum Source { None, RightController, LeftController, RightHand, LeftHand }

        private const float HandSmoothing = 20f;
        private const float PinchPress = 0.9f;
        private const float PinchRelease = 0.6f;
        /// <summary>How far back the press position is taken from, for hands.</summary>
        private const float PinchRewindSeconds = 0.12f;
        private const int HistorySize = 32;

        private Transform m_trackingSpace;
        private LineRenderer m_line;
        private Source m_source = Source.None;
        private Ray m_ray;
        private bool m_pressed;
        private bool m_wasPressed;
        private float m_pinchStrength;
        private OVRPlugin.HandState m_handState;
        private readonly bool[] m_handPinched = new bool[2];
        private readonly float[] m_handStrength = new float[2];
        private readonly Ray[] m_history = new Ray[HistorySize];
        private readonly float[] m_historyTime = new float[HistorySize];
        private int m_historyIndex;
        private OVRSkeleton[] m_skeletons;
        private float m_nextSkeletonSearch;

        public bool Valid => m_source != Source.None;
        /// <summary>The (smoothed) ray right now.</summary>
        public Ray Ray => m_ray;
        /// <summary>Ray to use for the press that just started: for hands, where it pointed just before the pinch.</summary>
        public Ray PressRay { get; private set; }
        public bool Pressed => m_pressed;
        public bool PressStarted => m_pressed && !m_wasPressed;
        public bool PressEnded => !m_pressed && m_wasPressed;
        public bool IsHand => m_source is Source.RightHand or Source.LeftHand;
        /// <summary>0..1 how closed the pinch / trigger is, for the cursor.</summary>
        public float PressAmount => m_pinchStrength;
        /// <summary>Thumbstick (controllers), each axis -1..1 with a dead zone; zero for hands.</summary>
        public Vector2 Stick { get; private set; }

        public void Init(Transform trackingSpace)
        {
            m_trackingSpace = trackingSpace;
            m_line = gameObject.AddComponent<LineRenderer>();
            m_line.useWorldSpace = true;
            m_line.positionCount = 2;
            m_line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.003f), new Keyframe(1f, 0.0012f));
            m_line.material = new Material(Shader.Find("Sprites/Default"));
            m_line.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.35f, 0.35f), new GradientAlphaKey(0.7f, 1f) }
            };
            m_line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_line.receiveShadows = false;
            m_line.enabled = false;
        }

        /// <summary>Reads the input; call once per frame before using the ray.</summary>
        public void Poll()
        {
            m_wasPressed = m_pressed;

            PollHands(); // hands only: New Vision doesn't use controllers

            m_history[m_historyIndex] = m_ray;
            m_historyTime[m_historyIndex] = Time.time;
            m_historyIndex = (m_historyIndex + 1) % HistorySize;
            if (PressStarted)
            {
                PressRay = IsHand ? RayAt(Time.time - PinchRewindSeconds) : m_ray;
            }
        }

        private void PollHands()
        {
            var rightValid = ReadHand(OVRPlugin.Hand.HandRight, out var rightRay);
            var leftValid = ReadHand(OVRPlugin.Hand.HandLeft, out var leftRay);
            var leftStarted = m_handPinched[0] && m_handStrength[0] > PinchPress && !m_wasPressed;
            var rightStarted = m_handPinched[1] && m_handStrength[1] > PinchPress && !m_wasPressed;
            if (leftValid && leftStarted && !(rightValid && rightStarted))
            {
                m_source = Source.LeftHand;
            }
            else if (rightValid && (m_source != Source.LeftHand || !leftValid || rightStarted))
            {
                m_source = Source.RightHand;
            }
            else if (!leftValid && !rightValid)
            {
                m_source = Source.None;
            }
            if (m_source == Source.LeftHand && !leftValid)
            {
                m_source = rightValid ? Source.RightHand : Source.None;
            }

            if (m_source == Source.None)
            {
                m_pressed = false;
                m_pinchStrength = 0f;
                return;
            }
            var index = m_source == Source.LeftHand ? 0 : 1;
            var ray = index == 0 ? leftRay : rightRay;
            var t = 1f - Mathf.Exp(-HandSmoothing * Time.deltaTime);
            if (m_ray.direction.sqrMagnitude < 0.5f || Vector3.Distance(m_ray.origin, ray.origin) > 0.3f)
            {
                t = 1f; // first frame or the other hand: no smoothing from a stale ray
            }
            m_ray = new Ray(Vector3.Lerp(m_ray.origin, ray.origin, t), Vector3.Slerp(m_ray.direction, ray.direction, t));
            m_pressed = m_handPinched[index];
            m_pinchStrength = m_handStrength[index];
        }

        private bool ReadHand(OVRPlugin.Hand hand, out Ray ray)
        {
            ray = default;
            var index = hand == OVRPlugin.Hand.HandLeft ? 0 : 1;
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref m_handState) ||
                (m_handState.Status & OVRPlugin.HandStatus.InputStateValid) == 0)
            {
                m_handPinched[index] = false;
                m_handStrength[index] = 0f;
                return false;
            }
            var pose = m_handState.PointerPose.ToOVRPose();
            ray = new Ray(ToWorld(pose.position), ToWorld(pose.orientation) * Vector3.forward);
            var strength = m_handState.PinchStrength != null && m_handState.PinchStrength.Length > 1 ? m_handState.PinchStrength[1] : 0f;
            m_handStrength[index] = strength;
            m_handPinched[index] = m_handPinched[index] ? strength > PinchRelease : strength > PinchPress;
            return true;
        }

        private Ray RayAt(float time)
        {
            // Newest entry not newer than 'time'
            for (var i = 1; i <= HistorySize; i++)
            {
                var index = (m_historyIndex - i + HistorySize) % HistorySize;
                if (m_historyTime[index] <= time && m_history[index].direction.sqrMagnitude > 0.5f)
                {
                    return m_history[index];
                }
            }
            return m_ray;
        }

        /// <summary>Index fingertip of a tracked hand (left = 0, right = 1), for touching the panel directly.</summary>
        public bool TryGetFingertip(int hand, out Vector3 position)
        {
            position = default;
            if ((m_skeletons == null || m_skeletons.Length == 0) && Time.time >= m_nextSkeletonSearch)
            {
                m_skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None);
                m_nextSkeletonSearch = Time.time + 2f;
            }
            if (m_skeletons == null)
            {
                return false;
            }
            foreach (var skeleton in m_skeletons)
            {
                if (skeleton == null || !skeleton.IsInitialized || !skeleton.IsDataValid)
                {
                    continue;
                }
                var type = skeleton.GetSkeletonType();
                var isLeft = type is OVRSkeleton.SkeletonType.HandLeft or OVRSkeleton.SkeletonType.XRHandLeft;
                var isRight = type is OVRSkeleton.SkeletonType.HandRight or OVRSkeleton.SkeletonType.XRHandRight;
                if ((hand == 0 && !isLeft) || (hand == 1 && !isRight))
                {
                    continue;
                }
                var tipId = type is OVRSkeleton.SkeletonType.XRHandLeft or OVRSkeleton.SkeletonType.XRHandRight
                    ? OVRSkeleton.BoneId.XRHand_IndexTip
                    : OVRSkeleton.BoneId.Hand_IndexTip;
                foreach (var bone in skeleton.Bones)
                {
                    if (bone.Id == tipId && bone.Transform != null)
                    {
                        position = bone.Transform.position;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Shows the laser from the hand to <paramref name="end"/>, or hides it.</summary>
        public void DrawLaser(bool visible, Vector3 end)
        {
            m_line.enabled = visible && Valid;
            if (m_line.enabled)
            {
                m_line.SetPosition(0, m_ray.origin);
                m_line.SetPosition(1, end);
            }
        }

        private Vector3 ToWorld(Vector3 local) => m_trackingSpace != null ? m_trackingSpace.TransformPoint(local) : local;
        private Quaternion ToWorld(Quaternion local) => m_trackingSpace != null ? m_trackingSpace.rotation * local : local;
    }
}
