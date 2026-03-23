using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Renders the back camera as a full-screen background using GL.DrawTexture in OnPostRender.
    /// Tested on Moto G84 5G (Android 15, portrait mode).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private Material camMat;
        private bool cameraReady = false;
        private int rotAngle = 0;
        private bool isMirrored = false;

        // Public accessor for other scripts (FrameProcessor)
        public WebCamTexture CameraTexture => webCamTexture;
        public bool IsReady => cameraReady;

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

            Debug.Log($"[CameraFix] Opening: {camName}");
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            // Wait until camera is producing frames
            float timeout = 10f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0) { Debug.LogError("[CameraFix] Timeout waiting for camera!"); yield break; }
                yield return null;
            }

            yield return new WaitForSeconds(0.3f);

            // Get raw rotation from device
            rotAngle = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;
            Debug.Log($"[CameraFix] Camera ready: {webCamTexture.width}x{webCamTexture.height} " +
                      $"rotAngle={rotAngle} mirror={isMirrored}");

            // Create material
            var shader = Shader.Find("Hidden/BlitCopy");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Texture");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            camMat = new Material(shader);
            camMat.mainTexture = webCamTexture;

            cameraReady = true;
        }

        // OnPostRender is called after the camera renders — works even with empty scene
        private void OnPostRender()
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying || camMat == null)
                return;

            camMat.mainTexture = webCamTexture;

            GL.PushMatrix();
            GL.LoadOrtho();
            camMat.SetPass(0);

            // GL coordinate system: (0,0) = bottom-left, (1,1) = top-right
            // WebCamTexture UV: (0,0) = bottom-left of raw frame
            //
            // Moto G84 5G in portrait mode:
            //   videoRotationAngle = 90 (rotate CCW 90° to get upright image)
            //   videoVerticallyMirrored = false (back camera, not mirrored)
            //
            // For rotAngle=90 (need to rotate texture 90° CCW):
            //   bottom-left of screen  → top-left of texture    (0,1)
            //   bottom-right of screen → bottom-left of texture  (0,0)
            //   top-right of screen    → bottom-right of texture (1,0)
            //   top-left of screen     → top-right of texture    (1,1)

            GL.Begin(GL.QUADS);

            if (rotAngle == 0)
            {
                // No rotation
                if (isMirrored)
                {
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                }
            }
            else if (rotAngle == 90)
            {
                // Rotate 90° CCW (standard Android portrait back camera)
                if (isMirrored)
                {
                    GL.TexCoord2(1, 1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(0, 1, 0);
                }
            }
            else if (rotAngle == 180)
            {
                // Rotate 180°
                if (isMirrored)
                {
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(1, 1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(0, 1, 0);
                }
            }
            else // 270
            {
                // Rotate 270° CCW (= 90° CW)
                if (isMirrored)
                {
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(1, 0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 1, 0);
                }
            }

            GL.End();
            GL.PopMatrix();
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
