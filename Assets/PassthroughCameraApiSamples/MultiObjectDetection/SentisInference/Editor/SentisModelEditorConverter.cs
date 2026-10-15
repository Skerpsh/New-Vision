// Copyright (c) Meta Platforms, Inc. and affiliates.
using Meta.XR.Samples;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection.Editor
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    [CustomEditor(typeof(SentisInferenceRunManager))]
    public class SentisModelEditorConverter : UnityEditor.Editor
    {
        private SentisInferenceRunManager m_targetClass;
        private float m_iouThreshold;
        private float m_scoreThreshold;

        public void OnEnable()
        {
            m_targetClass = (SentisInferenceRunManager)target;
            m_iouThreshold = serializedObject.FindProperty("m_iouThreshold").floatValue;
            m_scoreThreshold = serializedObject.FindProperty("m_scoreThreshold").floatValue;
        }

        public override void OnInspectorGUI()
        {
            _ = DrawDefaultInspector();

            if (GUILayout.Button("Generate Sentis model from Onnx Model (and use it)"))
            {
                OnEnable(); // Get the latest values from the serialized object
                ConvertModel(); // convert the ONNX model to sentis
            }
        }

        private void ConvertModel()
        {
            var sentisPath = ConvertToSentis(m_targetClass.OnnxModel);

            // use the generated model
            serializedObject.Update();
            serializedObject.FindProperty("m_sentisModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ModelAsset>(sentisPath);
            _ = serializedObject.ApplyModifiedProperties();
            Debug.Log($"[NewVision] Sentis model saved to {sentisPath} and assigned");
        }

        /// <summary>
        /// Converts a YOLO ONNX model (output shape (1, 84, N)) to a Sentis model with outputs (corners, classIDs, scores),
        /// saved next to the ONNX file, e.g. yolo11n_320.onnx -> yolo11n_320.sentis. Returns the asset path of the result.
        /// </summary>
        public static string ConvertToSentis(ModelAsset onnxModel)
        {
            //Load model
            var model = ModelLoader.Load(onnxModel);

            //Here we transform the output of the model by feeding it through a Non-Max-Suppression layer.
            var graph = new FunctionalGraph();
            var input = graph.AddInput(model, 0);

            var centersToCornersData = new[]
            {
                        1,      0,      1,      0,
                        0,      1,      0,      1,
                        -0.5f,  0,      0.5f,   0,
                        0,      -0.5f,  0,      0.5f
            };
            var centersToCorners = Functional.Constant(new TensorShape(4, 4), centersToCornersData);
            var modelOutput = Functional.Forward(model, input)[0];  //shape(1,N,85)
            // Following for yolo model. in (1, 84, N) out put shape
            var boxCoords = modelOutput[0, ..4, ..].Transpose(0, 1);
            var allScores = modelOutput[0, 4.., ..].Transpose(0, 1);
            var scores = Functional.ReduceMax(allScores, 1);                                    //shape=(N)
            var classIDs = Functional.ArgMax(allScores, 1);                                     //shape=(N)
            var corners = Functional.MatMul(boxCoords, centersToCorners);                    //shape=(N,4)
            var modelFinal = graph.Compile(corners, classIDs, scores);

            //Export the model to Sentis format next to the ONNX file.
            // Float16 weights keep almost full accuracy (Uint8 noticeably hurts small models) at 2x the size of Uint8.
            var onnxPath = AssetDatabase.GetAssetPath(onnxModel);
            var sentisPath = System.IO.Path.ChangeExtension(onnxPath, ".sentis");
            ModelQuantizer.QuantizeWeights(QuantizationType.Float16, ref modelFinal);
            ModelWriter.Save(sentisPath, modelFinal);
            AssetDatabase.Refresh();
            return sentisPath;
        }
    }
}
