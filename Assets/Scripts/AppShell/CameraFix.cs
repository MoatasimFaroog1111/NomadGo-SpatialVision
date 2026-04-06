using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Displays the device back-camera as a full-screen background using
    /// a Screen-Space Overlay Canvas (sorting order -100), so all UI renders on top.
    /// Uses RawImage — no GL shader dependency, correct orientation via RectTransform.
    /// </summary>
    public class CameraFix : MonoBehaviour
    {
        private WebCamTexture webCamTexture;
        private bool          cameraReady = false;
        private RawImage      rawImage;
        private string        diagText    = "Initializing...";

        public WebCamTexture CameraTexture => webCamTexture;
        public bool          IsReady       => cameraReady;

        private void Awake()
        {
            // Disable ARCameraBackground so it doesn't fight with our WebCamTexture display
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "ARCameraBackground")
                {
                    mb.enabled = false;
                    Debug.Log($"[CameraFix] Disabled ARCameraBackground on {mb.gameObject.name}");
                }
            }
        }

        private void Start()
        {
            BuildCameraCanvas();
            StartCoroutine(StartCamera());
        }

        private void BuildCameraCanvas()
        {
            var canvasGo = new GameObject("[CameraBG]");
            var canvas   = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;   // behind all UI
            canvasGo.AddComponent<CanvasScaler>();

            var imgGo = new GameObject("feed");
            imgGo.transform.SetParent(canvasGo.transform, false);
            rawImage = imgGo.AddComponent<RawImage>();

            // Center-anchored — size set explicitly after camera starts
            var rt = rawImage.rectTransform;
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(Screen.width, Screen.height);
            rawImage.color      = Color.black;
        }

        private IEnumerator StartCamera()
        {
            diagText = "Requesting camera permission...";
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                diagText = "Camera permission denied.\nAllow camera access and restart.";
                Debug.LogError("[CameraFix] Camera permission denied.");
                yield break;
            }

            string camName = "";
            foreach (var d in WebCamTexture.devices)
            {
                Debug.Log($"[CameraFix] device: {d.name} front={d.isFrontFacing}");
                if (!d.isFrontFacing) { camName = d.name; break; }
            }
            if (string.IsNullOrEmpty(camName) && WebCamTexture.devices.Length > 0)
                camName = WebCamTexture.devices[0].name;

            if (string.IsNullOrEmpty(camName))
            {
                diagText = "No camera found on device.";
                Debug.LogError("[CameraFix] No camera device found.");
                yield break;
            }

            diagText = "Opening camera...";
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            float timeout = 30f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0)
                {
                    diagText = "Camera timed out. Check permissions.";
                    Debug.LogError("[CameraFix] Camera startup timed out.");
                    yield break;
                }
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);

            int  rotAngle = webCamTexture.videoRotationAngle;
            bool mirrored = webCamTexture.videoVerticallyMirrored;

            int scrW = Screen.width;
            int scrH = Screen.height;
            int camW = webCamTexture.width;
            int camH = webCamTexture.height;

            // Compute scale so the (possibly rotated) camera fills the screen (ScaleAndCrop).
            // After a 90/270° rotation the camera's width maps to screen height and vice-versa.
            float scale;
            if (rotAngle == 90 || rotAngle == 270)
                scale = Mathf.Max((float)scrW / camH, (float)scrH / camW);
            else
                scale = Mathf.Max((float)scrW / camW, (float)scrH / camH);

            rawImage.texture = webCamTexture;
            rawImage.color   = Color.white;

            var rt = rawImage.rectTransform;
            // sizeDelta in local (pre-rotation) space; after rotation it fills the screen
            rt.sizeDelta        = new Vector2(camW * scale, camH * scale);
            // Rotate to correct orientation  (Unity +z = CCW, so -rotAngle = clockwise correction)
            rt.localEulerAngles = new Vector3(0f, 0f, -rotAngle);
            // Mirror only if needed (front cameras)
            rt.localScale       = new Vector3(mirrored ? -1f : 1f, 1f, 1f);

            cameraReady = true;
            diagText    = "";
            Debug.Log($"[CameraFix] Ready rot={rotAngle} mirror={mirrored} " +
                      $"cam={camW}x{camH} scale={scale:F3} " +
                      $"rect={camW * scale:F0}x{camH * scale:F0}");
        }

        // Show a status message only while the camera is starting up
        private void OnGUI()
        {
            if (cameraReady || string.IsNullOrEmpty(diagText)) return;

            var s = new GUIStyle(GUI.skin.label);
            s.fontSize         = Mathf.Max(22, Screen.height / 28);
            s.fontStyle        = FontStyle.Bold;
            s.normal.textColor = Color.yellow;
            s.alignment        = TextAnchor.MiddleCenter;
            s.wordWrap         = true;
            GUI.Label(new Rect(20, Screen.height * 0.4f, Screen.width - 40, Screen.height * 0.2f),
                      diagText, s);
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null) { webCamTexture.Stop(); webCamTexture = null; }
        }
    }
}
