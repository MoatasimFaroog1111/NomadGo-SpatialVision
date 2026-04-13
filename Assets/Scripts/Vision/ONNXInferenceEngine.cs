using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_BARRACUDA
using Unity.Barracuda;
#endif

namespace NomadGo.Vision
{
    public class ONNXInferenceEngine : MonoBehaviour
    {
        private string   modelPath;
        private int      inputWidth           = 640;
        private int      inputHeight          = 640;
        private float    confidenceThreshold  = 0.45f;
        private float    nmsThreshold         = 0.5f;
        private int      maxDetections        = 100;
        private string[] labels;

        private bool  isLoaded        = false;
        private bool  useDemoMode     = false;
        private bool  isLoading       = false;
        private float lastInferenceMs = 0f;

        // FIX: retry constants — Android OBB extraction can be slow on first launch
        private const int   MAX_RETRIES         = 3;
        private const float RETRY_DELAY_SECONDS = 2f;
        // FIX: increased timeout — yolov8n.onnx is ~12 MB, needs more than default 30s on slow devices
        private const int   REQUEST_TIMEOUT_SECONDS = 120;

#if UNITY_BARRACUDA
        private Model   barracudaModel;
        private IWorker barracudaWorker;
        private bool    barracudaReady = false;
#endif

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
#if UNITY_BARRACUDA
            barracudaWorker?.Dispose();
            barracudaWorker = null;
            barracudaModel  = null;
            barracudaReady  = false;
#endif
            isLoaded    = false;
            useDemoMode = false;
            if (!string.IsNullOrEmpty(newLabelsPath) && File.Exists(newLabelsPath))
            {
                try { labels = File.ReadAllText(newLabelsPath).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries); }
                catch { LoadLabels(modelPath); }
            }
            StartCoroutine(LoadModelAsync());
        }

        public List<DetectionResult> RunInference(Texture2D frame)
        {
#if UNITY_BARRACUDA
            if (!useDemoMode && barracudaReady && barracudaWorker != null && frame != null)
            {
                try { return RunBarracudaInference(frame); }
                catch (Exception ex)
                {
                    Debug.LogError($"[ONNXEngine] Inference error: {ex.Message}");
                    return GenerateDemoDetections();
                }
            }
#endif
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

#if UNITY_BARRACUDA
            // FIX: use overrideOnnxPath if set by ReloadModel, else build from config path
            string effectivePath = !string.IsNullOrEmpty(overrideOnnxPath)
                ? overrideOnnxPath
                : Path.Combine(Application.streamingAssetsPath, modelPath);

            Debug.Log($"[ONNXEngine] Loading model from: {effectivePath}");
            Debug.Log($"[ONNXEngine] StreamingAssetsPath = {Application.streamingAssetsPath}");
            Debug.Log($"[ONNXEngine] Platform = {Application.platform}");

            byte[] bytes = null;

#if UNITY_ANDROID && !UNITY_EDITOR
            // On Android, StreamingAssets lives inside the APK (JAR/ZIP).
            // MUST use UnityWebRequest — File.Exists / File.ReadAllBytes do NOT work.
            // FIX: added retry loop because on first cold launch Android needs time
            // to extract the APK's StreamingAssets before they’re readable.
            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                Debug.Log($"[ONNXEngine] Android load attempt {attempt}/{MAX_RETRIES}");

                using (var req = UnityWebRequest.Get(effectivePath))
                {
                    // FIX: increased timeout from default (no timeout set = 30s) to 120s
                    // yolov8n.onnx is ~12 MB — slow devices need more time
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
                            Debug.LogError($"[ONNXEngine] CRITICAL: yolov8n.onnx could not be loaded → falling back to DEMO mode.");
                            Debug.LogError($"[ONNXEngine] Verify the file exists at: Assets/StreamingAssets/Models/yolov8n.onnx");
                            isLoading = false;
                            ActivateDemoMode();
                            yield break;
                        }
                    }
                }
            }
#else
            // Editor / Desktop: direct File I/O is fine
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

            // bytes loaded successfully — now initialize Barracuda
            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogError("[ONNXEngine] Bytes array is null or empty after load → DEMO mode.");
                isLoading = false;
                ActivateDemoMode();
                yield break;
            }

            // FIX: ModelLoader.Load is synchronous and blocks main thread.
            // For ~12MB it takes ~1-3s on mobile — acceptable since it’s a one-time startup cost.
            // If you need async loading, use a background thread + UnityMainThreadDispatcher.
            try
            {
                Debug.Log($"[ONNXEngine] Parsing model ({bytes.Length / 1024 / 1024f:F1} MB)...");
                barracudaModel = ModelLoader.Load(bytes, verbose: false);

                // FIX: WorkerFactory.Type.CSharpBurst requires Burst package.
                // On Android IL2CPP, use ComputePrecompiled for GPU or CSharpBurst for CPU.
                // FIX: fall back gracefully if GPU worker fails.
                try
                {
                    barracudaWorker = WorkerFactory.CreateWorker(
                        WorkerFactory.Type.ComputePrecompiled, barracudaModel);
                    Debug.Log("[ONNXEngine] Worker created: ComputePrecompiled (GPU).");
                }
                catch (Exception gpuEx)
                {
                    Debug.LogWarning($"[ONNXEngine] GPU worker failed ({gpuEx.Message}), falling back to CSharpBurst (CPU).");
                    barracudaWorker = WorkerFactory.CreateWorker(
                        WorkerFactory.Type.CSharpBurst, barracudaModel);
                    Debug.Log("[ONNXEngine] Worker created: CSharpBurst (CPU fallback).");
                }

                barracudaReady = true;
                isLoaded       = true;
                isLoading      = false;
                useDemoMode    = false; // FIX: explicitly clear demo mode flag on success
                Debug.Log($"[ONNXEngine] ✅ Barracuda model ready. Real AI inference ACTIVE.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Barracuda model init failed: {ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"[ONNXEngine] StackTrace: {ex.StackTrace}");
                isLoading = false;
                ActivateDemoMode();
            }
#else
            // UNITY_BARRACUDA not defined — this means the Barracuda package is not installed
            // or UNITY_BARRACUDA is not in Scripting Define Symbols.
            Debug.LogError("[ONNXEngine] UNITY_BARRACUDA symbol is NOT defined!");
            Debug.LogError("[ONNXEngine] Fix: Add 'UNITY_BARRACUDA' to ProjectSettings → Player → Scripting Define Symbols");
            Debug.LogError("[ONNXEngine] AND ensure 'com.unity.barracuda' is in Packages/manifest.json");
            isLoading = false;
            ActivateDemoMode();
            yield return null;
#endif
        }

        private void ActivateDemoMode()
        {
            useDemoMode = true;
            isLoaded    = true;
            isLoading   = false;
            Debug.LogWarning("[ONNXEngine] ⚠️ DEMO mode active — using simulated detections. NOT production-ready.");
        }

#if UNITY_BARRACUDA
        private List<DetectionResult> RunBarracudaInference(Texture2D frame)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            float[] nhwcData    = TextureToNHWC(frame);
            var     inputTensor = new Tensor(new TensorShape(1, inputHeight, inputWidth, 3), nhwcData);

            barracudaWorker.Execute(inputTensor);
            inputTensor.Dispose();

            Tensor output = barracudaWorker.PeekOutput("output0");
            sw.Stop();
            lastInferenceMs = (float)sw.Elapsed.TotalMilliseconds;

            var detections = ParseYOLOv8Barracuda(output);
            output.Dispose();

            return ApplyNMS(detections).Take(maxDetections).ToList();
        }

        private float[] TextureToNHWC(Texture2D src)
        {
            var rt   = RenderTexture.GetTemporary(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
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

            int     hw = inputWidth * inputHeight;
            float[] d  = new float[hw * 3];

            for (int i = 0; i < hw; i++)
            {
                int row = inputHeight - 1 - (i / inputWidth);
                int col = i % inputWidth;
                int s   = row * inputWidth + col;
                d[i * 3 + 0] = px[s].r / 255f;
                d[i * 3 + 1] = px[s].g / 255f;
                d[i * 3 + 2] = px[s].b / 255f;
            }
            return d;
        }

        private List<DetectionResult> ParseYOLOv8Barracuda(Tensor output)
        {
            const int numAnchors = 8400;
            int       numClasses = labels != null ? Mathf.Min(labels.Length, 80) : 80;
            float     sx = 1f / inputWidth;
            float     sy = 1f / inputHeight;

            var results = new List<DetectionResult>();

            for (int a = 0; a < numAnchors; a++)
            {
                float maxConf = 0f; int maxCls = 0;
                for (int c = 0; c < numClasses; c++)
                {
                    float s = output[0, 0, a, 4 + c];
                    if (s > maxConf) { maxConf = s; maxCls = c; }
                }
                if (maxConf < confidenceThreshold) continue;

                float cx = output[0, 0, a, 0] * sx;
                float cy = output[0, 0, a, 1] * sy;
                float bw = output[0, 0, a, 2] * sx;
                float bh = output[0, 0, a, 3] * sy;

                string lbl = (labels != null && maxCls < labels.Length) ? labels[maxCls] : $"cls{maxCls}";
                results.Add(new DetectionResult(maxCls, lbl, maxConf,
                    new Rect(Mathf.Clamp01(cx - bw * .5f), Mathf.Clamp01(cy - bh * .5f),
                             Mathf.Clamp(bw, 0.01f, 1f),   Mathf.Clamp(bh, 0.01f, 1f))));
            }
            return results;
        }
#endif

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
            var  kept = new List<DetectionResult>();
            var  sup  = new bool[dets.Count];
            for (int i = 0; i < dets.Count; i++)
            {
                if (sup[i]) continue;
                kept.Add(dets[i]);
                for (int j = i + 1; j < dets.Count; j++)
                {
                    if (sup[j] || dets[i].classId != dets[j].classId) continue;
                    Rect  a = dets[i].boundingBox, b = dets[j].boundingBox;
                    float x1 = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
                    float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
                    float inter = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
                    float uni   = a.width * a.height + b.width * b.height - inter;
                    if (uni > 0 && inter / uni > nmsThreshold) sup[j] = true;
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
            var res  = new List<DetectionResult>();
            int hide = UnityEngine.Random.Range(0, 3);
            var hideSet = new HashSet<int>();
            while (hideSet.Count < hide)
                hideSet.Add(UnityEngine.Random.Range(0, _anchors.Length));

            for (int i = 0; i < _anchors.Length; i++)
            {
                if (hideSet.Contains(i)) continue;
                Rect   a   = _anchors[i]; float j = 0.008f;
                int    cls = i < _demoClassIds.Length ? _demoClassIds[i] : 39;
                string lbl = (labels != null && cls < labels.Length) ? labels[cls] : "item";
                res.Add(new DetectionResult(cls, lbl, 0.78f + UnityEngine.Random.value * 0.18f,
                    new Rect(
                        Mathf.Clamp01(a.x + (UnityEngine.Random.value - .5f) * j),
                        Mathf.Clamp01(a.y + (UnityEngine.Random.value - .5f) * j),
                        Mathf.Clamp(a.width  + (UnityEngine.Random.value - .5f) * j, 0.05f, 0.45f),
                        Mathf.Clamp(a.height + (UnityEngine.Random.value - .5f) * j, 0.05f, 0.45f))));
            }
            return res;
        }

        private void OnDestroy()
        {
#if UNITY_BARRACUDA
            barracudaWorker?.Dispose();
            barracudaModel = null;
            barracudaReady = false;
#endif
        }
    }
}
