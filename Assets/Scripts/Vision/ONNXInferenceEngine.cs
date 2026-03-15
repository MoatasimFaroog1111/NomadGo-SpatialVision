using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if ONNX_RUNTIME
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
#endif

namespace NomadGo.Vision
{
    public class ONNXInferenceEngine : MonoBehaviour
    {
        private string modelPath;
        private string labelsPath;
        private int inputWidth = 640;
        private int inputHeight = 640;
        private float confidenceThreshold = 0.45f;
        private float nmsThreshold = 0.5f;
        private int maxDetections = 100;
        private string[] labels;
        private bool isLoaded = false;
        private float lastInferenceTimeMs = 0f;

#if ONNX_RUNTIME
        private InferenceSession session;
        private string inputName;
        private string outputName;
#endif

        public bool IsLoaded => isLoaded;
        public float LastInferenceTimeMs => lastInferenceTimeMs;

        public void Initialize(AppShell.ModelConfig config)
        {
            modelPath = config.path;
            labelsPath = config.labels_path;
            inputWidth = config.input_width;
            inputHeight = config.input_height;
            confidenceThreshold = config.confidence_threshold;
            nmsThreshold = config.nms_threshold;
            maxDetections = config.max_detections;

            LoadLabels();
            LoadModel();
        }

        private void LoadLabels()
        {
            TextAsset labelsAsset = Resources.Load<TextAsset>(labelsPath.Replace(".txt", "").Replace("Models/", ""));
            if (labelsAsset == null)
            {
                labelsAsset = Resources.Load<TextAsset>("labels");
            }

            if (labelsAsset != null)
            {
                labels = labelsAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Debug.Log($"[ONNXEngine] Loaded {labels.Length} labels.");
            }
            else
            {
                labels = new string[] {
                    "bottle", "can", "box", "carton", "bag",
                    "jar", "container", "package", "pouch", "tube"
                };
                Debug.LogWarning("[ONNXEngine] Labels file not found. Using default labels.");
            }
        }

        private void LoadModel()
        {
#if ONNX_RUNTIME
            try
            {
                string fullModelPath = System.IO.Path.Combine(Application.streamingAssetsPath, modelPath);

                if (!System.IO.File.Exists(fullModelPath))
                {
                    TextAsset modelAsset = Resources.Load<TextAsset>(modelPath.Replace(".onnx", ""));
                    if (modelAsset != null)
                    {
                        string tempPath = System.IO.Path.Combine(Application.persistentDataPath, "model.onnx");
                        System.IO.File.WriteAllBytes(tempPath, modelAsset.bytes);
                        fullModelPath = tempPath;
                        Debug.Log($"[ONNXEngine] Model extracted to: {fullModelPath}");
                    }
                    else
                    {
                        Debug.LogError($"[ONNXEngine] Model file not found at: {fullModelPath}");
                        isLoaded = false;
                        return;
                    }
                }

                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                sessionOptions.InterOpNumThreads = 2;
                sessionOptions.IntraOpNumThreads = 2;
                sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;

                session = new InferenceSession(fullModelPath, sessionOptions);

                inputName = session.InputMetadata.Keys.First();
                outputName = session.OutputMetadata.Keys.First();

                Debug.Log($"[ONNXEngine] Model loaded successfully from: {fullModelPath}");
                Debug.Log($"[ONNXEngine] Input: {inputName}, Output: {outputName}");
                Debug.Log($"[ONNXEngine] Input size: {inputWidth}x{inputHeight}");
                Debug.Log($"[ONNXEngine] Confidence threshold: {confidenceThreshold}");
                Debug.Log($"[ONNXEngine] NMS threshold: {nmsThreshold}");

                isLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Failed to load model: {ex.Message}");
                Debug.LogError($"[ONNXEngine] Stack trace: {ex.StackTrace}");
                isLoaded = false;
            }
#else
            Debug.LogWarning("[ONNXEngine] ONNX Runtime not installed. Add ONNX_RUNTIME to Scripting Define Symbols after importing the package.");
            Debug.LogWarning("[ONNXEngine] Running in stub mode — no real inference will be performed.");
            isLoaded = false;
#endif
        }

        public List<DetectionResult> RunInference(Texture2D frame)
        {
#if ONNX_RUNTIME
            if (!isLoaded || session == null)
            {
                Debug.LogWarning("[ONNXEngine] Model not loaded. Skipping inference.");
                return new List<DetectionResult>();
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            float[] inputTensor = PreprocessFrame(frame);

            List<DetectionResult> rawDetections = ExecuteModel(inputTensor, frame.width, frame.height);

            List<DetectionResult> finalDetections = ApplyNMS(rawDetections);

            if (finalDetections.Count > maxDetections)
            {
                finalDetections = finalDetections.Take(maxDetections).ToList();
            }

            stopwatch.Stop();
            lastInferenceTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;

            return finalDetections;
#else
            Debug.LogWarning("[ONNXEngine] ONNX Runtime not available. Returning empty detections.");
            return new List<DetectionResult>();
#endif
        }

        private float[] PreprocessFrame(Texture2D frame)
        {
            RenderTexture rt = RenderTexture.GetTemporary(inputWidth, inputHeight);
            Graphics.Blit(frame, rt);

            Texture2D resized = new Texture2D(inputWidth, inputHeight, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            resized.ReadPixels(new Rect(0, 0, inputWidth, inputHeight), 0, 0);
            resized.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = resized.GetPixels();
            float[] tensor = new float[3 * inputWidth * inputHeight];

            for (int i = 0; i < pixels.Length; i++)
            {
                tensor[i] = pixels[i].r;
                tensor[pixels.Length + i] = pixels[i].g;
                tensor[2 * pixels.Length + i] = pixels[i].b;
            }

            Destroy(resized);
            return tensor;
        }

#if ONNX_RUNTIME
        private List<DetectionResult> ExecuteModel(float[] inputTensor, int originalWidth, int originalHeight)
        {
            var detections = new List<DetectionResult>();

            try
            {
                var tensor = new DenseTensor<float>(inputTensor, new[] { 1, 3, inputHeight, inputWidth });
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(inputName, tensor)
                };

                using (var results = session.Run(inputs))
                {
                    var output = results.First();
                    var outputTensor = output.AsTensor<float>();
                    var outputDims = outputTensor.Dimensions.ToArray();

                    float scaleX = (float)originalWidth / inputWidth;
                    float scaleY = (float)originalHeight / inputHeight;

                    int numClasses = labels.Length;

                    if (outputDims.Length == 3 && outputDims[0] == 1)
                    {
                        int numDetections = outputDims[2];
                        int rowSize = outputDims[1];

                        for (int i = 0; i < numDetections; i++)
                        {
                            float cx = outputTensor[0, 0, i];
                            float cy = outputTensor[0, 1, i];
                            float w  = outputTensor[0, 2, i];
                            float h  = outputTensor[0, 3, i];

                            float bestConf = 0f;
                            int bestClass = -1;

                            for (int c = 0; c < numClasses && (c + 4) < rowSize; c++)
                            {
                                float conf = outputTensor[0, 4 + c, i];
                                if (conf > bestConf)
                                {
                                    bestConf = conf;
                                    bestClass = c;
                                }
                            }

                            if (bestConf >= confidenceThreshold && bestClass >= 0)
                            {
                                float x1 = (cx - w / 2f) * scaleX;
                                float y1 = (cy - h / 2f) * scaleY;
                                float bw = w * scaleX;
                                float bh = h * scaleY;

                                Rect box = new Rect(x1, y1, bw, bh);
                                string label = GetLabel(bestClass);

                                detections.Add(new DetectionResult(bestClass, label, bestConf, box));
                            }
                        }
                    }
                    else if (outputDims.Length == 2)
                    {
                        int numDetections = outputDims[0];
                        int colSize = outputDims[1];

                        for (int i = 0; i < numDetections; i++)
                        {
                            float cx = outputTensor[i, 0];
                            float cy = outputTensor[i, 1];
                            float w  = outputTensor[i, 2];
                            float h  = outputTensor[i, 3];
                            float objectness = colSize > 4 ? outputTensor[i, 4] : 1.0f;

                            if (objectness < confidenceThreshold) continue;

                            float bestConf = 0f;
                            int bestClass = -1;
                            int classOffset = colSize > 5 ? 5 : 4;

                            for (int c = 0; c < numClasses && (c + classOffset) < colSize; c++)
                            {
                                float conf = outputTensor[i, classOffset + c] * objectness;
                                if (conf > bestConf)
                                {
                                    bestConf = conf;
                                    bestClass = c;
                                }
                            }

                            if (bestConf >= confidenceThreshold && bestClass >= 0)
                            {
                                float x1 = (cx - w / 2f) * scaleX;
                                float y1 = (cy - h / 2f) * scaleY;
                                float bw = w * scaleX;
                                float bh = h * scaleY;

                                Rect box = new Rect(x1, y1, bw, bh);
                                string label = GetLabel(bestClass);

                                detections.Add(new DetectionResult(bestClass, label, bestConf, box));
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[ONNXEngine] Unexpected output shape: [{string.Join(", ", outputDims)}]");
                    }

                    Debug.Log($"[ONNXEngine] Raw detections: {detections.Count}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Inference error: {ex.Message}");
            }

            return detections;
        }
#endif

        private List<DetectionResult> ApplyNMS(List<DetectionResult> detections)
        {
            if (detections.Count == 0) return detections;

            detections.Sort((a, b) => b.confidence.CompareTo(a.confidence));

            List<DetectionResult> kept = new List<DetectionResult>();
            bool[] suppressed = new bool[detections.Count];

            for (int i = 0; i < detections.Count; i++)
            {
                if (suppressed[i]) continue;
                kept.Add(detections[i]);

                for (int j = i + 1; j < detections.Count; j++)
                {
                    if (suppressed[j]) continue;
                    if (detections[i].classId != detections[j].classId) continue;

                    float iou = ComputeIOU(detections[i].boundingBox, detections[j].boundingBox);
                    if (iou > nmsThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }

            return kept;
        }

        public static float ComputeIOU(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.xMin, b.xMin);
            float y1 = Mathf.Max(a.yMin, b.yMin);
            float x2 = Mathf.Min(a.xMax, b.xMax);
            float y2 = Mathf.Min(a.yMax, b.yMax);

            float intersectionArea = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
            float unionArea = a.width * a.height + b.width * b.height - intersectionArea;

            if (unionArea <= 0) return 0f;
            return intersectionArea / unionArea;
        }

        public string GetLabel(int classId)
        {
            if (labels != null && classId >= 0 && classId < labels.Length)
            {
                return labels[classId];
            }
            return $"class_{classId}";
        }

        private void OnDestroy()
        {
#if ONNX_RUNTIME
            if (session != null)
            {
                session.Dispose();
                session = null;
                Debug.Log("[ONNXEngine] Inference session disposed.");
            }
#endif
        }
    }
}
