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
        private string   labelsPath;          // path from config (Resources key)
        private int      inputWidth           = 640;
        private int      inputHeight          = 640;
        private float    confidenceThreshold  = 0.45f;
        private float    nmsThreshold         = 0.5f;
        private int      maxDetections        = 100;
        private string[] labels;

        // Cached-model overrides — set by AppManager after ModelDownloader finishes
        private string   overrideOnnxPath   = null; // full path to cached .onnx
        private string   overrideLabelsPath = null; // full path to cached labels.txt

        private bool  isLoaded        = false;
        private bool  useDemoMode     = false;
        private bool  isLoading       = false;
        private float lastInferenceMs = 0f;

#if UNITY_BARRACUDA
        private Model   barracudaModel;
        private IWorker barracudaWorker;
        private bool    barracudaReady = false;
#endif

        public bool  IsLoaded            => isLoaded;
        public bool  IsLoading           => isLoading;
        public bool  IsInDemoMode        => useDemoMode;
        public float LastInferenceTimeMs => lastInferenceMs;

        public void Initialize(AppShell.ModelConfig config)
        {
            modelPath           = config.path;
            labelsPath          = config.labels_path;
            inputWidth          = config.input_width;
            inputHeight         = config.input_height;
            confidenceThreshold = config.confidence_threshold;
            nmsThreshold        = config.nms_threshold;
            maxDetections       = config.max_detections;

            // Check if a ModelDownloader has already fetched a cached model
            var downloader = FindObjectOfType<ModelDownloader>();
            if (downloader != null && downloader.HasCachedModel)
            {
                overrideOnnxPath   = downloader.CachedModelPath;
                overrideLabelsPath = string.IsNullOrEmpty(downloader.CachedLabelsPath)
                                        ? null : downloader.CachedLabelsPath;
                Debug.Log($"[ONNXEngine] Using cached model: {overrideOnnxPath}");
            }

            LoadLabels(labelsPath);
            StartCoroutine(LoadModelAsync());
        }

        /// <summary>
        /// Hot-swaps the loaded model at runtime (e.g. after a background download).
        /// Disposes the old Barracuda worker, loads from the provided paths, and
        /// re-initialises inference. Falls back to demo mode on failure.
        /// </summary>
        public void ReloadModel(string onnxPath, string newLabelsPath)
        {
            if (isLoading)
            {
                Debug.LogWarning("[ONNXEngine] ReloadModel called while already loading — ignored.");
                return;
            }

            Debug.Log($"[ONNXEngine] ReloadModel → {onnxPath}");
            overrideOnnxPath   = onnxPath;
            overrideLabelsPath = newLabelsPath;

#if UNITY_BARRACUDA
            // Dispose existing worker so the new load starts clean
            barracudaWorker?.Dispose();
            barracudaWorker = null;
            barracudaModel  = null;
            barracudaReady  = false;
#endif
            isLoaded  = false;
            useDemoMode = false;

            if (!string.IsNullOrEmpty(newLabelsPath) && File.Exists(newLabelsPath))
                LoadLabelsFromFile(newLabelsPath);
            else
                LoadLabels(labelsPath);

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

        private void LoadLabels(string cfgLabelsPath)
        {
            // Priority 1: cached labels file on disk (set by ModelDownloader)
            if (!string.IsNullOrEmpty(overrideLabelsPath) && File.Exists(overrideLabelsPath))
            {
                LoadLabelsFromFile(overrideLabelsPath);
                return;
            }

            // Priority 2: Resources folder (bundled)
            string res = (cfgLabelsPath ?? "").Replace(".txt", "").Replace("Models/", "");
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
                Debug.LogWarning("[ONNXEngine] labels.txt not found — using built-in COCO 80.");
            }
        }

        /// <summary>Loads labels from an absolute file-system path (e.g. persistentDataPath cache).</summary>
        private void LoadLabelsFromFile(string filePath)
        {
            try
            {
                string text = File.ReadAllText(filePath);
                labels = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Debug.Log($"[ONNXEngine] {labels.Length} labels loaded from cached file: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ONNXEngine] Failed to read cached labels ({filePath}): {ex.Message}");
                // Fall through to built-in COCO labels
                labels = null;
                LoadLabels(labelsPath);
            }
        }

        private IEnumerator LoadModelAsync()
        {
            isLoading = true;
#if UNITY_BARRACUDA
            // Priority 1: cached model on disk (downloaded by ModelDownloader)
            bool usingCached = !string.IsNullOrEmpty(overrideOnnxPath) && File.Exists(overrideOnnxPath);
            string onnxPath  = usingCached
                ? overrideOnnxPath
                : Path.Combine(Application.streamingAssetsPath, modelPath);

            Debug.Log($"[ONNXEngine] Loading: {onnxPath}" + (usingCached ? " (cached)" : " (bundled)"));

            byte[] bytes = null;

            if (usingCached)
            {
                // Cached file lives in persistentDataPath — read directly
                try
                {
                    bytes = File.ReadAllBytes(onnxPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ONNXEngine] Failed to read cached model ({ex.Message}) → falling back to bundled.");
                    usingCached = false;
                    onnxPath    = Path.Combine(Application.streamingAssetsPath, modelPath);
                }
                yield return null;
            }

            // If not cached (or cached read failed), load from StreamingAssets
            if (!usingCached)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var req = UnityWebRequest.Get(onnxPath))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[ONNXEngine] Failed: {req.error} → DEMO mode.");
                        ActivateDemoMode(); yield break;
                    }
                    bytes = req.downloadHandler.data;
                }
#else
                if (!File.Exists(onnxPath))
                {
                    Debug.LogWarning($"[ONNXEngine] Not found: {onnxPath} → DEMO mode.");
                    ActivateDemoMode(); yield break;
                }
                bytes = File.ReadAllBytes(onnxPath);
                yield return null;
#endif
            }

            if (bytes == null)
            {
                Debug.LogWarning("[ONNXEngine] No model bytes available → DEMO mode.");
                ActivateDemoMode(); yield break;
            }

            try
            {
                barracudaModel  = ModelLoader.Load(bytes, verbose: false);

                barracudaWorker = WorkerFactory.CreateWorker(
                    WorkerFactory.Type.CSharpBurst, barracudaModel);

                barracudaReady = true;
                isLoaded       = true;
                isLoading      = false;
                Debug.Log($"[ONNXEngine] Barracuda model ready ({bytes.Length/1024/1024f:F1} MB). Real AI active.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Barracuda init failed: {ex.Message} → DEMO mode.");
                isLoading = false;
                ActivateDemoMode();
            }
#else
            Debug.LogWarning("[ONNXEngine] UNITY_BARRACUDA not defined → DEMO mode.");
            isLoading = false;
            ActivateDemoMode();
            yield return null;
#endif
        }

        private void ActivateDemoMode()
        {
            useDemoMode = true;
            isLoaded    = true;
            Debug.Log("[ONNXEngine] DEMO mode active.");
        }

#if UNITY_BARRACUDA
        private List<DetectionResult> RunBarracudaInference(Texture2D frame)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Preprocess → NHWC float32 [1, 640, 640, 3] (Barracuda input format)
            float[] nhwcData = TextureToNHWC(frame);
            var inputTensor = new Tensor(new TensorShape(1, inputHeight, inputWidth, 3), nhwcData);

            // Execute
            barracudaWorker.Execute(inputTensor);
            inputTensor.Dispose();

            // Get output — YOLOv8 name: "output0"
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
            DestroyImmediate(tex); // immediate: avoids deferred-GC buildup during inference

            int hw     = inputWidth * inputHeight;
            float[] d  = new float[hw * 3]; // NHWC: each pixel has 3 channels

            for (int i = 0; i < hw; i++)
            {
                // Flip Y: Unity Y=0 is bottom; NHWC Y=0 is top
                int row = inputHeight - 1 - (i / inputWidth);
                int col = i % inputWidth;
                int s   = row * inputWidth + col;
                // NHWC: [n, h, w, c] → flat [h*W*C + w*C + c]
                d[i * 3 + 0] = px[s].r / 255f;
                d[i * 3 + 1] = px[s].g / 255f;
                d[i * 3 + 2] = px[s].b / 255f;
            }
            return d;
        }

        private List<DetectionResult> ParseYOLOv8Barracuda(Tensor output)
        {
            // Shape: N=1, H=1, W=8400, C=84
            // Features: C 0-3 = cx,cy,w,h (in pixels for 640 input); C 4-83 = class scores
            const int numAnchors = 8400;
            int numClasses = labels != null ? Mathf.Min(labels.Length, 80) : 80;
            float sx = 1f / inputWidth;
            float sy = 1f / inputHeight;

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
                    new Rect(Mathf.Clamp01(cx - bw*.5f), Mathf.Clamp01(cy - bh*.5f),
                             Mathf.Clamp(bw, 0.01f, 1f), Mathf.Clamp(bh, 0.01f, 1f))));
            }
            return results;
        }
#endif

        public static float ComputeIOU(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.xMin, b.xMin);
            float y1 = Mathf.Max(a.yMin, b.yMin);
            float x2 = Mathf.Min(a.xMax, b.xMax);
            float y2 = Mathf.Min(a.yMax, b.yMax);
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
                    Rect a = dets[i].boundingBox, b = dets[j].boundingBox;
                    float x1 = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
                    float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
                    float inter = Mathf.Max(0, x2-x1) * Mathf.Max(0, y2-y1);
                    float uni   = a.width*a.height + b.width*b.height - inter;
                    if (uni > 0 && inter/uni > nmsThreshold) sup[j] = true;
                }
            }
            return kept;
        }

        private static readonly Rect[] _anchors =
        {
            new Rect(0.10f,0.15f,0.22f,0.28f), new Rect(0.55f,0.15f,0.22f,0.28f),
            new Rect(0.10f,0.55f,0.22f,0.28f), new Rect(0.55f,0.55f,0.22f,0.28f),
            new Rect(0.33f,0.35f,0.20f,0.26f),
        };
        // bottle=39, cup=41, bowl=45, apple=47, banana=46
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
                Rect a   = _anchors[i]; float j = 0.008f;
                int  cls = i < _demoClassIds.Length ? _demoClassIds[i] : 39;
                string lbl = (labels != null && cls < labels.Length) ? labels[cls] : "item";
                res.Add(new DetectionResult(cls, lbl, 0.78f + UnityEngine.Random.value * 0.18f,
                    new Rect(
                        Mathf.Clamp01(a.x + (UnityEngine.Random.value-.5f)*j),
                        Mathf.Clamp01(a.y + (UnityEngine.Random.value-.5f)*j),
                        Mathf.Clamp(a.width  + (UnityEngine.Random.value-.5f)*j, 0.05f, 0.45f),
                        Mathf.Clamp(a.height + (UnityEngine.Random.value-.5f)*j, 0.05f, 0.45f))));
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
