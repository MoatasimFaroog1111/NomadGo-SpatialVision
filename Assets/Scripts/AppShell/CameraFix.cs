using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Fixes camera display for ARFoundation on Android.
    /// Handles black screen and upside-down issues on Moto G84 5G.
    /// Uses WebCamTexture as fallback when ARCore is unavailable.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private ARCameraBackground arBackground;
        private ARCameraManager arCameraManager;
        private WebCamTexture webCamTexture;
        private RawImage backgroundImage;
        private bool arWorking = false;
        private bool fallbackStarted = false;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            arBackground = GetComponent<ARCameraBackground>();
            arCameraManager = GetComponent<ARCameraManager>();

            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.allowHDR = false;
                cam.allowMSAA = false;
            }
        }

        private void Start()
        {
            // Ensure ARCameraBackground exists
            if (arBackground == null)
            {
                arBackground = gameObject.AddComponent<ARCameraBackground>();
            }

            // Ensure ARCameraManager exists
            if (arCameraManager == null)
            {
                arCameraManager = gameObject.AddComponent<ARCameraManager>();
                arCameraManager.autoFocusRequested = true;
                arCameraManager.requestedFacingDirection = CameraFacingDirection.World;
            }

            // Subscribe to AR frames
            arCameraManager.frameReceived += OnARFrameReceived;

            // Start fallback timer
            StartCoroutine(CheckARAndFallback());
        }

        private void OnARFrameReceived(ARCameraFrameEventArgs args)
        {
            arWorking = true;
        }

        private IEnumerator CheckARAndFallback()
        {
            // Wait for AR to initialize
            yield return new WaitForSeconds(4f);

            if (!arWorking && !fallbackStarted)
            {
                Debug.LogWarning("[CameraFix] ARCore not working after 4s. Starting WebCamTexture fallback.");
                StartWebCamFallback();
            }
        }

        private void StartWebCamFallback()
        {
            fallbackStarted = true;

            // Disable AR background
            if (arBackground != null)
                arBackground.enabled = false;

            // Request camera permission
            StartCoroutine(RequestAndStartCamera());
        }

        private IEnumerator RequestAndStartCamera()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("[CameraFix] Camera permission denied!");
                yield break;
            }

            // Find back camera
            string cameraName = "";
            foreach (var device in WebCamTexture.devices)
            {
                if (!device.isFrontFacing)
                {
                    cameraName = device.name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(cameraName) && WebCamTexture.devices.Length > 0)
                cameraName = WebCamTexture.devices[0].name;

            if (string.IsNullOrEmpty(cameraName))
            {
                Debug.LogError("[CameraFix] No camera device found!");
                yield break;
            }

            webCamTexture = new WebCamTexture(cameraName, 1280, 720, 30);
            webCamTexture.Play();

            yield return new WaitUntil(() => webCamTexture.width > 16);

            // Create background quad to display camera
            CreateCameraBackground();

            Debug.Log($"[CameraFix] WebCamTexture fallback active: {cameraName} {webCamTexture.width}x{webCamTexture.height}");
        }

        private void CreateCameraBackground()
        {
            // Create a Canvas in world space behind everything
            var canvasGO = new GameObject("CameraBackground");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.farClipPlane - 1f;
            canvas.sortingOrder = -100;

            var canvasScaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasScaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1080, 1920);

            // Create RawImage
            var imageGO = new GameObject("CameraImage");
            imageGO.transform.SetParent(canvasGO.transform, false);
            backgroundImage = imageGO.AddComponent<RawImage>();
            backgroundImage.texture = webCamTexture;

            var rect = imageGO.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Fix orientation
            FixCameraOrientation();
        }

        private void FixCameraOrientation()
        {
            if (backgroundImage == null || webCamTexture == null) return;

            int rotation = webCamTexture.videoRotationAngle;
            bool mirrored = webCamTexture.videoVerticallyMirrored;

            Debug.Log($"[CameraFix] Camera rotation: {rotation}, mirrored: {mirrored}");

            // Apply rotation to RawImage
            backgroundImage.rectTransform.localEulerAngles = new Vector3(0, 0, -rotation);

            // Fix mirror
            if (mirrored)
            {
                backgroundImage.uvRect = new Rect(0, 1, 1, -1);
            }
            else
            {
                backgroundImage.uvRect = new Rect(0, 0, 1, 1);
            }
        }

        private void Update()
        {
            if (cam != null && cam.clearFlags != CameraClearFlags.SolidColor)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }

            // Update orientation in case it changes
            if (fallbackStarted && backgroundImage != null && webCamTexture != null && webCamTexture.isPlaying)
            {
                FixCameraOrientation();
            }
        }

        private void OnDestroy()
        {
            if (arCameraManager != null)
                arCameraManager.frameReceived -= OnARFrameReceived;

            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                webCamTexture = null;
            }
        }
    }
}
