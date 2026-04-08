using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace NomadGo.AppShell
{
    public class CameraController : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private RawImage cameraDisplay;
        [SerializeField] private AspectRatioFitter aspectFitter;

        [Header("AR")]
        [SerializeField] private ARCameraManager arCameraManager;

        private WebCamTexture webCamTexture;
        private bool usingARFoundation = false;
        private bool cameraStarted = false;

        // Moto G84 5G camera sensor is rotated 90 degrees
        private int deviceCameraRotation = 0;
        private bool deviceCameraMirror = false;

        private void Start()
        {
            // CameraFix handles camera — avoid dual-camera conflict
            if (FindObjectOfType<CameraFix>() != null)
            {
                Debug.Log("[CameraController] CameraFix present — disabling self to avoid conflict.");
                enabled = false;
                return;
            }

            StartCoroutine(InitializeCamera());
        }

        private IEnumerator InitializeCamera()
        {
            // Request camera permission
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("[CameraController] Camera permission denied!");
                yield break;
            }

            // Try ARFoundation first
            if (arCameraManager != null && arCameraManager.enabled)
            {
                Debug.Log("[CameraController] Trying ARFoundation...");
                arCameraManager.frameReceived += OnARFrameReceived;
                yield return new WaitForSeconds(3f);

                if (!usingARFoundation)
                {
                    Debug.LogWarning("[CameraController] ARFoundation not providing frames, falling back to WebCamTexture.");
                    arCameraManager.frameReceived -= OnARFrameReceived;
                    StartWebCamFallback();
                }
            }
            else
            {
                StartWebCamFallback();
            }
        }

        private void OnARFrameReceived(ARCameraFrameEventArgs args)
        {
            usingARFoundation = true;
            cameraStarted = true;
            Debug.Log("[CameraController] ARFoundation frame received!");
        }

        private void StartWebCamFallback()
        {
            Debug.Log("[CameraController] Starting WebCamTexture fallback...");

            // Find back camera
            WebCamDevice? backCamera = null;
            foreach (var device in WebCamTexture.devices)
            {
                Debug.Log($"[CameraController] Found camera: {device.name}, isFront: {device.isFrontFacing}");
                if (!device.isFrontFacing)
                {
                    backCamera = device;
                    break;
                }
            }

            if (backCamera == null && WebCamTexture.devices.Length > 0)
            {
                backCamera = WebCamTexture.devices[0];
            }

            if (backCamera == null)
            {
                Debug.LogError("[CameraController] No camera found!");
                return;
            }

            webCamTexture = new WebCamTexture(backCamera.Value.name, 1280, 720, 30);
            webCamTexture.Play();

            if (cameraDisplay != null)
            {
                cameraDisplay.texture = webCamTexture;
                cameraDisplay.gameObject.SetActive(true);
            }

            cameraStarted = true;
            Debug.Log($"[CameraController] WebCamTexture started: {backCamera.Value.name}");
        }

        private void Update()
        {
            if (webCamTexture == null || !webCamTexture.isPlaying) return;
            if (cameraDisplay == null) return;

            // Get rotation from device
            deviceCameraRotation = webCamTexture.videoRotationAngle;
            deviceCameraMirror = webCamTexture.videoVerticallyMirrored;

            // Apply rotation and flip correction
            ApplyCameraTransform();
        }

        private void ApplyCameraTransform()
        {
            if (cameraDisplay == null) return;

            // Reset transform
            cameraDisplay.rectTransform.localEulerAngles = Vector3.zero;
            cameraDisplay.uvRect = new Rect(0, 0, 1, 1);

            // Apply rotation
            float rotation = -deviceCameraRotation;

            // Fix for Android: cameras are often rotated 90 degrees
            // Moto G84 5G specific: sensor rotation is typically 90 degrees
            cameraDisplay.rectTransform.localEulerAngles = new Vector3(0, 0, rotation);

            // Fix vertical mirror if needed
            if (deviceCameraMirror)
            {
                // Flip UV vertically
                cameraDisplay.uvRect = new Rect(0, 1, 1, -1);
            }

            // Update aspect ratio
            if (aspectFitter != null && webCamTexture.width > 0)
            {
                float aspect = (float)webCamTexture.width / webCamTexture.height;
                if (deviceCameraRotation == 90 || deviceCameraRotation == 270)
                    aspect = 1f / aspect;
                aspectFitter.aspectRatio = aspect;
            }
        }

        public WebCamTexture GetWebCamTexture()
        {
            return webCamTexture;
        }

        public bool IsCameraReady()
        {
            return cameraStarted && (usingARFoundation || (webCamTexture != null && webCamTexture.isPlaying && webCamTexture.width > 16));
        }

        private void OnDestroy()
        {
            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                webCamTexture = null;
            }

            if (arCameraManager != null)
            {
                arCameraManager.frameReceived -= OnARFrameReceived;
            }
        }
    }
}
