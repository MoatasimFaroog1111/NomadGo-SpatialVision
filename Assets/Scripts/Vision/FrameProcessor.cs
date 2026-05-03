using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.Vision
{
    public class FrameProcessor : MonoBehaviour
    {
        public static FrameProcessor Instance;

        public bool IsProcessing { get; private set; } = false;

        public List<DetectionResult> LatestDetections { get; private set; } = new List<DetectionResult>();

        private ONNXInferenceEngine engine;

        private void Awake()
        {
            Instance = this;

            engine = GetComponent<ONNXInferenceEngine>();

            if (engine == null)
            {
                engine = FindObjectOfType<ONNXInferenceEngine>();
            }

            if (engine == null)
            {
                Debug.LogError("[FrameProcessor] ONNXInferenceEngine not found.");
            }
            else
            {
                Debug.Log("[FrameProcessor] ONNXInferenceEngine found.");
            }
        }

        public void StartProcessing()
        {
            IsProcessing = true;
            Debug.Log("[FrameProcessor] Started.");
        }

        public void StopProcessing()
        {
            IsProcessing = false;
            LatestDetections.Clear();
            Debug.Log("[FrameProcessor] Stopped.");
        }

        private void Update()
        {
            if (!IsProcessing)
                return;

            if (engine == null)
            {
                engine = FindObjectOfType<ONNXInferenceEngine>();

                if (engine == null)
                    return;
            }

            Texture2D frame = CaptureFrame();

            if (frame == null)
                return;

            List<DetectionResult> detections = engine.Run(frame);

            if (detections != null)
            {
                LatestDetections = detections;
                Debug.Log("[FrameProcessor] Detections: " + LatestDetections.Count);
            }

            Destroy(frame);
        }

        private Texture2D CaptureFrame()
        {
            try
            {
                return ScreenCapture.CaptureScreenshotAsTexture();
            }
            catch
            {
                return null;
            }
        }
    }
}
