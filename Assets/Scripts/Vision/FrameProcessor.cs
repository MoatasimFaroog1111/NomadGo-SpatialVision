using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace NomadGo.Vision
{
    public class FrameProcessor : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private ONNXInferenceEngine inferenceEngine;

        private bool isProcessing = false;
        private int frameSkip = 0;
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

            frameSkip = Mathf.Max(0, (int)(60f / 15f) - 1);
            Debug.Log($"[FrameProcessor] Initialized. Frame skip: {frameSkip}");
        }

        public void StartProcessing()
        {
            if (inferenceEngine == null || !inferenceEngine.IsLoaded)
            {
                Debug.LogError("[FrameProcessor] Cannot start — inference engine not ready.");
                return;
            }

            isProcessing = true;
            frameCounter = 0;

            if (cameraManager != null)
            {
                cameraManager.frameReceived += OnCameraFrameReceived;
            }

            Debug.Log("[FrameProcessor] Processing started.");
        }

        public void StopProcessing()
        {
            isProcessing = false;

            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }

            Debug.Log("[FrameProcessor] Processing stopped.");
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            if (!isProcessing) return;

            frameCounter++;
            if (frameCounter % (frameSkip + 1) != 0) return;

            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
            {
                return;
            }

            Texture2D texture = ConvertCpuImageToTexture(cpuImage);
            cpuImage.Dispose();

            if (texture == null) return;

            ProcessFrame(texture);
            Destroy(texture);
        }

        private Texture2D ConvertCpuImageToTexture(XRCpuImage cpuImage)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            int size = cpuImage.GetConvertedDataSize(conversionParams);
            var buffer = new Unity.Collections.NativeArray<byte>(size, Unity.Collections.Allocator.Temp);

            cpuImage.Convert(conversionParams, buffer);

            Texture2D texture = new Texture2D(
                conversionParams.outputDimensions.x,
                conversionParams.outputDimensions.y,
                conversionParams.outputFormat,
                false
            );
            texture.LoadRawTextureData(buffer);
            texture.Apply();

            buffer.Dispose();
            return texture;
        }

        private void ProcessFrame(Texture2D frame)
        {
            List<DetectionResult> detections = inferenceEngine.RunInference(frame);

            latestDetections = detections;
            OnDetectionsUpdated?.Invoke(detections);

            if (detections.Count > 0)
            {
                Debug.Log($"[FrameProcessor] Frame processed: {detections.Count} detections, {inferenceEngine.LastInferenceTimeMs:F1}ms");
            }
        }

        private void OnDestroy()
        {
            StopProcessing();
        }
    }
}
