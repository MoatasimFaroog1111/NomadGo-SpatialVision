using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Camera display fix for Android - uses WebCamTexture directly on a RawImage.
    /// Handles orientation correction for Moto G84 5G.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private RawImage bgImage;
        private GameObject bgCanvas;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.depth = -10;
            }
        }

        private void Start()
        {
            StartCoroutine(StartCamera());
        }

        private IEnumerator StartCamera()
        {
            // Request camera permission
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("[CameraFix] Camera permission denied!");
                yield break;
            }

            // Find back-facing camera
            string camName = "";
            foreach (var device in WebCamTexture.devices)
            {
                Debug.Log($"[CameraFix] Device: {device.name}, front: {device.isFrontFacing}");
                if (!device.isFrontFacing)
                {
                    camName = device.name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(camName) && WebCamTexture.devices.Length > 0)
                camName = WebCamTexture.devices[0].name;

            if (string.IsNullOrEmpty(camName))
            {
                Debug.LogError("[CameraFix] No camera found!");
                yield break;
            }

            Debug.Log($"[CameraFix] Starting camera: {camName}");
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            // Wait for camera to start
            float timeout = 5f;
            while (webCamTexture.width <= 16 && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (webCamTexture.width <= 16)
            {
                Debug.LogError("[CameraFix] Camera failed to start!");
                yield break;
            }

            Debug.Log($"[CameraFix] Camera started: {webCamTexture.width}x{webCamTexture.height}, rotation: {webCamTexture.videoRotationAngle}");

            // Create background canvas
            CreateBackground();
        }

        private void CreateBackground()
        {
            // Create Screen Space Overlay canvas (always visible, no camera needed)
            bgCanvas = new GameObject("CamBG_Canvas");
            var canvas = bgCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;

            bgCanvas.AddComponent<CanvasScaler>();
            bgCanvas.AddComponent<GraphicRaycaster>();

            // Create RawImage that fills the entire screen
            var imgGO = new GameObject("CamBG_Image");
            imgGO.transform.SetParent(bgCanvas.transform, false);

            bgImage = imgGO.AddComponent<RawImage>();
            bgImage.texture = webCamTexture;
            bgImage.color = Color.white;

            var rect = imgGO.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Apply orientation fix
            ApplyOrientation();

            Debug.Log("[CameraFix] Camera background created successfully!");
        }

        private void ApplyOrientation()
        {
            if (bgImage == null || webCamTexture == null) return;

            int rotation = webCamTexture.videoRotationAngle;
            bool mirrored = webCamTexture.videoVerticallyMirrored;

            Debug.Log($"[CameraFix] Rotation={rotation}, Mirrored={mirrored}");

            // Apply rotation
            bgImage.rectTransform.localEulerAngles = new Vector3(0, 0, -rotation);

            // Apply mirror correction
            if (mirrored)
            {
                // Flip UV vertically
                bgImage.uvRect = new Rect(0, 1, 1, -1);
            }
            else
            {
                bgImage.uvRect = new Rect(0, 0, 1, 1);
            }

            // Scale to fill screen correctly when rotated
            if (rotation == 90 || rotation == 270)
            {
                float screenAspect = (float)Screen.width / Screen.height;
                float camAspect = (float)webCamTexture.height / webCamTexture.width;
                float scale = screenAspect / camAspect;
                bgImage.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void Update()
        {
            // Keep camera flags correct
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }

        private void OnDestroy()
        {
            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                webCamTexture = null;
            }
            if (bgCanvas != null)
            {
                Destroy(bgCanvas);
            }
        }
    }
}
