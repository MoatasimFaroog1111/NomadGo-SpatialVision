using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// CameraFix v10 — Three targeted fixes over v9:
    ///
    ///  FIX 1 – RawImage sizing: v9 used full-screen anchoring which caused the
    ///    camera texture to be STRETCHED to screen dimensions BEFORE rotation,
    ///    distorting the image. Now the RawImage uses exact texture dimensions
    ///    (sizeDelta = camW x camH), centred, then rotated and scaled-to-fill.
    ///    This is the correct Unity approach for displaying camera with rotation.
    ///
    ///  FIX 2 – Orientation locked to Portrait (manifest): fullSensor caused
    ///    videoRotationAngle to change when the user moved the phone, making the
    ///    display flicker/wrong. Portrait lock gives a stable, known rotation.
    ///
    ///  FIX 3 – Diagnostic OnGUI: shows a large, always-visible yellow/red status
    ///    panel so we can see exactly which stage the camera is at, regardless
    ///    of whether the Canvas is rendering correctly.
    ///
    /// Flip formula (official Unity docs):
    ///   flipY = (videoVerticallyMirrored != SystemInfo.graphicsUVStartsAtTop)
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private RenderTexture blitRT;
        private RawImage rawImage;
        private bool cameraReady = false;
        private string cameraStatus = "Initializing...";
        private string errorMsg = "";
        private int camWidth, camHeight;

        public WebCamTexture CameraTexture => webCamTexture;
        public bool IsReady => cameraReady;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.clearFlags     = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR       = false;
            cam.allowMSAA      = false;
            cam.depth          = -10;

            // Kill all AR components that could steal the camera device
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
                }
            }

            // Disable all other cameras
            foreach (var c in FindObjectsOfType<Camera>())
                if (c != cam) c.enabled = false;

            BuildCameraCanvas();
        }

        private void BuildCameraCanvas()
        {
            var go = new GameObject("[CameraCanvas]");
            DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1000;
            go.AddComponent<CanvasScaler>();

            var imgGO = new GameObject("[CameraImage]");
            imgGO.transform.SetParent(go.transform, false);
            rawImage = imgGO.AddComponent<RawImage>();
            rawImage.color = new Color(0, 0, 0, 0); // Fully transparent until ready

            // Centred pivot — will be given exact texture dimensions in coroutine
            var rt = rawImage.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = Vector2.zero;
        }

        private void Start() => StartCoroutine(StartCamera());

        private IEnumerator StartCamera()
        {
            cameraStatus = "Requesting permission...";
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                errorMsg = "Camera permission denied!\nPlease grant camera access in Settings.";
                cameraStatus = "Permission denied";
                yield break;
            }

            // Log all available camera devices
            var devices = WebCamTexture.devices;
            cameraStatus = $"Found {devices.Length} camera(s)";
            for (int i = 0; i < devices.Length; i++)
                Debug.Log($"[CameraFix] Cam {i}: {devices[i].name} front={devices[i].isFrontFacing}");

            if (devices.Length == 0)
            {
                errorMsg = "No camera device found on this device!";
                cameraStatus = "No cameras";
                yield break;
            }

            // Use default back camera (no device name = let Android choose)
            cameraStatus = "Starting camera (640x480)...";
            webCamTexture = new WebCamTexture(640, 480, 30);
            webCamTexture.Play();

            // Wait up to 60 s for real frames
            float timeout = 60f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                cameraStatus = $"Waiting for camera... ({timeout:F0}s left)  w={webCamTexture.width}";
                if (timeout <= 0)
                {
                    webCamTexture.Stop();

                    // Retry with explicit back-camera name
                    string backName = "";
                    foreach (var d in devices)
                        if (!d.isFrontFacing) { backName = d.name; break; }

                    if (!string.IsNullOrEmpty(backName))
                    {
                        cameraStatus = $"Retrying with: {backName}";
                        webCamTexture = new WebCamTexture(backName, 640, 480, 30);
                        webCamTexture.Play();
                        timeout = 30f;

                        while (webCamTexture.width <= 16)
                        {
                            timeout -= Time.deltaTime;
                            cameraStatus = $"Retry... ({timeout:F0}s) w={webCamTexture.width}";
                            if (timeout <= 0) break;
                            yield return null;
                        }
                    }

                    if (webCamTexture.width <= 16)
                    {
                        errorMsg = $"Camera timeout!\nCheck camera permissions in Settings.\nDevices found: {devices.Length}";
                        cameraStatus = "Timeout";
                        yield break;
                    }
                }
                yield return null;
            }

            yield return new WaitForSeconds(0.3f);

            camWidth  = webCamTexture.width;
            camHeight = webCamTexture.height;
            int  rotAngle = webCamTexture.videoRotationAngle;
            bool mirror   = webCamTexture.videoVerticallyMirrored;
            bool flipY    = (mirror != SystemInfo.graphicsUVStartsAtTop);

            Debug.Log($"[CameraFix] Ready: {camWidth}x{camHeight}" +
                      $" rot={rotAngle} mirror={mirror} flipY={flipY}" +
                      $" api={SystemInfo.graphicsDeviceType}" +
                      $" uvTop={SystemInfo.graphicsUVStartsAtTop}");

            blitRT = new RenderTexture(camWidth, camHeight, 0, RenderTextureFormat.ARGB32);
            blitRT.Create();

            // ── FIX 1: Exact texture dimensions, not full-screen anchoring ──
            // Full-screen anchoring would stretch the texture to 1080×2340 BEFORE
            // rotation, distorting aspect ratio. Exact dimensions + scale-to-fill is correct.
            rawImage.texture = blitRT;
            rawImage.color   = Color.white;

            var rect = rawImage.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(camWidth, camHeight); // ← KEY FIX

            // Rotate so the landscape texture appears portrait
            rect.localRotation = Quaternion.Euler(0f, 0f, -rotAngle);

            // Scale to fill the portrait screen
            float scale;
            if (rotAngle == 90 || rotAngle == 270)
            {
                // After -90° rotation: visual width = camHeight, visual height = camWidth
                scale = Mathf.Max(
                    (float)Screen.width  / camHeight,
                    (float)Screen.height / camWidth
                );
            }
            else
            {
                scale = Mathf.Max(
                    (float)Screen.width  / camWidth,
                    (float)Screen.height / camHeight
                );
            }

            rect.localScale = new Vector3(scale, flipY ? -scale : scale, 1f);

            cameraStatus = $"OK {camWidth}x{camHeight} rot={rotAngle} flip={flipY} scale={scale:F2}";
            cameraReady  = true;
            errorMsg     = "";
        }

        private void Update()
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying) return;
            Graphics.Blit(webCamTexture, blitRT);
        }

        // ── FIX 3: Always-visible diagnostic display ─────────────────────────────
        // This OnGUI renders regardless of Canvas state, so the user always sees
        // the current camera status even if the RawImage isn't working yet.
        private void OnGUI()
        {
            if (cameraReady) return; // Camera working normally — no diagnostic needed

            float W = Screen.width;
            float H = Screen.height;
            float panelY  = H * 0.3f;
            float panelH  = H * 0.4f;

            // Background
            Color bgColor = string.IsNullOrEmpty(errorMsg)
                ? new Color(1f, 0.8f, 0f, 0.92f)   // Yellow = starting
                : new Color(0.85f, 0.1f, 0.1f, 0.95f); // Red = error

            GUI.color = bgColor;
            GUI.DrawTexture(new Rect(0, panelY, W, panelH), Texture2D.whiteTexture);
            GUI.color = Color.black;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = Mathf.Max(24, (int)(W * 0.04f)),
                wordWrap  = true,
                alignment = TextAnchor.UpperLeft
            };

            string diagText = string.IsNullOrEmpty(errorMsg)
                ? $"[NomadGo Camera]\n{cameraStatus}\n\nDevices: {WebCamTexture.devices.Length}"
                : $"[NomadGo Camera ERROR]\n{errorMsg}\n\n{cameraStatus}";

            GUI.Label(new Rect(20, panelY + 15, W - 40, panelH - 30), diagText, style);
            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null) { webCamTexture.Stop(); webCamTexture = null; }
            if (blitRT != null) { blitRT.Release(); Destroy(blitRT); blitRT = null; }
        }
    }
}
