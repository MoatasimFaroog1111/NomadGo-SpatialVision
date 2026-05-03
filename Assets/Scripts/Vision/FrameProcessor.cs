using System.Collections.Generic;
using UnityEngine;

namespace Vision
{
    public class FrameProcessor : MonoBehaviour
    {
        public static FrameProcessor Instance;

        public bool IsProcessing { get; private set; } = false;
        public List<DetectionResult> LatestDetections = new List<DetectionResult>();

        private ONNXInferenceEngine engine;

        void Awake()
        {
            Instance = this;
            engine = GetComponent<ONNXInferenceEngine>();

            if (engine == null)
            {
                Debug.LogError("❌ ONNXInferenceEngine NOT FOUND");
            }
        }

        public void StartProcessing()
        {
            IsProcessing = true;
            Debug.Log("🔥 FrameProcessor STARTED");
        }

        public void StopProcessing()
        {
            IsProcessing = false;
            LatestDetections.Clear();
            Debug.Log("🛑 FrameProcessor STOPPED");
        }

        void Update()
        {
            if (!IsProcessing || engine == null)
                return;

            Texture2D tex = GetCameraFrame();

            if (tex == null)
                return;

            var detections = engine.Run(tex);

            if (detections != null)
            {
                LatestDetections = detections;

                Debug.Log("✅ DETECTIONS: " + detections.Count);
            }
        }

        private Texture2D GetCameraFrame()
        {
            // مؤقت: نأخذ الشاشة كصورة (حل سريع)
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            return tex;
        }
    }
}
