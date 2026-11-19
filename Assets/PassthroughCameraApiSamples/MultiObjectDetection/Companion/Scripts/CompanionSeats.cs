// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Places to sit on, from the room the user scanned in Space Setup (MR Utility Kit): the tops of beds.
    /// Starts MRUK itself if the scene has none. Never opens Space Setup: without a scanned room there are just no seats.
    /// </summary>
    public class CompanionSeats
    {
        private struct Seat
        {
            public Vector3 Center;   // middle of the top surface
            public Vector3 AxisA;    // the two horizontal sides, unit vectors
            public Vector3 AxisB;
            public float HalfA;
            public float HalfB;
        }

        private readonly List<Seat> m_seats = new();

        public int Count => m_seats.Count;

        public void Load()
        {
            var mruk = MRUK.Instance;
            if (mruk == null)
            {
                // Inactive while it's set up: MRUK reads its settings in Awake
                var go = new GameObject("MRUK");
                go.SetActive(false);
                mruk = go.AddComponent<MRUK>();
                mruk.SceneSettings = new MRUK.MRUKSettings { DataSource = MRUK.SceneDataSource.Device, LoadSceneOnStartup = false };
                mruk.EnableWorldLock = false; // it would move the tracking space under the detection boxes
                go.SetActive(true);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
            {
                Debug.LogWarning("[NewVision] companion: no scene permission, no seats");
                return;
            }
#endif
            mruk.RegisterSceneLoadedCallback(Collect);
            if (!mruk.IsInitialized)
            {
                _ = mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
            }
        }

        private void Collect()
        {
            m_seats.Clear();
            foreach (var room in MRUK.Instance.Rooms)
            {
                foreach (var anchor in room.Anchors)
                {
                    if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.BED) && anchor.VolumeBounds.HasValue)
                    {
                        m_seats.Add(ToSeat(anchor.transform, anchor.VolumeBounds.Value));
                    }
                }
            }
            foreach (var s in m_seats)
            {
                Debug.Log($"[NewVision] companion: bed at {s.Center}, {2f * s.HalfA:0.00} x {2f * s.HalfB:0.00} m");
            }
            Debug.Log($"[NewVision] companion: room loaded, {m_seats.Count} bed(s)");
        }

        // The volume's axes are whatever the anchor has: find which one is vertical instead of assuming it
        private static Seat ToSeat(Transform t, Bounds bounds)
        {
            var sides = new[]
            {
                t.TransformVector(new Vector3(bounds.size.x, 0f, 0f)),
                t.TransformVector(new Vector3(0f, bounds.size.y, 0f)),
                t.TransformVector(new Vector3(0f, 0f, bounds.size.z)),
            };
            var vertical = 0;
            for (var i = 1; i < 3; i++)
            {
                if (Mathf.Abs(sides[i].normalized.y) > Mathf.Abs(sides[vertical].normalized.y))
                {
                    vertical = i;
                }
            }
            var a = sides[(vertical + 1) % 3];
            var b = sides[(vertical + 2) % 3];
            var center = t.TransformPoint(bounds.center);
            center.y += Mathf.Abs(sides[vertical].y) / 2f;
            return new Seat
            {
                Center = center,
                AxisA = a.normalized,
                AxisB = b.normalized,
                HalfA = a.magnitude / 2f,
                HalfB = b.magnitude / 2f,
            };
        }

        /// <summary>
        /// The spot on the edge of the nearest bed that faces the user, a little in from the edge, on top of it.
        /// </summary>
        public bool TryGetSeat(Vector3 user, float inset, out Vector3 spot)
        {
            spot = default;
            var best = float.MaxValue;
            foreach (var s in m_seats)
            {
                var d = user - s.Center;
                var u = Mathf.Clamp(Vector3.Dot(d, s.AxisA), -s.HalfA + inset, s.HalfA - inset);
                var v = Mathf.Clamp(Vector3.Dot(d, s.AxisB), -s.HalfB + inset, s.HalfB - inset);
                var candidate = s.Center + s.AxisA * u + s.AxisB * v;
                var distance = Vector3.Distance(Vector3.ProjectOnPlane(candidate - user, Vector3.up), Vector3.zero);
                if (distance < best)
                {
                    best = distance;
                    spot = candidate;
                }
            }
            return best < float.MaxValue;
        }
    }
}
