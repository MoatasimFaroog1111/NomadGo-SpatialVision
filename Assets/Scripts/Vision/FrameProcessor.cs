using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.Vision
{
    public class FrameProcessor : MonoBehaviour
    {
        private ONNXInferenceEngine inferenceEngine;
        private AppShell.CameraFix cameraFix;

        private bool isProcessing = false;
        private int frameSkip = 3;
        private int frameCounter = 0;
        private List<DetectionResult> latestDetections = new List<DetectionResult>();

        public bool IsProcessing => isProcessing;
        public List<DetectionResult> LatestDetections => latestDetections;
        public float LastInferenceTimeMs => inferenceEngine != null ? inferenceEngine.LastInferenceTimeMs : 0f;
        public int InputWidth { get; private set; } = 640;
        public int InputHeight { get; private set; } = 640;

        public delegate void DetectionsUpdatedHandler(List<DetectionResult> detections);
        public event DetectionsUpdatedHandler OnDetectionsUpdated;

        public void Initialize(AppShell.ModelConfig config)
        {
            if (inferenceEngine == null)
                inferenceEngine = gameObject.AddComponent<ONNXInferenceEngine>();

            inferenceEngine.Initialize(config);

            InputWidth = config.input_width;
            InputHeight = config.input_height;
            frameSkip = Mathf.Max(0, (int)(30f / 8f) - 1);
            Debug.Log($"[FrameProcessor] Initialized. FrameSkip={frameSkip}, Model={config.path}");
        }

        public void StartProcessing()
        {
            if (inferenceEngine == null)
            {
                Debug.LogError("[FrameProcessor] Inference engine not initialized. Call Initialize() first.");
                return;
            }

            if (!inferenceEngine.IsLoaded)
            {
                Debug.LogError("[FrameProcessor] Engine not ready. Cannot start.");
                return;
            }

            // Prefer injected reference; fall back to scene search only once
            if (cameraFix == null)
                cameraFix = FindObjectOfType<AppShell.CameraFix>();

            if (cameraFix == null)
            {
                Debug.LogError("[FrameProcessor] CameraFix not found. Add CameraFix to the camera GameObject.");
                return;
            }

            isProcessing = true;
            frameCounter = 0;
            Debug.Log("[FrameProcessor] Processing started.");
        }

        /// <summary>Optional: inject CameraFix directly to avoid scene search.</summary>
        public void InjectCameraFix(AppShell.CameraFix fix) => cameraFix = fix;

        public void StopProcessing()
        {
            isProcessing = false;
            latestDetections.Clear();
            OnDetectionsUpdated?.Invoke(latestDetections);
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

            var tex = ConvertWebCamToTexture(webCam);
            if (tex == null) return;

            ProcessFrame(tex);
            DestroyImmediate(tex); // immediate: avoids deferred-GC buildup at 30fps
        }

        private Texture2D ConvertWebCamToTexture(WebCamTexture webCam)
        {
            try
            {
                int targetW = InputWidth;
                int targetH = InputHeight;

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
        }

        private void OnDestroy()
        {
            StopProcessing();
        }
    }
}
