using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Renders the back camera as a full-screen background using GL.DrawTexture in OnPostRender.
    /// OnPostRender works even when the scene has no 3D content, unlike OnRenderImage.
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
                if (timeout <= 0) { Debug.LogError("[CameraFix] Timeout!"); yield break; }
                yield return null;
            }

            yield return new WaitForSeconds(0.3f);

            rotAngle = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;
            Debug.Log($"[CameraFix] Ready: {webCamTexture.width}x{webCamTexture.height} rot={rotAngle} mirror={isMirrored}");

            // Create material — Hidden/BlitCopy is the most reliable for GL drawing
            var shader = Shader.Find("Hidden/BlitCopy");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Texture");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            camMat = new Material(shader);
            camMat.mainTexture = webCamTexture;

            cameraReady = true;
        }

        // OnPostRender is called after the camera renders its view
        // Unlike OnRenderImage, it IS called even with an empty scene
        private void OnPostRender()
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying || camMat == null)
                return;

            camMat.mainTexture = webCamTexture;

            GL.PushMatrix();
            GL.LoadOrtho();

            camMat.SetPass(0);

            // UV coordinates based on rotation
            // GL screen: (0,0)=bottom-left, (1,1)=top-right
            float u0 = 0f, u1 = 1f;
            float v0 = isMirrored ? 1f : 0f;
            float v1 = isMirrored ? 0f : 1f;

            GL.Begin(GL.QUADS);
            if (rotAngle == 0)
            {
                GL.TexCoord2(u0, v0); GL.Vertex3(0, 0, 0);
                GL.TexCoord2(u1, v0); GL.Vertex3(1, 0, 0);
                GL.TexCoord2(u1, v1); GL.Vertex3(1, 1, 0);
                GL.TexCoord2(u0, v1); GL.Vertex3(0, 1, 0);
            }
            else if (rotAngle == 90)
            {
                GL.TexCoord2(u0, v1); GL.Vertex3(0, 0, 0);
                GL.TexCoord2(u0, v0); GL.Vertex3(1, 0, 0);
                GL.TexCoord2(u1, v0); GL.Vertex3(1, 1, 0);
                GL.TexCoord2(u1, v1); GL.Vertex3(0, 1, 0);
            }
            else if (rotAngle == 180)
            {
                GL.TexCoord2(u1, v1); GL.Vertex3(0, 0, 0);
                GL.TexCoord2(u0, v1); GL.Vertex3(1, 0, 0);
                GL.TexCoord2(u0, v0); GL.Vertex3(1, 1, 0);
                GL.TexCoord2(u1, v0); GL.Vertex3(0, 1, 0);
            }
            else // 270
            {
                GL.TexCoord2(u1, v0); GL.Vertex3(0, 0, 0);
                GL.TexCoord2(u1, v1); GL.Vertex3(1, 0, 0);
                GL.TexCoord2(u0, v1); GL.Vertex3(1, 1, 0);
                GL.TexCoord2(u0, v0); GL.Vertex3(0, 1, 0);
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
