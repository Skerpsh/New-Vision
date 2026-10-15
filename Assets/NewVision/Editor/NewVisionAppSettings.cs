using System.Linq;
using PassthroughCameraSamples.MultiObjectDetection;
using PassthroughCameraSamples.MultiObjectDetection.Editor;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace NewVision.Editor
{
    /// <summary>
    /// "New Vision" editor menu:
    /// - Apply App Settings: app name, package id, icon, and permission to hide the boundary (for walking around / outdoors).
    /// - Convert Models: converts every ONNX model in the Model folder to Sentis and makes them selectable in the app
    ///   (in-app menu, "Model" row). To add a model: export it with Tools/export_model.py, then run this menu item.
    /// </summary>
    public static class NewVisionAppSettings
    {
        private const string ProductName = "New Vision";
        private const string CompanyName = "New Vision";
        private const string PackageId = "com.newvision.app";
        private const string IconPath = "Assets/NewVision/AppIcon.png";
        private const string ModelFolder = "Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model";
        private const string ExcludedSampleModel = "yolov9onnx.onnx";
        private const string InferencePrefabPath = "Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Prefabs/SentisInferenceManagerPrefab.prefab";

        [MenuItem("New Vision/Apply App Settings")]
        public static void Apply()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogError($"[NewVision] Icon not found at {IconPath}");
            }
            else
            {
                // Default icon: used by every platform that has no icon of its own (Android included)
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            }

            // Lets the app hide the boundary (Guardian) while passthrough is on, so you can walk freely, e.g. outdoors.
            // The app requests it at runtime (SentisInferenceRunManager).
            var projectConfig = OVRProjectConfig.CachedProjectConfig;
            projectConfig.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(projectConfig);

            AssetDatabase.SaveAssets();
            Debug.Log($"[NewVision] App settings applied: name '{ProductName}', package '{PackageId}', icon '{IconPath}', boundary visibility supported");
        }

        [MenuItem("New Vision/Convert Models")]
        public static void ConvertModels()
        {
            // Every ONNX model in the folder (e.g. yolo11n_320.onnx, yolo11s_416.onnx, a custom-trained one),
            // except the original sample model. Models must output (1, 4 + classes, N) like Ultralytics YOLO exports
            // and use the same classes as the labels file.
            var onnxPaths = AssetDatabase.FindAssets("t:ModelAsset", new[] { ModelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".onnx") && !p.EndsWith(ExcludedSampleModel))
                .OrderBy(p => p)
                .ToArray();
            if (onnxPaths.Length == 0)
            {
                Debug.LogError($"[NewVision] No .onnx models found in {ModelFolder}");
                return;
            }

            var sentisModels = new ModelAsset[onnxPaths.Length];
            for (var i = 0; i < onnxPaths.Length; i++)
            {
                _ = EditorUtility.DisplayCancelableProgressBar("New Vision", $"Converting {onnxPaths[i]}", (float)i / onnxPaths.Length);
                var sentisPath = SentisModelEditorConverter.ConvertToSentis(AssetDatabase.LoadAssetAtPath<ModelAsset>(onnxPaths[i]));
                sentisModels[i] = AssetDatabase.LoadAssetAtPath<ModelAsset>(sentisPath);
                Debug.Log($"[NewVision] Converted {onnxPaths[i]} -> {sentisPath}");
            }
            EditorUtility.ClearProgressBar();

            var root = PrefabUtility.LoadPrefabContents(InferencePrefabPath);
            try
            {
                var runManager = root.GetComponentInChildren<SentisInferenceRunManager>(true);
                var so = new SerializedObject(runManager);
                var variants = so.FindProperty("m_modelVariants");
                variants.arraySize = sentisModels.Length;
                for (var i = 0; i < sentisModels.Length; i++)
                {
                    variants.GetArrayElementAtIndex(i).objectReferenceValue = sentisModels[i];
                }
                so.FindProperty("m_sentisModel").objectReferenceValue = sentisModels[0];
                // The ONNX reference is editor-only; clearing it keeps the ONNX file out of the APK
                so.FindProperty("OnnxModel").objectReferenceValue = null;
                _ = so.ApplyModifiedPropertiesWithoutUndo();
                _ = PrefabUtility.SaveAsPrefabAsset(root, InferencePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Debug.Log($"[NewVision] {sentisModels.Length} models are selectable in the app: " +
                      string.Join(", ", sentisModels.Select(m => m.name)));
        }
    }
}
