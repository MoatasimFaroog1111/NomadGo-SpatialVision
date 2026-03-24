using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// FIXED v2: Use RawImage instead of GL.DrawTexture to avoid shader issues.
    /// GL.DrawTexture causes PINK/MAGENTA on many Android devices when shader is not found.
    /// RawImage approach works reliably on all Android devices.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private RawImage cameraImage;
        private Canvas cameraCanvas;
        private bool cameraReady = false;
        private int rotAngle = 0;
        private bool isMirrored = false;

        public WebCamTexture CameraTexture => webCamTexture;
        public bool IsReady => cameraReady;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.depth = -10;
            }
        }

        private void Start()
        {
            BuildCameraCanvas();
            StartCoroutine(StartCamera());
        }

        private void BuildCameraCanvas()
        {
            // FIXED: Use a dedicated canvas behind the UI canvas, with RawImage for camera feed
            // This avoids GL shader issues entirely
            var cameraCanvasGO = new GameObject("CameraCanvas");
            DontDestroyOnLoad(cameraCanvasGO);

            cameraCanvas = cameraCanvasGO.AddComponent<Canvas>();
            cameraCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cameraCanvas.sortingOrder = -100; // Behind everything else

            cameraCanvasGO.AddComponent<CanvasScaler>();
            cameraCanvasGO.AddComponent<GraphicRaycaster>();

            // Full-screen RawImage to display camera
            var imageGO = new GameObject("CameraImage");
            imageGO.transform.SetParent(cameraCanvasGO.transform, false);

            var rt = imageGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            cameraImage = imageGO.AddComponent<RawImage>();
            cameraImage.color = Color.black; // Black until camera starts
            cameraImage.raycastTarget = false;
        }

        private IEnumerator StartCamera()
        {
            // Request permission
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
                Debug.Log($"[CameraFix] Device: {device.name} front={device.isFrontFacing}");
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
                Debug.LogError("[CameraFix] No camera device found!");
                yield break;
            }

            Debug.Log($"[CameraFix] Opening camera: {camName}");
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            // Wait for camera to produce frames
            float timeout = 10f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0)
                {
                    Debug.LogError("[CameraFix] Timeout waiting for camera frames!");
                    yield break;
                }
                yield return null;
            }

            yield return new WaitForSeconds(0.3f);

            rotAngle = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;

            Debug.Log($"[CameraFix] Camera ready: {webCamTexture.width}x{webCamTexture.height} " +
                      $"rotation={rotAngle} mirrored={isMirrored}");

            // FIXED: Assign texture to RawImage and apply rotation/flip via RectTransform rotation
            cameraImage.texture = webCamTexture;
            cameraImage.color = Color.white;

            ApplyRotationCorrection();
            cameraReady = true;
        }

        private void ApplyRotationCorrection()
        {
            if (cameraImage == null) return;

            var rt = cameraImage.rectTransform;

            // FIXED: Use rotation and scale to fix orientation
            // rotAngle=90 is standard for portrait Android
            float zRot = 0f;
            float scaleX = 1f;
            float scaleY = 1f;

            switch (rotAngle)
            {
                case 90:
                    zRot = -90f;
                    // After 90° rotation, width becomes height → stretch to fill
                    float aspect = (float)Screen.height / Screen.width;
                    scaleX = aspect;
                    scaleY = aspect;
                    break;
                case 180:
                    zRot = 180f;
                    break;
                case 270:
                    zRot = 90f;
                    aspect = (float)Screen.height / Screen.width;
                    scaleX = aspect;
                    scaleY = aspect;
                    break;
                default:
                    zRot = 0f;
                    break;
            }

            // Flip for mirrored (front camera)
            if (isMirrored) scaleX = -scaleX;

            rt.localEulerAngles = new Vector3(0, 0, zRot);
            rt.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                webCamTexture = null;
            }
            if (cameraCanvas != null)
            {
                Destroy(cameraCanvas.gameObject);
            }
        }
    }
}
