using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Renders the back camera as a full-screen background using OnRenderImage.
    /// This approach bypasses YUV issues by letting Unity handle the conversion.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private Material camMat;
        private bool cameraReady = false;

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

            Debug.Log($"[CameraFix] Starting: {camName}");
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            // Wait until camera is actually producing frames
            float timeout = 10f;
            while (webCamTexture.width <= 16 || !webCamTexture.didUpdateThisFrame)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0) { Debug.LogError("[CameraFix] Timeout!"); yield break; }
                yield return null;
            }

            Debug.Log($"[CameraFix] Camera ready: {webCamTexture.width}x{webCamTexture.height} rot={webCamTexture.videoRotationAngle}");

            // Create material for rendering
            // Use Unlit/Texture — simplest possible, no color conversion needed
            // Unity's WebCamTexture already provides RGBA on Android via internal YUV conversion
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("UI/Default");
            camMat = new Material(shader);
            camMat.mainTexture = webCamTexture;

            cameraReady = true;
        }

        // OnRenderImage is called after the camera renders — we draw the webcam here
        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying)
            {
                Graphics.Blit(src, dest);
                return;
            }

            // Build a matrix to handle rotation and mirroring
            int rotation = webCamTexture.videoRotationAngle;
            bool mirrored = webCamTexture.videoVerticallyMirrored;

            // Create transform matrix for UV
            Matrix4x4 uvMat = Matrix4x4.identity;

            // Handle vertical mirror
            if (mirrored)
            {
                // Flip Y: translate to center, scale Y by -1, translate back
                uvMat = Matrix4x4.TRS(new Vector3(0, 1, 0), Quaternion.identity, new Vector3(1, -1, 1));
            }

            camMat.mainTexture = webCamTexture;

            // Blit webcam texture to screen
            Graphics.Blit(webCamTexture, dest, camMat);
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                webCamTexture = null;
            }
            if (camMat != null)
            {
                Destroy(camMat);
                camMat = null;
            }
        }
    }
}
