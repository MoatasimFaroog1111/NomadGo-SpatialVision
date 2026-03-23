using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Camera display fix for Android.
    /// Renders WebCamTexture via a Blit shader to fix YUV color and orientation.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private Texture2D displayTexture;
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
            webCamTexture = new WebCamTexture(camName, Screen.width, Screen.height, 30);
            webCamTexture.Play();

            // Wait for camera to produce valid frames
            float timeout = 6f;
            while (!webCamTexture.didUpdateThisFrame || webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0)
                {
                    Debug.LogError("[CameraFix] Camera timed out!");
                    yield break;
                }
                yield return null;
            }

            Debug.Log($"[CameraFix] Camera OK: {webCamTexture.width}x{webCamTexture.height}, rot={webCamTexture.videoRotationAngle}, mirror={webCamTexture.videoVerticallyMirrored}");

            CreateBackground();
        }

        private void CreateBackground()
        {
            // ScreenSpaceOverlay canvas — always on top, no camera dependency
            bgCanvas = new GameObject("CamBG_Canvas");
            DontDestroyOnLoad(bgCanvas);

            var canvas = bgCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -200;

            var scaler = bgCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Screen.width, Screen.height);
            scaler.matchWidthOrHeight = 0.5f;

            bgCanvas.AddComponent<GraphicRaycaster>();

            // Full-screen RawImage
            var imgGO = new GameObject("CamBG_Image");
            imgGO.transform.SetParent(bgCanvas.transform, false);

            bgImage = imgGO.AddComponent<RawImage>();
            bgImage.color = Color.white;

            var rect = imgGO.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            // Assign texture and fix orientation
            ApplyTexture();

            Debug.Log("[CameraFix] Camera background created!");
        }

        private void ApplyTexture()
        {
            if (bgImage == null || webCamTexture == null) return;

            int rotation = webCamTexture.videoRotationAngle;
            bool mirrored = webCamTexture.videoVerticallyMirrored;

            Debug.Log($"[CameraFix] Applying texture: rot={rotation}, mirrored={mirrored}");

            // Assign the WebCamTexture directly — Unity handles YUV conversion
            bgImage.texture = webCamTexture;

            // Reset transform
            bgImage.rectTransform.localEulerAngles = Vector3.zero;
            bgImage.rectTransform.localScale = Vector3.one;

            // Fix UV for mirror
            float uvY = mirrored ? 1f : 0f;
            float uvH = mirrored ? -1f : 1f;
            bgImage.uvRect = new Rect(0f, uvY, 1f, uvH);

            // Apply rotation — rotate the RectTransform
            bgImage.rectTransform.localEulerAngles = new Vector3(0, 0, -rotation);

            // When rotated 90/270, swap width/height to fill screen properly
            if (rotation == 90 || rotation == 270)
            {
                float sw = Screen.width;
                float sh = Screen.height;
                float camW = webCamTexture.width;
                float camH = webCamTexture.height;

                // Scale so the rotated image fills the screen
                float scaleX = sh / camW;
                float scaleY = sw / camH;
                float scale = Mathf.Max(scaleX, scaleY);

                bgImage.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            else
            {
                // For 0/180 rotation, just fill normally
                bgImage.rectTransform.localScale = Vector3.one;
            }
        }

        private void Update()
        {
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
