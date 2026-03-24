using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_SENTIS
using Unity.Sentis;
#endif

namespace NomadGo.Vision
{
    /// <summary>
    /// AI Inference Engine v2 — Unity Sentis + YOLOv8n
    ///
    /// Real AI mode (requires com.unity.sentis in manifest + yolov8n.onnx in StreamingAssets/Models/):
    ///   - Loads model at startup via async coroutine
    ///   - GPU-accelerated inference (Sentis GPUCompute backend)
    ///   - Parses YOLOv8 output format (1, 84, 8400): 80 COCO classes
    ///
    /// Demo mode (auto-fallback when model/package not available):
    ///   - 5 stable simulated detections at fixed positions
    ///   - Lets the full UI/count/report pipeline be tested
    /// </summary>
    public class ONNXInferenceEngine : MonoBehaviour
    {
        private string   modelPath;
        private int      inputWidth           = 640;
        private int      inputHeight          = 640;
        private float    confidenceThreshold  = 0.45f;
        private float    nmsThreshold         = 0.5f;
        private int      maxDetections        = 100;
        private string[] labels;

        private bool  isLoaded    = false;
        private bool  useDemoMode = false;
        private float lastInferenceMs = 0f;

#if UNITY_SENTIS
        private Model   sentisModel;
        private IWorker sentisWorker;
        private bool    sentisReady = false;
#endif

        public bool  IsLoaded            => isLoaded;
        public float LastInferenceTimeMs => lastInferenceMs;

        // ── Public API ────────────────────────────────────────────────────────────

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

        public List<DetectionResult> RunInference(Texture2D frame)
        {
            if (useDemoMode || !isLoaded) return GenerateDemoDetections();

#if UNITY_SENTIS
            if (!sentisReady || sentisWorker == null || frame == null)
                return GenerateDemoDetections();

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var inputTensor = PreprocessTexture(frame);
                if (inputTensor == null) return GenerateDemoDetections();

                sentisWorker.Execute(inputTensor);
                inputTensor.Dispose();

                // YOLOv8 output layer name is "output0"
                TensorFloat output = sentisWorker.PeekOutput("output0") as TensorFloat;
                if (output == null)
                {
                    Debug.LogError("[ONNXEngine] 'output0' not found in model outputs.");
                    return GenerateDemoDetections();
                }

                output.MakeReadable();
                float[] data = output.ToReadOnlyArray();

                sw.Stop();
                lastInferenceMs = (float)sw.Elapsed.TotalMilliseconds;

                var raw     = ParseYOLOv8Output(data);
                var final   = ApplyNMS(raw);
                return final.Take(maxDetections).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Inference error: {ex.Message}");
                return GenerateDemoDetections();
            }
#else
            return GenerateDemoDetections();
#endif
        }

        public string GetLabel(int classId)
        {
            if (labels != null && classId >= 0 && classId < labels.Length)
                return labels[classId];
            return $"class_{classId}";
        }

        // ── Labels ────────────────────────────────────────────────────────────────

        private void LoadLabels(string labelsPath)
        {
            string res = labelsPath.Replace(".txt", "").Replace("Models/", "");
            TextAsset asset = Resources.Load<TextAsset>(res)
                           ?? Resources.Load<TextAsset>("labels");

            if (asset != null)
            {
                labels = asset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Debug.Log($"[ONNXEngine] {labels.Length} labels loaded.");
            }
            else
            {
                // Full COCO 80 classes built-in
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
                Debug.LogWarning("[ONNXEngine] labels.txt not found — using built-in COCO 80 classes.");
            }
        }

        // ── Async model loading ───────────────────────────────────────────────────

        private IEnumerator LoadModelAsync()
        {
#if UNITY_SENTIS
            string onnxPath = Path.Combine(Application.streamingAssetsPath, modelPath);
            Debug.Log($"[ONNXEngine] Loading model: {onnxPath}");

            byte[] modelBytes = null;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Android: StreamingAssets are compressed inside APK — must use UnityWebRequest
            using (var req = UnityWebRequest.Get(onnxPath))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[ONNXEngine] Model load failed: {req.error} → DEMO mode.");
                    ActivateDemoMode(); yield break;
                }
                modelBytes = req.downloadHandler.data;
            }
#else
            if (!File.Exists(onnxPath))
            {
                Debug.LogWarning($"[ONNXEngine] Model not found at {onnxPath} → DEMO mode.");
                ActivateDemoMode(); yield break;
            }
            modelBytes = File.ReadAllBytes(onnxPath);
            yield return null;
#endif
            try
            {
                sentisModel  = ModelLoader.Load(modelBytes);
                sentisWorker = WorkerFactory.CreateWorker(BackendType.GPUCompute, sentisModel);
                sentisReady  = true;
                isLoaded     = true;
                Debug.Log($"[ONNXEngine] Model ready ({modelBytes.Length / 1024 / 1024f:F1} MB). Real AI active.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Sentis init error: {ex.Message} → DEMO mode.");
                ActivateDemoMode();
            }
#else
            // Sentis package not installed
            Debug.LogWarning("[ONNXEngine] Unity Sentis not installed → DEMO mode.");
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

        // ── Preprocessing ─────────────────────────────────────────────────────────

#if UNITY_SENTIS
        private TensorFloat PreprocessTexture(Texture2D src)
        {
            try
            {
                // Resize to 640×640
                var rt   = RenderTexture.GetTemporary(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var resized = new Texture2D(inputWidth, inputHeight, TextureFormat.RGB24, false);
                resized.ReadPixels(new Rect(0, 0, inputWidth, inputHeight), 0, 0);
                resized.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                // Build NCHW float tensor [1, 3, H, W], normalized 0–1
                Color32[] px = resized.GetPixels32();
                Destroy(resized);
                int hw = inputWidth * inputHeight;
                float[] nchw = new float[3 * hw];

                for (int i = 0; i < hw; i++)
                {
                    // Flip Y: Unity Y=0 is bottom; NCHW Y=0 is top
                    int row = inputHeight - 1 - (i / inputWidth);
                    int col = i % inputWidth;
                    int s   = row * inputWidth + col;
                    nchw[0 * hw + i] = px[s].r / 255f;
                    nchw[1 * hw + i] = px[s].g / 255f;
                    nchw[2 * hw + i] = px[s].b / 255f;
                }

                return new TensorFloat(new TensorShape(1, 3, inputHeight, inputWidth), nchw);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Preprocess error: {ex.Message}");
                return null;
            }
        }
#endif

        // ── YOLOv8 output parsing ─────────────────────────────────────────────────

        private List<DetectionResult> ParseYOLOv8Output(float[] data)
        {
            // Shape: (1, 84, 8400)  [batch, features, anchors]
            // Features 0-3: cx,cy,w,h in pixels (scale 0-640)
            // Features 4-83: class confidence scores
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
                    float s = data[(4 + c) * numAnchors + a];
                    if (s > maxConf) { maxConf = s; maxCls = c; }
                }
                if (maxConf < confidenceThreshold) continue;

                float cx = data[0 * numAnchors + a] * sx;
                float cy = data[1 * numAnchors + a] * sy;
                float bw = data[2 * numAnchors + a] * sx;
                float bh = data[3 * numAnchors + a] * sy;
                float bx = cx - bw * 0.5f;
                float by = cy - bh * 0.5f;

                string lbl = (labels != null && maxCls < labels.Length) ? labels[maxCls] : $"cls{maxCls}";
                results.Add(new DetectionResult(maxCls, lbl, maxConf,
                    new Rect(Mathf.Clamp01(bx), Mathf.Clamp01(by),
                             Mathf.Clamp(bw, 0.01f, 1f), Mathf.Clamp(bh, 0.01f, 1f))));
            }
            return results;
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
                    float x1 = Mathf.Max(dets[i].boundingBox.xMin, dets[j].boundingBox.xMin);
                    float y1 = Mathf.Max(dets[i].boundingBox.yMin, dets[j].boundingBox.yMin);
                    float x2 = Mathf.Min(dets[i].boundingBox.xMax, dets[j].boundingBox.xMax);
                    float y2 = Mathf.Min(dets[i].boundingBox.yMax, dets[j].boundingBox.yMax);
                    float inter = Mathf.Max(0, x2-x1) * Mathf.Max(0, y2-y1);
                    float a = dets[i].boundingBox.width * dets[i].boundingBox.height;
                    float b = dets[j].boundingBox.width * dets[j].boundingBox.height;
                    if (inter / (a + b - inter) > nmsThreshold) sup[j] = true;
                }
            }
            return kept;
        }

        // ── Demo mode ─────────────────────────────────────────────────────────────

        private static readonly Rect[] _anchors =
        {
            new Rect(0.10f,0.15f,0.22f,0.28f), new Rect(0.55f,0.15f,0.22f,0.28f),
            new Rect(0.10f,0.55f,0.22f,0.28f), new Rect(0.55f,0.55f,0.22f,0.28f),
            new Rect(0.33f,0.35f,0.20f,0.26f),
        };
        // COCO indices: bottle=39, cup=41, bowl=45, apple=47, banana=46
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
                Rect a = _anchors[i]; float j = 0.008f;
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

        // ── Cleanup ───────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
#if UNITY_SENTIS
            sentisWorker?.Dispose();
            sentisModel = null;
            sentisReady = false;
#endif
        }
    }
}
