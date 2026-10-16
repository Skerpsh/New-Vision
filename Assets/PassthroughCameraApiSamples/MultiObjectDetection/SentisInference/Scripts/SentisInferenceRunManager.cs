// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Meta.XR;
using Meta.XR.Samples;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class SentisInferenceRunManager : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;
        [SerializeField] private DetectionUiMenuManager m_uiMenuManager;
        [SerializeField] private DetectionManager m_detectionManager;

        [Header("Sentis Model config")]
        [SerializeField] private BackendType m_backend = BackendType.CPU;
        [SerializeField] private ModelAsset m_sentisModel;
        [Tooltip("Models selectable in the in-app menu (filled by 'New Vision > Convert Models'). Empty = only Sentis Model.")]
        [SerializeField] private ModelAsset[] m_modelVariants;
        [SerializeField] private TextAsset m_labelsAsset;
        [SerializeField, Range(0, 1)] private float m_iouThreshold = 0.6f;
        [SerializeField, Range(0, 1)] private float m_scoreThreshold = 0.23f;

        [Header("UI display references")]
        [SerializeField] private SentisInferenceUiManager m_uiInference;

        [Header("[Editor Only] Convert to Sentis")]
        public ModelAsset OnnxModel;
        [Space(40)]

        // Saved by name, so adding or removing models later doesn't switch to a different one
        private const string ModelPrefsKey = "NewVision.ModelName";
        private const string DefaultModelName = "yolo11n_416";

        private Worker m_engine;
        private Vector2Int m_inputSize;
        private ClassFilterMenu m_classFilter;
        private readonly List<(int classId, Vector4 boundingBox, float score)> m_detections = new List<(int classId, Vector4 boundingBox, float score)>();
        private ModelAsset[] m_models;
        private int m_modelIndex;
        private int m_requestedModelIndex;
        private RenderTexture m_letterboxTexture;
        private Material m_letterboxMaterial;

        private void Awake()
        {
            m_models = m_modelVariants != null && m_modelVariants.Length > 0 ? m_modelVariants : new[] { m_sentisModel };
            var savedName = PlayerPrefs.GetString(ModelPrefsKey, DefaultModelName);
            m_modelIndex = System.Array.FindIndex(m_models, m => m.name == savedName);
            if (m_modelIndex < 0)
            {
                m_modelIndex = System.Array.FindIndex(m_models, m => m.name == DefaultModelName);
            }
            m_modelIndex = Mathf.Max(0, m_modelIndex);
            m_requestedModelIndex = m_modelIndex;
            LoadModel(m_modelIndex);

            m_letterboxMaterial = new Material(Resources.Load<Shader>("NewVisionLetterbox"));
        }

        private void LoadModel(int index)
        {
            m_engine?.Dispose();
            var model = ModelLoader.Load(m_models[index]);
            var inputShape = model.inputs[0].shape;
            m_inputSize = new Vector2Int(inputShape.Get(3), inputShape.Get(2)); // (width, height)
            m_engine = new Worker(model, m_backend);
            m_modelIndex = index;
            Debug.Log($"[NewVision] model {m_models[index].name}, input {m_inputSize.x}x{m_inputSize.y}");
        }

        private static string ModelDisplayName(ModelAsset model)
        {
            // yolo11n_320 -> "YOLO11n 320 (fast)", yolo12s_416 -> "YOLO12s 416 (accurate)", my_model -> "my model"
            var name = model.name.Replace("yolo", "YOLO").Replace('_', ' ');
            var size = System.Text.RegularExpressions.Regex.Match(model.name, @"^yolo\d+([nsmlx])").Groups[1].Value;
            return size switch
            {
                "n" => $"{name} (fast)",
                "s" => $"{name} (accurate)",
                "m" or "l" or "x" => $"{name} (accurate, slow)",
                _ => name
            };
        }

        private IEnumerator Start()
        {
            m_uiInference.SetLabels(m_labelsAsset);

            // Menu to choose which classes are detected; boxes of switched-off classes disappear right away
            m_classFilter = gameObject.AddComponent<ClassFilterMenu>();
            m_classFilter.Init(m_labelsAsset);
            m_classFilter.Changed += _ => m_uiInference.ClearAnnotations();
            var modelNames = new string[m_models.Length];
            for (var i = 0; i < m_models.Length; i++)
            {
                modelNames[i] = ModelDisplayName(m_models[i]);
            }
            // The switch itself happens between two inferences (see RunInference)
            m_classFilter.SetModelOptions(modelNames, m_modelIndex, index => m_requestedModelIndex = index);

            // Look at anything for 2 s (or pinch / pull a trigger) and Gemini tells what it is
            gameObject.AddComponent<ObjectInfoLookup>().Init(m_cameraAccess, m_uiInference, () => m_uiMenuManager.IsPaused);

            // Hide the boundary (Guardian) so you can walk around freely, e.g. outdoors. Works while passthrough is on;
            // needs 'Boundary Visibility Support' in the project config (New Vision > Apply App Settings).
            if (OVRManager.instance != null)
            {
                OVRManager.instance.shouldBoundaryVisibilityBeSuppressed = true;
            }

            while (true)
            {
                while (m_uiMenuManager.IsPaused)
                {
                    yield return null;
                }
                yield return RunInference();
            }
        }

        private void OnDestroy()
        {
            m_engine.PeekOutput(0)?.CompleteAllPendingOperations();
            m_engine.PeekOutput(1)?.CompleteAllPendingOperations();
            m_engine.PeekOutput(2)?.CompleteAllPendingOperations();
            m_engine.Dispose();
            if (m_letterboxTexture != null)
            {
                m_letterboxTexture.Release();
                Destroy(m_letterboxTexture);
            }
            Destroy(m_letterboxMaterial);
        }

        /// <summary>
        /// Fits the camera image into the network input without stretching (letterbox, like YOLO is trained).
        /// Returns where the image sits inside the input: (offsetX, offsetY, width, height), all in 0..1.
        /// </summary>
        private Vector4 Letterbox(Texture source)
        {
            if (m_letterboxTexture == null || m_letterboxTexture.width != m_inputSize.x || m_letterboxTexture.height != m_inputSize.y)
            {
                if (m_letterboxTexture != null)
                {
                    m_letterboxTexture.Release();
                    Destroy(m_letterboxTexture);
                }
                m_letterboxTexture = new RenderTexture(m_inputSize.x, m_inputSize.y, 0, RenderTextureFormat.ARGB32);
            }

            var scale = Mathf.Min((float)m_inputSize.x / source.width, (float)m_inputSize.y / source.height);
            var width = source.width * scale / m_inputSize.x;
            var height = source.height * scale / m_inputSize.y;
            // Centered, so the bands are symmetric and the mapping back doesn't depend on the texture's vertical orientation
            var content = new Vector4((1f - width) * 0.5f, (1f - height) * 0.5f, width, height);
            m_letterboxMaterial.SetVector("_Content", content);
            Graphics.Blit(source, m_letterboxTexture, m_letterboxMaterial);
            return content;
        }

        internal static void PreloadModel(ModelAsset modelAsset)
        {
            // Load model
            var model = ModelLoader.Load(modelAsset);
            var inputShape = model.inputs[0].shape;

            // Create engine to run model
            using var worker = new Worker(model, BackendType.CPU);

            // Run inference with an empty image to load the model in the memory. The first inference blocks the main thread for a long time, so we're doing it on the app launch
            Texture tempTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var textureTransform = new TextureTransform().SetDimensions(tempTexture.width, tempTexture.height, 3);
            using var input = new Tensor<float>(new TensorShape(1, 3, inputShape.Get(2), inputShape.Get(3)));
            TextureConverter.ToTensor(tempTexture, input, textureTransform);
            worker.Schedule(input);

            // Complete the inference immediately and destroy the temporary texture
            worker.PeekOutput(0).CompleteAllPendingOperations();
            worker.PeekOutput(1).CompleteAllPendingOperations();
            worker.PeekOutput(2).CompleteAllPendingOperations();
            Destroy(tempTexture);
        }

        private IEnumerator RunInference()
        {
            if (!m_cameraAccess.IsPlaying)
            {
                yield break;
            }

            // Model chosen in the menu: switch between inferences, when no readback is pending
            if (m_requestedModelIndex != m_modelIndex)
            {
                LoadModel(m_requestedModelIndex);
                PlayerPrefs.SetString(ModelPrefsKey, m_models[m_modelIndex].name);
                PlayerPrefs.Save();
                m_uiInference.ClearAnnotations();
            }

            [DllImport("OVRPlugin", CallingConvention = CallingConvention.Cdecl)]
            static extern OVRPlugin.Result ovrp_GetNodePoseStateAtTime(double time, OVRPlugin.Node nodeId, out OVRPlugin.PoseStatef nodePoseState);
            if (!ovrp_GetNodePoseStateAtTime(OVRPlugin.GetTimeInSeconds(), OVRPlugin.Node.Head, out _).IsSuccess())
            {
                Debug.Log("ovrp_GetNodePoseStateAtTime failed, which means 'm_cameraAccess.GetCameraPose()' is not reliable, skipping.");
                yield break;
            }

            var cachedCameraPose = m_cameraAccess.GetCameraPose();
            var inferenceStartTime = Time.realtimeSinceStartup;

            // Update Capture data
            Texture targetTexture = m_cameraAccess.GetTexture();

            // Letterbox the image into the network input, convert it to a Tensor and schedule the inference
            var content = Letterbox(targetTexture);
            var textureTransform = new TextureTransform().SetDimensions(m_inputSize.x, m_inputSize.y, 3);
            using var input = new Tensor<float>(new TensorShape(1, 3, m_inputSize.y, m_inputSize.x));
            TextureConverter.ToTensor(m_letterboxTexture, input, textureTransform);

            // Schedule all model layers
            m_engine.Schedule(input);

            // Get the results. ReadbackAndCloneAsync waits for all layers to complete before returning the result.
            // All three readbacks are requested at once, so we wait for them together instead of one frame after another.
            var boxesAwaiter = (m_engine.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
            var classIDsAwaiter = (m_engine.PeekOutput(1) as Tensor<int>).ReadbackAndCloneAsync().GetAwaiter();
            var scoresAwaiter = (m_engine.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
            while (!boxesAwaiter.IsCompleted || !classIDsAwaiter.IsCompleted || !scoresAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var boxes = boxesAwaiter.GetResult();
            using var classIDs = classIDsAwaiter.GetResult();
            using var scores = scoresAwaiter.GetResult();
            if (boxes.shape[0] == 0)
            {
                yield break;
            }

            if (classIDs.shape[0] == 0)
            {
                Debug.LogError("classIDs.shape[0] == 0");
                yield break;
            }

            if (scores.shape[0] == 0)
            {
                Debug.LogError("scores.shape[0] == 0");
                yield break;
            }

            NonMaxSuppression(m_detections, boxes, classIDs, scores, m_iouThreshold, m_scoreThreshold, m_classFilter.EnabledClasses);
            Debug.Log($"[NewVision] inference {m_models[m_modelIndex].name} {m_backend}: {(Time.realtimeSinceStartup - inferenceStartTime) * 1000f:F0} ms, {m_detections.Count} objects");

            // Boxes are in letterboxed input pixels: map them back to the camera image, scaled to the input size
            for (var i = 0; i < m_detections.Count; i++)
            {
                var (classId, box, score) = m_detections[i];
                box.x = (box.x / m_inputSize.x - content.x) / content.z * m_inputSize.x;
                box.z = (box.z / m_inputSize.x - content.x) / content.z * m_inputSize.x;
                box.y = (box.y / m_inputSize.y - content.y) / content.w * m_inputSize.y;
                box.w = (box.w / m_inputSize.y - content.y) / content.w * m_inputSize.y;
                m_detections[i] = (classId, box, score);
            }

            // If the spatial anchor exists but lost tracking, world positions are unreliable: skip.
            // Without an anchor (e.g. outdoors, where it may fail to be created) boxes are still placed in world space.
            if (!m_cameraAccess.IsPlaying || (m_detectionManager.m_spatialAnchor != null && !m_detectionManager.m_spatialAnchor.IsTracked))
            {
                yield break;
            }

            // Update UI.
            m_uiInference.DrawUIBoxes(m_detections, m_inputSize, cachedCameraPose);
        }

        private static void NonMaxSuppression(List<(int classId, Vector4 boundingBox, float score)> outDetections, Tensor<float> boxes, Tensor<int> classIDs, Tensor<float> scores, float iouThreshold, float scoreThreshold, bool[] enabledClasses)
        {
            outDetections.Clear();

            // Filter by score threshold and by the classes chosen in the menu first,
            // so a switched-off class can't suppress an overlapping box of a wanted class
            List<int> filteredIndices = new List<int>();
            NativeArray<float>.ReadOnly scoresArray = scores.AsReadOnlyNativeArray();
            NativeArray<int>.ReadOnly classIDsArray = classIDs.AsReadOnlyNativeArray();
            for (int i = 0; i < scoresArray.Length; i++)
            {
                if (scoresArray[i] >= scoreThreshold && (enabledClasses == null || enabledClasses[classIDsArray[i]]))
                {
                    filteredIndices.Add(i);
                }
            }

            if (filteredIndices.Count == 0)
            {
                return;
            }

            // Sort filtered indices by scores in descending order
            filteredIndices.Sort((a, b) => scoresArray[b].CompareTo(scoresArray[a]));

            // Apply NMS algorithm
            bool[] suppressed = new bool[filteredIndices.Count];
            for (int i = 0; i < filteredIndices.Count; i++)
            {
                if (suppressed[i])
                    continue;

                int idx = filteredIndices[i];

                // Add this detection to results
                outDetections.Add((classIDs[idx], GetBox(idx), scoresArray[idx]));

                // Suppress overlapping boxes regardless of class
                for (int j = i + 1; j < filteredIndices.Count; j++)
                {
                    if (suppressed[j])
                        continue;

                    int jdx = filteredIndices[j];

                    float iou = CalculateIoU(GetBox(idx), GetBox(jdx));
                    if (iou > iouThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            Vector4 GetBox(int i) => new Vector4(boxes[i, 0], boxes[i, 1], boxes[i, 2], boxes[i, 3]);
        }

        internal static float CalculateIoU(Vector4 boxA, Vector4 boxB)
        {
            // Boxes are in format (topLeftX, topLeftY, bottomRightX, bottomRightY)
            // Calculate intersection coordinates
            float x1 = Mathf.Max(boxA.x, boxB.x);
            float y1 = Mathf.Max(boxA.y, boxB.y);
            float x2 = Mathf.Min(boxA.z, boxB.z);
            float y2 = Mathf.Min(boxA.w, boxB.w);

            // Calculate intersection area
            float intersectionWidth = Mathf.Max(0, x2 - x1);
            float intersectionHeight = Mathf.Max(0, y2 - y1);
            float intersectionArea = intersectionWidth * intersectionHeight;

            // Calculate individual box areas
            float boxAArea = (boxA.z - boxA.x) * (boxA.w - boxA.y);
            float boxBArea = (boxB.z - boxB.x) * (boxB.w - boxB.y);

            // Calculate union area
            float unionArea = boxAArea + boxBArea - intersectionArea;

            // Return IoU (Intersection over Union)
            if (unionArea == 0)
                return 0;

            return intersectionArea / unionArea;
        }
    }
}
