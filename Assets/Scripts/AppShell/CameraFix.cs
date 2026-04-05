using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera        cam;
        private WebCamTexture webCamTexture;
        private bool          cameraReady = false;
        private int           rotAngle    = 0;
        private bool          isMirrored  = false;
        private string        diagText    = "Initializing camera...";

        public WebCamTexture CameraTexture => webCamTexture;
        public bool          IsReady       => cameraReady;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags      = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.depth           = 100;
            }

            // Disable any AR camera components to avoid conflicts
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "ARCameraBackground")
                {
                    mb.enabled = false;
                    Debug.Log($"[CameraFix] Disabled ARCameraBackground on {mb.gameObject.name}");
                }
            }

            // Keep only this camera active
            foreach (var c in FindObjectsOfType<Camera>())
            {
                if (c == cam) continue;
                c.enabled = false;
            }
        }

        private void Start() { StartCoroutine(StartCamera()); }

        private IEnumerator StartCamera()
        {
            diagText = "Requesting camera permission...";
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                diagText = "Camera permission denied. Please allow camera access and restart.";
                Debug.LogError("[CameraFix] Camera permission denied.");
                yield break;
            }

            // Pick back-facing camera
            string camName = "";
            foreach (var device in WebCamTexture.devices)
            {
                Debug.Log($"[CameraFix] Found: {device.name} front={device.isFrontFacing}");
                if (!device.isFrontFacing) { camName = device.name; break; }
            }
            if (string.IsNullOrEmpty(camName) && WebCamTexture.devices.Length > 0)
                camName = WebCamTexture.devices[0].name;

            if (string.IsNullOrEmpty(camName))
            {
                diagText = "No camera found on device.";
                Debug.LogError("[CameraFix] No camera device found.");
                yield break;
            }

            diagText = $"Opening camera: {camName}";
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            // Wait for camera to produce valid frames
            float timeout = 30f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                diagText = $"Waiting for camera... ({timeout:F0}s)";
                if (timeout <= 0)
                {
                    diagText = "Camera timed out. Check permissions.";
                    Debug.LogError("[CameraFix] Camera startup timed out.");
                    yield break;
                }
                yield return null;
            }

            // Brief stabilisation delay
            yield return new WaitForSeconds(1.5f);

            rotAngle   = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;

            cameraReady = true;
            diagText    = "";
            Debug.Log($"[CameraFix] Ready. rot={rotAngle} mirrored={isMirrored} " +
                      $"size={webCamTexture.width}x{webCamTexture.height}");
        }

        // ── Camera rendering via GUI.DrawTexture — no shader dependency ──────

        private void OnGUI()
        {
            // Show status message while camera is not ready
            if (!cameraReady || webCamTexture == null)
            {
                if (!string.IsNullOrEmpty(diagText))
                {
                    var style = new GUIStyle(GUI.skin.box);
                    style.fontSize  = Mathf.Max(20, Screen.height / 30);
                    style.fontStyle = FontStyle.Bold;
                    style.normal.textColor = Color.yellow;
                    style.alignment = TextAnchor.MiddleCenter;
                    style.wordWrap  = true;
                    GUI.Box(new Rect(20, Screen.height / 2 - 60,
                                    Screen.width - 40, 120), diagText, style);
                }
                return;
            }

            float W = Screen.width;
            float H = Screen.height;

            // Rotate around screen center to match device orientation
            Matrix4x4 savedMatrix = GUI.matrix;
            Vector2    pivot       = new Vector2(W / 2f, H / 2f);
            GUIUtility.RotateAroundPivot(-rotAngle, pivot);

            // When rotated 90/270 the camera's aspect fills the screen differently
            Rect drawRect;
            if (rotAngle == 90 || rotAngle == 270)
            {
                // Camera is landscape-sized; we need to fit it into portrait screen
                float camAspect = (float)webCamTexture.width / webCamTexture.height;
                float drawW     = H * camAspect;
                drawRect = new Rect((W - drawW) / 2f, 0, drawW, H);
            }
            else
            {
                drawRect = new Rect(0, 0, W, H);
            }

            // Flip horizontal if mirrored (front camera)
            if (isMirrored)
            {
                // Mirror by scaling around pivot
                GUIUtility.ScaleAroundPivot(new Vector2(-1, 1), pivot);
            }

            GUI.DrawTexture(drawRect, webCamTexture,
                            ScaleMode.ScaleToFit, false);

            GUI.matrix = savedMatrix;
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null) { webCamTexture.Stop(); webCamTexture = null; }
        }
    }
}
