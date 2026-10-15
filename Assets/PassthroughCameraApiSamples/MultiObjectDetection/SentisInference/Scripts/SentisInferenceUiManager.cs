// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using Meta.XR;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class SentisInferenceUiManager : MonoBehaviour
    {
        [Header("Placement configuration")]
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [SerializeField] private RectTransform m_detectionBoxPrefab;
        [Tooltip("How fast boxes glide to the latest detection. Higher = snappier, lower = smoother. 0 = no smoothing.")]
        [SerializeField] private float m_boxSmoothingSpeed = 15f;
        [Tooltip("Frame line thickness per meter of distance to the head, so lines look equally thin near and far.")]
        [SerializeField] private float m_frameThicknessPerMeter = 0.004f;
        [Tooltip("Label scale per meter of distance to the head, so text has the same apparent size near and far.")]
        [SerializeField] private float m_labelScalePerMeter = 0.0002f;
        [Space(10)]
        public UnityEvent<int> OnObjectsDetected;

        internal readonly List<BoundingBoxData> m_boxDrawn = new();
        private string[] m_labels;
        private readonly List<BoundingBoxData> m_boxPool = new();
        private Transform m_head;

        internal class BoundingBoxData
        {
            public string ClassName;
            public int ClassId;
            public RectTransform BoxRectTransform;
            public SharpFrame Frame;
            public Text Label;
            public float lastUpdateTime;

            // Latest detection result; the box glides towards it in Update()
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public Vector2 TargetSize;
            public bool IsNew;
        }

        private void Awake()
        {
            m_detectionBoxPrefab.gameObject.SetActive(false);
            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null)
            {
                m_head = cameraRig.centerEyeAnchor;
            }
            else if (Camera.main != null)
            {
                m_head = Camera.main.transform;
            }
        }

        /// <summary>
        /// Rotation that faces the head while keeping the box upright: its top and bottom edges are always horizontal
        /// (no roll), no matter how the head is tilted.
        /// </summary>
        private Quaternion UprightRotationFacingHead(Vector3 boxPosition, Quaternion fallback)
        {
            if (m_head == null)
            {
                return fallback;
            }
            var forward = boxPosition - m_head.position;
            // Looking straight up/down at the box: the horizontal direction is undefined, keep the current one
            return Vector3.Cross(forward, Vector3.up).sqrMagnitude < 1e-6f ? fallback : Quaternion.LookRotation(forward, Vector3.up);
        }

        private void Update()
        {
            // Remove boxes that haven't been updated recently
            for (int i = m_boxDrawn.Count - 1; i >= 0; i--)
            {
                var box = m_boxDrawn[i];
                const float timeToPersistBoxes = 3f;
                if (Time.time - box.lastUpdateTime > timeToPersistBoxes)
                {
                    ReturnToPool(box);
                    m_boxDrawn.RemoveAt(i);
                }
            }

            // Move boxes smoothly towards their latest detected position and size (frame-rate independent exponential smoothing).
            // Rotation is not interpolated: every frame the box faces the current head position and stays level with the horizon.
            var t = m_boxSmoothingSpeed > 0f ? 1f - Mathf.Exp(-m_boxSmoothingSpeed * Time.deltaTime) : 1f;
            foreach (var box in m_boxDrawn)
            {
                var rectTransform = box.BoxRectTransform;
                var position = Vector3.Lerp(rectTransform.position, box.TargetPosition, t);
                rectTransform.SetPositionAndRotation(position, UprightRotationFacingHead(position, rectTransform.rotation));
                rectTransform.sizeDelta = Vector2.Lerp(rectTransform.sizeDelta, box.TargetSize, t);
                if (m_head != null)
                {
                    var distance = Mathf.Max(0.3f, Vector3.Distance(m_head.position, position));
                    var thickness = distance * m_frameThicknessPerMeter;
                    if (box.Frame != null)
                    {
                        box.Frame.Thickness = thickness;
                    }
                    if (box.Label != null)
                    {
                        // Keep the label the same apparent size at any distance, sitting just above the top edge of the frame
                        var labelRect = box.Label.rectTransform;
                        labelRect.localScale = Vector3.one * (distance * m_labelScalePerMeter);
                        labelRect.anchoredPosition = new Vector2(0f, thickness);
                    }
                }
            }
        }

        public void SetLabels(TextAsset labelsAsset)
        {
            // Parse neural net labels
            m_labels = labelsAsset.text.Split('\n');
            for (var i = 0; i < m_labels.Length; i++)
            {
                m_labels[i] = m_labels[i].Trim(); // drop '\r' of Windows line endings
            }
        }

        public void DrawUIBoxes(List<(int classId, Vector4 boundingBox, float score)> detections, Vector2 inputSize, Pose cameraPose)
        {
            if (detections.Count == 0)
            {
                OnObjectsDetected?.Invoke(0);
                return;
            }

            OnObjectsDetected?.Invoke(detections.Count);

            // Draw the bounding boxes
            for (var i = 0; i < detections.Count; i++)
            {
                var detection = detections[i];
                float x1 = detection.boundingBox[0];
                float y1 = detection.boundingBox[1];
                float x2 = detection.boundingBox[2];
                float y2 = detection.boundingBox[3];
                Rect rect = new Rect(x1, y1, x2 - x1, y2 - y1);
                // Rect rect = Rect.MinMaxRect(x1, y1, x2, y2); // todo

                Vector2 normalizedCenter = rect.center / inputSize;

                // Get the object class name
                var classname = m_labels[detection.classId];

                // Get the 3D marker world position using Depth Raycast
                var ray = m_cameraAccess.ViewportPointToRay(new Vector2(normalizedCenter.x, 1.0f - normalizedCenter.y), cameraPose);
                var worldPos = m_environmentRaycast.Raycast(ray);
                if (!worldPos.HasValue)
                {
                    Debug.Log($"RaycastManager failed, ray:{ray}, cameraPose:{cameraPose}");
                    continue;
                }
                var normRect = new Rect(
                    rect.x / inputSize.x,
                    1f - rect.yMax / inputSize.y,
                    rect.width / inputSize.x,
                    rect.height / inputSize.y
                );

                // Calculate distance and center point first
                float distance = Vector3.Distance(cameraPose.position, worldPos.Value);
                var worldSpaceCenter = m_cameraAccess.ViewportPointToRay(normRect.center, cameraPose).GetPoint(distance);
                var normal = (worldSpaceCenter - cameraPose.position).normalized;

                // Intersect corner rays with the plane perpendicular to the camera view
                var plane = new Plane(normal, worldSpaceCenter);
                var minRay = m_cameraAccess.ViewportPointToRay(normRect.min, cameraPose);
                var maxRay = m_cameraAccess.ViewportPointToRay(normRect.max, cameraPose);
                plane.Raycast(minRay, out float intersectionDistanceMin);
                plane.Raycast(maxRay, out float intersectionDistanceMax);
                var min = minRay.GetPoint(intersectionDistanceMin);
                var max = maxRay.GetPoint(intersectionDistanceMax);

                // Transform world-space positions to camera's local space to get 2D size
                var topLeftLocal = Quaternion.Inverse(cameraPose.rotation) * (min - cameraPose.position);
                var bottomRightLocal = Quaternion.Inverse(cameraPose.rotation) * (max - cameraPose.position);
                var size = new Vector2(
                    Mathf.Abs(bottomRightLocal.x - topLeftLocal.x),
                    Mathf.Abs(bottomRightLocal.y - topLeftLocal.y));

                var boxData = GetOrCreateBoundingBoxData(detection.classId, worldSpaceCenter, size);
                var boxRectTransform = boxData.BoxRectTransform;
                boxData.Label.text = $"{classname} {detection.score * 100f:0}%";
                boxData.TargetPosition = worldSpaceCenter;
                boxData.TargetRotation = Quaternion.LookRotation(normal);
                boxData.TargetSize = size;
                if (boxData.IsNew)
                {
                    // A freshly shown box appears right on the object instead of flying in
                    boxRectTransform.SetPositionAndRotation(boxData.TargetPosition, UprightRotationFacingHead(boxData.TargetPosition, boxData.TargetRotation));
                    boxRectTransform.sizeDelta = boxData.TargetSize;
                    boxData.IsNew = false;
                }
                boxData.lastUpdateTime = Time.time;
            }
        }

        private BoundingBoxData GetOrCreateBoundingBoxData(int classId, Vector3 worldSpaceCenter, Vector2 worldSpaceSize)
        {
            BoundingBoxData reusedBox = null;
            for (int i = m_boxDrawn.Count - 1; i >= 0; i--)
            {
                var box = m_boxDrawn[i];
                var localPos = box.BoxRectTransform.InverseTransformPoint(worldSpaceCenter);
                var newBox = new Vector4(
                    localPos.x - worldSpaceSize.x * 0.5f,
                    localPos.y - worldSpaceSize.y * 0.5f,
                    localPos.x + worldSpaceSize.x * 0.5f,
                    localPos.y + worldSpaceSize.y * 0.5f
                );

                var sizeDelta = box.BoxRectTransform.sizeDelta;
                var currentBox = new Vector4(
                    -sizeDelta.x * 0.5f,
                    -sizeDelta.y * 0.5f,
                    sizeDelta.x * 0.5f,
                    sizeDelta.y * 0.5f);

                if (box.ClassId == classId)
                {
                    // If the new box overlaps with an existing one of the same class, reuse it
                    if (SentisInferenceRunManager.CalculateIoU(newBox, currentBox) > 0f)
                    {
                        if (reusedBox == null)
                        {
                            reusedBox = box;
                        }
                        else
                        {
                            // Same overlapping class - remove the existing box
                            ReturnToPool(box);
                            m_boxDrawn.RemoveAt(i);
                        }
                    }
                }
                // If the new box's IoU with another class is significant, remove the existing box
                else if (SentisInferenceRunManager.CalculateIoU(newBox, currentBox) > 0.1f)
                {
                    // Different overlapping class - remove the existing box
                    ReturnToPool(box);
                    m_boxDrawn.RemoveAt(i);
                }
            }

            if (reusedBox != null)
            {
                return reusedBox;
            }

            // Create a new box
            var newData = GetBoxFromPoolOrCreate();
            newData.ClassId = classId;
            newData.ClassName = m_labels[classId];
            newData.IsNew = true;
            m_boxDrawn.Add(newData);
            return newData;
        }

        private BoundingBoxData GetBoxFromPoolOrCreate()
        {
            if (m_boxPool.Count > 0)
            {
                var pooled = m_boxPool[m_boxPool.Count - 1];
                pooled.BoxRectTransform.gameObject.SetActive(true);
                m_boxPool.RemoveAt(m_boxPool.Count - 1);
                return pooled;
            }

            var boxRectTransform = Instantiate(m_detectionBoxPrefab, ContentParent);
            boxRectTransform.gameObject.SetActive(true);
            var label = boxRectTransform.GetComponentInChildren<Text>();
            // Label grows upwards from the top-left corner of the frame
            label.rectTransform.pivot = Vector2.zero;
            label.alignment = TextAnchor.LowerLeft;
            // Dark outline keeps the green text readable on bright backgrounds
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(4f, -4f);
            return new BoundingBoxData
            {
                BoxRectTransform = boxRectTransform,
                Frame = boxRectTransform.GetComponent<SharpFrame>(),
                Label = label
            };
        }

        internal Transform ContentParent => m_detectionBoxPrefab.parent;

        private void ReturnToPool(BoundingBoxData box)
        {
            box.BoxRectTransform.gameObject.SetActive(false);
            m_boxPool.Add(box);
        }

        internal void ClearAnnotations()
        {
            foreach (var box in m_boxDrawn)
            {
                ReturnToPool(box);
            }
            m_boxDrawn.Clear();
        }
    }
}
