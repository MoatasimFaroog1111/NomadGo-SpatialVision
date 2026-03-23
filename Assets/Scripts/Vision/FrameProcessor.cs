using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.Vision
{
    /// <summary>
    /// Processes camera frames using WebCamTexture (via CameraFix) and runs YOLO inference.
    /// Does NOT use ARFoundation/ARCameraManager — uses WebCamTexture directly for compatibility.
    /// </summary>
    public class FrameProcessor : MonoBehaviour
    {
        private ONNXInferenceEngine inferenceEngine;
        private AppShell.CameraFix cameraFix;

        private bool isProcessing = false;
        private int frameSkip = 3; // Process every 4th frame (~7.5fps inference at 30fps)
        private int frameCounter = 0;
        private List<DetectionResult> latestDetections = new List<DetectionResult>();

        public bool IsProcessing => isProcessing;
        public List<DetectionResult> LatestDetections => latestDetections;
        public float LastInferenceTimeMs => inferenceEngine != null ? inferenceEngine.LastInferenceTimeMs : 0f;

        public delegate void DetectionsUpdatedHandler(List<DetectionResult> detections);
        public event DetectionsUpdatedHandler OnDetectionsUpdated;

        public void Initialize(AppShell.ModelConfig config)
        {
            if (inferenceEngine == null)
            {
                inferenceEngine = gameObject.AddComponent<ONNXInferenceEngine>();
            }
            inferenceEngine.Initialize(config);

            frameSkip = Mathf.Max(0, (int)(30f / 8f) - 1); // ~8fps inference
            Debug.Log($"[FrameProcessor] Initialized. FrameSkip={frameSkip}, Model={config.modelPath}");
        }

        public void StartProcessing()
        {
            if (inferenceEngine == null || !inferenceEngine.IsLoaded)
            {
                Debug.LogError("[FrameProcessor] Cannot start — inference engine not loaded.");
                return;
            }

            // Find CameraFix
            cameraFix = FindObjectOfType<AppShell.CameraFix>();
            if (cameraFix == null)
            {
                Debug.LogError("[FrameProcessor] CameraFix not found!");
                return;
            }

            isProcessing = true;
            frameCounter = 0;
            Debug.Log("[FrameProcessor] Processing started (WebCamTexture mode).");
        }

        public void StopProcessing()
        {
            isProcessing = false;
            Debug.Log("[FrameProcessor] Processing stopped.");
        }

        private void Update()
        {
            if (!isProcessing) return;
            if (cameraFix == null || !cameraFix.IsReady) return;

            var webCam = cameraFix.CameraTexture;
            if (webCam == null || !webCam.isPlaying || !webCam.didUpdateThisFrame) return;

            frameCounter++;
            if (frameCounter % (frameSkip + 1) != 0) return;

            // Convert WebCamTexture to Texture2D for YOLO
            var tex = ConvertWebCamToTexture(webCam);
            if (tex == null) return;

            ProcessFrame(tex);
            Destroy(tex);
        }

        private Texture2D ConvertWebCamToTexture(WebCamTexture webCam)
        {
            try
            {
                // Resize to model input size (640x640 for YOLOv8)
                int targetW = 640;
                int targetH = 640;

                // Create RenderTexture for resize
                var rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(webCam, rt);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;

                var tex = new Texture2D(targetW, targetH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
                tex.Apply();

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FrameProcessor] Frame conversion error: {e.Message}");
                return null;
            }
        }

        private void ProcessFrame(Texture2D frame)
        {
            var detections = inferenceEngine.RunInference(frame);
            latestDetections = detections;
            OnDetectionsUpdated?.Invoke(detections);

            if (detections.Count > 0)
            {
                Debug.Log($"[FrameProcessor] {detections.Count} detections, {inferenceEngine.LastInferenceTimeMs:F1}ms");
            }
        }

        private void OnDestroy()
        {
            StopProcessing();
        }
    }
}
