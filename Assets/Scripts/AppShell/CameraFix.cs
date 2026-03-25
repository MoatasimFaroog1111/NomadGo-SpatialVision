using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// CameraFix v9 — Complete rewrite using Canvas+RawImage (Unity's recommended approach).
    ///
    /// Previous approach (OnPostRender + GL) was replaced because:
    ///  - GL UV coordinate system differs between GLES and Vulkan in unpredictable ways
    ///  - OnPostRender can be silently skipped when the camera isn't the final renderer
    ///  - No reliable way to know which UV flip to apply at runtime
    ///
    /// New approach (Canvas + RawImage):
    ///  - Unity's UI system handles OES → display conversion internally
    ///  - flipY formula from official Unity docs:
    ///      flipY = (videoVerticallyMirrored != SystemInfo.graphicsUVStartsAtTop)
    ///  - Rotation handled via RectTransform.localRotation (no manual UV math)
    ///  - Scale-to-fill handles portrait/landscape aspect ratio mismatch
    ///  - Works identically on GLES and Vulkan
    ///
    /// Black screen fix: ARSession disabled in AppManager so ARCore never
    /// steals the camera device from WebCamTexture.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private RenderTexture blitRT;
        private RawImage rawImage;
        private bool cameraReady = false;
        private string errorMsg = "";

        public WebCamTexture CameraTexture => webCamTexture;
        public bool IsReady => cameraReady;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            // Minimal camera setup — just provides the black background under the Canvas
            cam.clearFlags    = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR      = false;
            cam.allowMSAA     = false;
            cam.depth         = -10;

            // Kill every AR component that could steal the camera device
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == null) continue;
                string t = mb.GetType().Name;
                if (t == "ARCameraBackground" || t == "ARSession" ||
                    t == "ARCameraManager"   || t == "ARInputManager" ||
                    t == "ARPlaneManager"    || t == "ARRaycastManager")
                {
                    mb.enabled = false;
                    mb.gameObject.SetActive(false);
                    Debug.Log($"[CameraFix] Disabled {t}");
                }
            }

            // Disable all other cameras
            foreach (var c in FindObjectsOfType<Camera>())
            {
                if (c != cam) { c.enabled = false; }
            }

            // Build the fullscreen camera display layer
            BuildCameraCanvas();
        }

        private void BuildCameraCanvas()
        {
            var canvasGO = new GameObject("[CameraCanvas]");
            DontDestroyOnLoad(canvasGO);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1000; // Behind all other UI

            canvasGO.AddComponent<CanvasScaler>();

            var imgGO = new GameObject("[CameraImage]");
            imgGO.transform.SetParent(canvasGO.transform, false);
            rawImage = imgGO.AddComponent<RawImage>();
            rawImage.color = Color.black; // Black until camera is ready

            var rt = rawImage.GetComponent<RectTransform>();
            rt.anchorMin        = Vector2.zero;
            rt.anchorMax        = Vector2.one;
            rt.sizeDelta        = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private void Start() => StartCoroutine(StartCamera());

        private IEnumerator StartCamera()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                errorMsg = "Camera permission denied";
                Debug.LogError("[CameraFix] " + errorMsg);
                yield break;
            }

            // Find back camera
            string camName = "";
            foreach (var d in WebCamTexture.devices)
            {
                Debug.Log($"[CameraFix] Device: {d.name} front={d.isFrontFacing}");
                if (!d.isFrontFacing) { camName = d.name; break; }
            }
            if (string.IsNullOrEmpty(camName) && WebCamTexture.devices.Length > 0)
                camName = WebCamTexture.devices[0].name;

            if (string.IsNullOrEmpty(camName))
            {
                errorMsg = "No camera device found";
                Debug.LogError("[CameraFix] " + errorMsg);
                yield break;
            }

            Debug.Log($"[CameraFix] Opening camera: {camName}");
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            // Wait up to 30 s for the camera to deliver real frames
            float timeout = 30f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0)
                {
                    errorMsg = $"Camera timeout (w={webCamTexture.width} playing={webCamTexture.isPlaying})";
                    Debug.LogError("[CameraFix] " + errorMsg);
                    yield break;
                }
                yield return null;
            }

            // Extra stabilisation frame
            yield return new WaitForSeconds(0.5f);

            int  rotAngle = webCamTexture.videoRotationAngle;
            bool mirror   = webCamTexture.videoVerticallyMirrored;

            // Official Unity formula: flip Y when mirrored XOR graphicsUVStartsAtTop
            // See: docs.unity3d.com/ScriptReference/WebCamTexture-videoVerticallyMirrored
            bool flipY = (mirror != SystemInfo.graphicsUVStartsAtTop);

            Debug.Log($"[CameraFix] Ready — size={webCamTexture.width}x{webCamTexture.height}" +
                      $" rot={rotAngle} mirror={mirror}" +
                      $" uvStartsAtTop={SystemInfo.graphicsUVStartsAtTop}" +
                      $" flipY={flipY} api={SystemInfo.graphicsDeviceType}");

            // Convert OES → ARGB32 RenderTexture (avoids OES sampling issues in UI shader)
            blitRT = new RenderTexture(
                webCamTexture.width, webCamTexture.height, 0, RenderTextureFormat.ARGB32);
            blitRT.Create();

            // Wire up the display
            rawImage.color   = Color.white; // Show the texture now
            rawImage.texture = blitRT;

            var rect = rawImage.GetComponent<RectTransform>();

            // Apply rotation — Unity's UI convention: negative angle = CW rotation
            rect.localRotation = Quaternion.Euler(0f, 0f, -rotAngle);

            // Scale so the image fills the screen (Scale-And-Crop style)
            float scaleX, scaleY;
            if (rotAngle == 90 || rotAngle == 270)
            {
                // Landscape texture displayed as portrait:
                // camWidth  maps to screen height direction after rotation
                // camHeight maps to screen width  direction after rotation
                float s = Mathf.Max(
                    (float)Screen.height / webCamTexture.width,
                    (float)Screen.width  / webCamTexture.height
                );
                scaleX = s;
                scaleY = flipY ? -s : s;
            }
            else
            {
                float s = Mathf.Max(
                    (float)Screen.width  / webCamTexture.width,
                    (float)Screen.height / webCamTexture.height
                );
                scaleX = s;
                scaleY = flipY ? -s : s;
            }

            rect.localScale = new Vector3(scaleX, scaleY, 1f);

            cameraReady = true;
            Debug.Log($"[CameraFix] Display ready: scale=({scaleX:F2},{scaleY:F2})");
        }

        private void Update()
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying) return;
            // Blit every frame — convert OES → ARGB32 in blitRT
            Graphics.Blit(webCamTexture, blitRT);
        }

        private void OnGUI()
        {
            // Show camera info / error at bottom of screen for debugging
            if (!cameraReady)
            {
                string msg = string.IsNullOrEmpty(errorMsg)
                    ? "Camera starting..."
                    : "Camera error: " + errorMsg;
                GUI.Label(new Rect(10, Screen.height - 40, Screen.width - 20, 36), msg);
            }
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null) { webCamTexture.Stop(); webCamTexture = null; }
            if (blitRT != null)
            {
                blitRT.Release();
                Destroy(blitRT);
                blitRT = null;
            }
        }
    }
}
