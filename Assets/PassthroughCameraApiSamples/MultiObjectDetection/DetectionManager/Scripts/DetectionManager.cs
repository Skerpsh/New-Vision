// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using Meta.XR.Samples;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionManager : MonoBehaviour
    {
        [SerializeField] private DetectionUiMenuManager m_menuManager;
        [SerializeField] private SentisInferenceUiManager m_uiInference;

        // Keeps the detection boxes fixed in the room even if head tracking is lost for a moment
        internal OVRSpatialAnchor m_spatialAnchor;
        private bool m_isHeadsetTracking;

        private void Awake()
        {
            StartCoroutine(UpdateSpatialAnchor());
            OVRManager.TrackingLost += OnTrackingLost;
            OVRManager.TrackingAcquired += OnTrackingAcquired;
        }

        private void OnDestroy()
        {
            EraseSpatialAnchor();
            OVRManager.TrackingLost -= OnTrackingLost;
            OVRManager.TrackingAcquired -= OnTrackingAcquired;
        }

        private void OnTrackingLost() => m_isHeadsetTracking = false;
        private void OnTrackingAcquired() => m_isHeadsetTracking = true;

        private IEnumerator UpdateSpatialAnchor()
        {
            while (true)
            {
                yield return null;
                if (m_spatialAnchor == null)
                {
                    yield return CreateSpatialAnchorAndSave();
                    if (m_spatialAnchor == null)
                    {
                        yield return new WaitForSeconds(5f);
                        continue;
                    }
                }

                if (!m_spatialAnchor.IsTracked)
                {
                    yield return RestoreSpatialAnchorTracking();
                }
            }

            IEnumerator CreateSpatialAnchorAndSave()
            {
                m_spatialAnchor = m_uiInference.ContentParent.gameObject.AddComponent<OVRSpatialAnchor>();

                // Wait for localization because SaveAnchorAsync() requires the anchor to be localized first.
                while (true)
                {
                    if (m_spatialAnchor == null)
                    {
                        // Spatial Anchor destroys itself when creation fails.
                        yield break;
                    }
                    if (m_spatialAnchor.Localized)
                    {
                        break;
                    }
                    yield return null;
                }

                // Save the anchor.
                var awaiter = m_spatialAnchor.SaveAnchorAsync().GetAwaiter();
                while (!awaiter.IsCompleted)
                {
                    yield return null;
                }
                var saveAnchorResult = awaiter.GetResult();
                if (!saveAnchorResult.Success)
                {
                    string message = $"SaveAnchorAsync() failed with error {saveAnchorResult}. Please check that the USE_ANCHOR_API permission is granted.\n" +
                                     "Also check that your app is uploaded to the Meta Horizon Store: https://developers.meta.com/vr/documentation/unity/unity-ssa-ts/#app-is-untrusted";
                    LogSpatialAnchor(message, LogType.Error);
                    EraseSpatialAnchor();
                    m_menuManager.m_labelInformation.text = message;
                    yield break;
                }
                LogSpatialAnchor("created");
            }

            IEnumerator RestoreSpatialAnchorTracking()
            {
                // Try to restore spatial anchor tracking. If restoration fails, erase it.
                LogSpatialAnchor("tracking was lost, restoring...");
                const int numRetries = 20;
                for (int i = 0; i < numRetries; i++)
                {
                    yield return new WaitForSeconds(1f);
                    if (!m_isHeadsetTracking)
                    {
                        LogSpatialAnchor($"{nameof(m_isHeadsetTracking)} is false, retrying ({i})");
                        continue;
                    }

                    var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>(1);
                    var awaiter = OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[]
                    {
                        m_spatialAnchor.Uuid
                    }, unboundAnchors).GetAwaiter();
                    while (!awaiter.IsCompleted)
                    {
                        yield return null;
                    }
                    var loadResult = awaiter.GetResult();
                    if (!loadResult.Success)
                    {
                        LogSpatialAnchor($"LoadUnboundAnchorsAsync() failed {loadResult.Status}, retrying ({i})", LogType.Error);
                        continue;
                    }
                    if (unboundAnchors.Count != 0)
                    {
                        LogSpatialAnchor($"LoadUnboundAnchorsAsync() unexpected count:{unboundAnchors.Count}, retrying ({i})", LogType.Error);
                        continue;
                    }
                    yield return null;
                    if (!m_spatialAnchor.IsTracked)
                    {
                        LogSpatialAnchor($"tracking is not restored, retrying ({i})");
                        continue;
                    }

                    LogSpatialAnchor("tracking was restored successfully");
                    yield break;
                }

                LogSpatialAnchor($"tracking restoration failed after {numRetries} retries", LogType.Warning);
                EraseSpatialAnchor();
                m_uiInference.ClearAnnotations();
            }
        }

        private void EraseSpatialAnchor()
        {
            if (m_spatialAnchor != null)
            {
                LogSpatialAnchor("EraseSpatialAnchor");
                m_spatialAnchor.EraseAnchorAsync();
                DestroyImmediate(m_spatialAnchor);
                m_spatialAnchor = null;
            }
        }

        private static void LogSpatialAnchor(string message, LogType logType = LogType.Log)
        {
            Debug.unityLogger.Log(logType, $"{nameof(OVRSpatialAnchor)}: {message}");
        }
    }
}
