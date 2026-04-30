using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace NomadGo.Vision
{
    public class ONNXInferenceEngine : MonoBehaviour
    {
        private string   modelPath;
        private int      inputWidth           = 640;
        private int      inputHeight          = 640;
        private float    confidenceThreshold  = 0.20f;
        private float    nmsThreshold         = 0.5f;
        private int      maxDetections        = 100;
        private string[] labels;

        private bool  isLoaded        = false;
        private bool  useDemoMode     = false;
        private bool  isLoading       = false;
        private float lastInferenceMs = 0f;

        private const int   MAX_RETRIES         = 3;
        private const float RETRY_DELAY_SECONDS = 2f;
        private const int   REQUEST_TIMEOUT_SECONDS = 120;

        // ONNX Runtime
        private InferenceSession ortSession;
        private string ortInputName = "images";
        private string ortOutputName = "output0";
        private bool ortReady = false;

        public bool  IsLoaded            => isLoaded;
        public bool  IsLoading           => isLoading;
        public bool  IsInDemoMode        => useDemoMode;
        public float LastInferenceTimeMs => lastInferenceMs;

        private string overrideOnnxPath   = null;
        private string overrideLabelsPath = null;

        public void Initialize(AppShell.ModelConfig config)
        {
            modelPath           = config.path;
            inputWidth          = config.input_width;
            inputHeight         = config.input_height;
            confidenceThreshold = config.confidence_threshold;
            nmsThreshold        = config.nms_threshold;
            maxDetections       = config.max_detections;

            LoadLabels(config.labels_path);
            StartCoroutine(LoadModelAsync());
        }

        public void ReloadModel(string onnxPath, string newLabelsPath)
        {
            if (isLoading) return;

            Debug.Log($"[ONNXEngine] ReloadModel → {onnxPath}");
            overrideOnnxPath   = onnxPath;
            overrideLabelsPath = newLabelsPath;

            ortSession?.Dispose();
            ortSession = null;
            ortReady = false;

            isLoaded    = false;
            useDemoMode = false;

            if (!string.IsNullOrEmpty(newLabelsPath) && File.Exists(newLabelsPath))
            {
                try
                {
                    labels = File.ReadAllText(newLabelsPath)
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                }
                catch
                {
                    LoadLabels(modelPath);
                }
            }

            StartCoroutine(LoadModelAsync());
        }

        public List<DetectionResult> RunInference(Texture2D frame)
        {
            if (!useDemoMode && ortReady && ortSession != null && frame != null)
            {
                try
                {
                    return RunOnnxRuntimeInference(frame);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ONNXEngine] ONNX Runtime inference error: {ex.Message}");
                    Debug.LogError($"[ONNXEngine] StackTrace: {ex.StackTrace}");
                    return GenerateDemoDetections();
                }
            }

            return isLoaded ? GenerateDemoDetections() : new List<DetectionResult>();
        }

        public string GetLabel(int classId)
        {
            if (labels != null && classId >= 0 && classId < labels.Length)
                return labels[classId];

            return $"class_{classId}";
        }

        private void LoadLabels(string labelsPath)
        {
            string res = labelsPath.Replace(".txt", "").Replace("Models/", "");
            TextAsset asset = Resources.Load<TextAsset>(res)
                           ?? Resources.Load<TextAsset>("labels");

            if (asset != null)
            {
                labels = asset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Debug.Log($"[ONNXEngine] {labels.Length} labels loaded from Resources.");
            }
            else
            {
                labels = new[]
                {
                    "person","bicycle","car","motorcycle","airplane","bus","train","truck","boat",
                    "traffic light","fire hydrant","stop sign","parking meter","bench","bird","cat",
                    "dog","horse","sheep","cow","elephant","bear","zebra","giraffe","backpack",
                    "umbrella","handbag","tie","suitcase","frisbee","skis","snowboard","sports ball",
                    "kite","baseball bat","baseball glove","skateboard","surfboard","tennis racket",
                    "bottle","wine glass","cup","fork","knife","spoon","bowl","banana","apple",
                    "sandwich","orange","broccoli","carrot","hot dog","pizza","donut","cake",
                    "chair","couch","potted plant","bed","dining table","toilet","tv","laptop",
                    "mouse","remote","keyboard","cell phone","microwave","oven","toaster","sink",
                    "refrigerator","book","clock","vase","scissors","teddy bear","hair drier","toothbrush"
                };

                Debug.LogWarning("[ONNXEngine] labels.txt not found in Resources — using built-in COCO 80.");
            }
        }

        private IEnumerator LoadModelAsync()
        {
            isLoading = true;

            string effectivePath = !string.IsNullOrEmpty(overrideOnnxPath)
                ? overrideOnnxPath
                : Path.Combine(Application.streamingAssetsPath, modelPath);

            Debug.Log($"[ONNXEngine] Loading ONNX Runtime model from: {effectivePath}");
            Debug.Log($"[ONNXEngine] StreamingAssetsPath = {Application.streamingAssetsPath}");
            Debug.Log($"[ONNXEngine] Platform = {Application.platform}");

            byte[] bytes = null;

#if UNITY_ANDROID && !UNITY_EDITOR
            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                Debug.Log($"[ONNXEngine] Android load attempt {attempt}/{MAX_RETRIES}");

                using (var req = UnityWebRequest.Get(effectivePath))
                {
                    req.timeout = REQUEST_TIMEOUT_SECONDS;
                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        bytes = req.downloadHandler.data;
                        Debug.Log($"[ONNXEngine] Downloaded {bytes.Length / 1024 / 1024f:F1} MB on attempt {attempt}");
                        break;
                    }
                    else
                    {
                        Debug.LogWarning($"[ONNXEngine] Attempt {attempt} failed: {req.error} | HTTP: {req.responseCode} | URL: {effectivePath}");

                        if (attempt < MAX_RETRIES)
                        {
                            Debug.Log($"[ONNXEngine] Retrying in {RETRY_DELAY_SECONDS}s...");
                            yield return new WaitForSeconds(RETRY_DELAY_SECONDS);
                        }
                        else
                        {
                            Debug.LogError($"[ONNXEngine] All {MAX_RETRIES} attempts failed. Last error: {req.error}");
                            Debug.LogError($"[ONNXEngine] CRITICAL: ONNX model could not be loaded → falling back to DEMO mode.");
                            Debug.LogError($"[ONNXEngine] Verify the file exists at: Assets/StreamingAssets/Models/yolov8n.onnx");

                            isLoading = false;
                            ActivateDemoMode();
                            yield break;
                        }
                    }
                }
            }
#else
            if (!File.Exists(effectivePath))
            {
                Debug.LogError($"[ONNXEngine] File not found: {effectivePath} → DEMO mode.");
                Debug.LogError($"[ONNXEngine] Place yolov8n.onnx at: Assets/StreamingAssets/Models/yolov8n.onnx");

                isLoading = false;
                ActivateDemoMode();
                yield break;
            }

            bytes = File.ReadAllBytes(effectivePath);
            Debug.Log($"[ONNXEngine] Loaded {bytes.Length / 1024 / 1024f:F1} MB from disk.");
            yield return null;
#endif

            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogError("[ONNXEngine] Bytes array is null or empty after load → DEMO mode.");
                isLoading = false;
                ActivateDemoMode();
                yield break;
            }

            try
            {
                Debug.Log($"[ONNXEngine] Creating ONNX Runtime session ({bytes.Length / 1024 / 1024f:F1} MB)...");

                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                ortSession = new InferenceSession(bytes, sessionOptions);

                ortInputName = ortSession.InputMetadata.Keys.FirstOrDefault() ?? "images";
                ortOutputName = ortSession.OutputMetadata.Keys.FirstOrDefault() ?? "output0";

                Debug.Log($"[ONNXEngine] Input name: {ortInputName}");
                Debug.Log($"[ONNXEngine] Output name: {ortOutputName}");

                ortReady = true;
                isLoaded = true;
                isLoading = false;
                useDemoMode = false;

                Debug.Log("[ONNXEngine] ✅ ONNX Runtime model ready. Real AI inference ACTIVE.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] ONNX Runtime init failed: {ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"[ONNXEngine] StackTrace: {ex.StackTrace}");

                isLoading = false;
                ActivateDemoMode();
            }
        }

        private void ActivateDemoMode()
        {
            useDemoMode = true;
            isLoaded    = true;
            isLoading   = false;

            Debug.LogWarning("[ONNXEngine] ⚠️ DEMO mode active — using simulated detections. NOT production-ready.");
        }

        private List<DetectionResult> RunOnnxRuntimeInference(Texture2D frame)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            DenseTensor<float> inputTensor = TextureToNCHWTensor(frame);

            var input = NamedOnnxValue.CreateFromTensor(ortInputName, inputTensor);
            using (var results = ortSession.Run(new[] { input }))
            {
                sw.Stop();
                lastInferenceMs = (float)sw.Elapsed.TotalMilliseconds;

                var outputValue = results.FirstOrDefault(r => r.Name == ortOutputName) ?? results.First();
                Tensor<float> outputTensor = outputValue.AsTensor<float>();

                var detections = ParseYOLOv8OnnxRuntime(outputTensor);
                return ApplyNMS(detections).Take(maxDetections).ToList();
            }
        }

        private DenseTensor<float> TextureToNCHWTensor(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);

            Graphics.Blit(src, rt);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            var tex = new Texture2D(inputWidth, inputHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, inputWidth, inputHeight), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            Color32[] px = tex.GetPixels32();
            DestroyImmediate(tex);

            var tensor = new DenseTensor<float>(new[] { 1, 3, inputHeight, inputWidth });

            for (int y = 0; y < inputHeight; y++)
            {
                for (int x = 0; x < inputWidth; x++)
                {
                    int srcY = inputHeight - 1 - y;
                    int pixelIndex = srcY * inputWidth + x;
                    Color32 p = px[pixelIndex];

                    tensor[0, 0, y, x] = p.r / 255f;
                    tensor[0, 1, y, x] = p.g / 255f;
                    tensor[0, 2, y, x] = p.b / 255f;
                }
            }

            return tensor;
        }

        private List<DetectionResult> ParseYOLOv8OnnxRuntime(Tensor<float> output)
        {
            var dims = output.Dimensions.ToArray();

            int numClasses = labels != null ? Mathf.Min(labels.Length, 80) : 80;
            var results = new List<DetectionResult>();

            // YOLOv8 common output:
            // [1, 84, 8400] = batch, attributes, anchors
            // attributes = x, y, w, h + classes
            if (dims.Length == 3 && dims[1] >= 5)
            {
                int attributes = dims[1];
                int anchors = dims[2];
                int availableClasses = Mathf.Min(numClasses, attributes - 4);

                for (int a = 0; a < anchors; a++)
                {
                    float maxConf = 0f;
                    int maxCls = 0;

                    for (int c = 0; c < availableClasses; c++)
                    {
                        float score = output[0, 4 + c, a];

                        if (score > maxConf)
                        {
                            maxConf = score;
                            maxCls = c;
                        }
                    }

                    if (maxConf < confidenceThreshold)
                        continue;

                    float cx = output[0, 0, a] / inputWidth;
                    float cy = output[0, 1, a] / inputHeight;
                    float bw = output[0, 2, a] / inputWidth;
                    float bh = output[0, 3, a] / inputHeight;

                    string lbl = (labels != null && maxCls < labels.Length) ? labels[maxCls] : $"cls{maxCls}";

                    results.Add(new DetectionResult(
                        maxCls,
                        lbl,
                        maxConf,
                        new Rect(
                            Mathf.Clamp01(cx - bw * 0.5f),
                            Mathf.Clamp01(cy - bh * 0.5f),
                            Mathf.Clamp(bw, 0.01f, 1f),
                            Mathf.Clamp(bh, 0.01f, 1f)
                        )
                    ));
                }

                return results;
            }

            // Alternative YOLO export output:
            // [1, 8400, 84]
            if (dims.Length == 3 && dims[2] >= 5)
            {
                int anchors = dims[1];
                int attributes = dims[2];
                int availableClasses = Mathf.Min(numClasses, attributes - 4);

                for (int a = 0; a < anchors; a++)
                {
                    float maxConf = 0f;
                    int maxCls = 0;

                    for (int c = 0; c < availableClasses; c++)
                    {
                        float score = output[0, a, 4 + c];

                        if (score > maxConf)
                        {
                            maxConf = score;
                            maxCls = c;
                        }
                    }

                    if (maxConf < confidenceThreshold)
                        continue;

                    float cx = output[0, a, 0] / inputWidth;
                    float cy = output[0, a, 1] / inputHeight;
                    float bw = output[0, a, 2] / inputWidth;
                    float bh = output[0, a, 3] / inputHeight;

                    string lbl = (labels != null && maxCls < labels.Length) ? labels[maxCls] : $"cls{maxCls}";

                    results.Add(new DetectionResult(
                        maxCls,
                        lbl,
                        maxConf,
                        new Rect(
                            Mathf.Clamp01(cx - bw * 0.5f),
                            Mathf.Clamp01(cy - bh * 0.5f),
                            Mathf.Clamp(bw, 0.01f, 1f),
                            Mathf.Clamp(bh, 0.01f, 1f)
                        )
                    ));
                }

                return results;
            }

            Debug.LogError($"[ONNXEngine] Unsupported YOLO output shape: [{string.Join(",", dims)}]");
            return results;
        }

        public static float ComputeIOU(Rect a, Rect b)
        {
            float x1    = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
            float x2    = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
            float inter = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
            float uni   = a.width * a.height + b.width * b.height - inter;

            return uni > 0f ? inter / uni : 0f;
        }

        private List<DetectionResult> ApplyNMS(List<DetectionResult> dets)
        {
            dets.Sort((a, b) => b.confidence.CompareTo(a.confidence));

            var kept = new List<DetectionResult>();
            var sup  = new bool[dets.Count];

            for (int i = 0; i < dets.Count; i++)
            {
                if (sup[i]) continue;

                kept.Add(dets[i]);

                for (int j = i + 1; j < dets.Count; j++)
                {
                    if (sup[j] || dets[i].classId != dets[j].classId) continue;

                    Rect a = dets[i].boundingBox;
                    Rect b = dets[j].boundingBox;

                    float x1 = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
                    float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
                    float inter = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
                    float uni   = a.width * a.height + b.width * b.height - inter;

                    if (uni > 0 && inter / uni > nmsThreshold)
                        sup[j] = true;
                }
            }

            return kept;
        }

        // —— Demo mode fallback ——
        private static readonly Rect[] _anchors =
        {
            new Rect(0.10f, 0.15f, 0.22f, 0.28f), new Rect(0.55f, 0.15f, 0.22f, 0.28f),
            new Rect(0.10f, 0.55f, 0.22f, 0.28f), new Rect(0.55f, 0.55f, 0.22f, 0.28f),
            new Rect(0.33f, 0.35f, 0.20f, 0.26f),
        };

        private static readonly int[] _demoClassIds = { 39, 41, 45, 47, 46 };

        private List<DetectionResult> GenerateDemoDetections()
        {
            lastInferenceMs = 2.5f;

            var res = new List<DetectionResult>();
            int hide = UnityEngine.Random.Range(0, 3);
            var hideSet = new HashSet<int>();

            while (hideSet.Count < hide)
                hideSet.Add(UnityEngine.Random.Range(0, _anchors.Length));

            for (int i = 0; i < _anchors.Length; i++)
            {
                if (hideSet.Contains(i)) continue;

                Rect a = _anchors[i];
                float j = 0.008f;
                int cls = i < _demoClassIds.Length ? _demoClassIds[i] : 39;
                string lbl = (labels != null && cls < labels.Length) ? labels[cls] : "item";

                res.Add(new DetectionResult(
                    cls,
                    lbl,
                    0.78f + UnityEngine.Random.value * 0.18f,
                    new Rect(
                        Mathf.Clamp01(a.x + (UnityEngine.Random.value - .5f) * j),
                        Mathf.Clamp01(a.y + (UnityEngine.Random.value - .5f) * j),
                        Mathf.Clamp(a.width  + (UnityEngine.Random.value - .5f) * j, 0.05f, 0.45f),
                        Mathf.Clamp(a.height + (UnityEngine.Random.value - .5f) * j, 0.05f, 0.45f)
                    )
                ));
            }

            return res;
        }

        private void OnDestroy()
        {
            ortSession?.Dispose();
            ortSession = null;
            ortReady = false;
        }
    }
}
