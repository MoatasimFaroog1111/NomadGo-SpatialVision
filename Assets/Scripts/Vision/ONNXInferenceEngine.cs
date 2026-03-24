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
    /// AI Inference Engine v3 — Unity Sentis 1.0.0 + YOLOv8n
    ///
    /// Real AI: loads yolov8n.onnx from StreamingAssets, runs on GPU via Sentis.
    /// Detects 80 COCO classes (bottle, cup, food, grocery items, etc.)
    ///
    /// Falls back to Demo mode if model/package unavailable.
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

        private bool  isLoaded       = false;
        private bool  useDemoMode    = false;
        private float lastInferenceMs = 0f;

#if UNITY_SENTIS
        private Model   sentisModel;
        private IWorker sentisWorker;
        private bool    sentisReady = false;
#endif

        public bool  IsLoaded            => isLoaded;
        public float LastInferenceTimeMs => lastInferenceMs;

        // ── Public API ───────────────────────────────────────────────────────────

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
#if UNITY_SENTIS
            if (!useDemoMode && sentisReady && sentisWorker != null && frame != null)
            {
                try { return RunSentisInference(frame); }
                catch (Exception ex)
                {
                    Debug.LogError($"[ONNXEngine] Inference error: {ex.Message}");
                    return GenerateDemoDetections();
                }
            }
#endif
            return useDemoMode || isLoaded ? GenerateDemoDetections()
                                           : new List<DetectionResult>();
        }

        public string GetLabel(int classId)
        {
            if (labels != null && classId >= 0 && classId < labels.Length)
                return labels[classId];
            return $"class_{classId}";
        }

        // ── Labels ───────────────────────────────────────────────────────────────

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
                // Full COCO 80 classes
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

        // ── Model loading (async, Android-safe) ──────────────────────────────────

        private IEnumerator LoadModelAsync()
        {
#if UNITY_SENTIS
            string onnxPath = Path.Combine(Application.streamingAssetsPath, modelPath);
            Debug.Log($"[ONNXEngine] Loading: {onnxPath}");

            byte[] bytes = null;

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
            try
            {
                sentisModel = ModelLoader.Load(bytes);

                // Try GPU first, fall back to CPU
                try   { sentisWorker = WorkerFactory.CreateWorker(BackendType.GPUCompute, sentisModel); }
                catch { sentisWorker = WorkerFactory.CreateWorker(BackendType.CPU, sentisModel); }

                sentisReady = true;
                isLoaded    = true;
                Debug.Log($"[ONNXEngine] Real AI ready ({bytes.Length/1024/1024f:F1} MB). 80 COCO classes.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Sentis init failed: {ex.Message} → DEMO mode.");
                ActivateDemoMode();
            }
#else
            Debug.LogWarning("[ONNXEngine] UNITY_SENTIS not defined → DEMO mode.");
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

        // ── Sentis inference ─────────────────────────────────────────────────────

#if UNITY_SENTIS
        private List<DetectionResult> RunSentisInference(Texture2D frame)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. Preprocess → NCHW float32 [1, 3, 640, 640]
            float[] ncnhw = TextureToNCHW(frame);
            var inputTensor = new TensorFloat(new TensorShape(1, 3, inputHeight, inputWidth), ncnhw);

            // 2. Run inference
            sentisWorker.Execute(inputTensor);
            inputTensor.Dispose();

            // 3. Read output (YOLOv8 output name: "output0", shape: [1, 84, 8400])
            var rawOutput = sentisWorker.PeekOutput("output0") as TensorFloat;
            if (rawOutput == null)
            {
                Debug.LogError("[ONNXEngine] 'output0' missing from model outputs.");
                return GenerateDemoDetections();
            }

            rawOutput.MakeReadable();

            // Read data using index operator (works in all Sentis 1.x versions)
            int total = rawOutput.shape.length;
            float[] data = new float[total];
            for (int i = 0; i < total; i++) data[i] = rawOutput[i];

            sw.Stop();
            lastInferenceMs = (float)sw.Elapsed.TotalMilliseconds;

            var detections = ParseYOLOv8(data);
            return ApplyNMS(detections).Take(maxDetections).ToList();
        }

        private float[] TextureToNCHW(Texture2D src)
        {
            // Resize to 640×640
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
            Destroy(tex);

            int hw    = inputWidth * inputHeight;
            float[] d = new float[3 * hw];

            for (int i = 0; i < hw; i++)
            {
                // Flip Y: Unity Y=0 is bottom; NCHW Y=0 is top
                int row = inputHeight - 1 - (i / inputWidth);
                int col = i % inputWidth;
                int s   = row * inputWidth + col;
                d[0 * hw + i] = px[s].r / 255f;
                d[1 * hw + i] = px[s].g / 255f;
                d[2 * hw + i] = px[s].b / 255f;
            }
            return d;
        }
#endif

        // ── YOLOv8 output parser ─────────────────────────────────────────────────
        //   Shape: [1, 84, 8400]  features = 0-3 (cx,cy,w,h px/640) + 4-83 (class scores)

        private List<DetectionResult> ParseYOLOv8(float[] data)
        {
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

                string lbl = (labels != null && maxCls < labels.Length) ? labels[maxCls] : $"cls{maxCls}";
                results.Add(new DetectionResult(maxCls, lbl, maxConf,
                    new Rect(Mathf.Clamp01(cx - bw*.5f), Mathf.Clamp01(cy - bh*.5f),
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

        // ── Demo mode ────────────────────────────────────────────────────────────

        private static readonly Rect[] _anchors =
        {
            new Rect(0.10f,0.15f,0.22f,0.28f), new Rect(0.55f,0.15f,0.22f,0.28f),
            new Rect(0.10f,0.55f,0.22f,0.28f), new Rect(0.55f,0.55f,0.22f,0.28f),
            new Rect(0.33f,0.35f,0.20f,0.26f),
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
                Rect a  = _anchors[i]; float j = 0.008f;
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

        // ── Cleanup ──────────────────────────────────────────────────────────────

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
