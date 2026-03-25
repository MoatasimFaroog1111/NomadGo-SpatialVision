using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
    /// <summary>
    /// CameraFix v7 — Black-screen + flip fixes for Moto G84 5G (Android 15, Vulkan)
    ///
    /// Black screen root cause: cam.depth=-10 meant our OnPostRender GL drawing happened
    /// BEFORE other cameras (AR camera, depth=0) which then cleared the screen to black.
    /// Fix: cam.depth=100 → we render LAST, nothing can overwrite our camera feed.
    /// All other cameras are disabled so nothing clears our output.
    ///
    /// Camera flip root cause: On Vulkan (Android 15 default), Graphics.Blit writes
    /// the RenderTexture with Y=0 at TOP, while GL.TexCoord2 addresses with Y=0 at
    /// BOTTOM. This inverts the V axis. Fix: detect SystemInfo.graphicsUVStartsAtTop
    /// and swap v0/v1 in all UV mappings.
    ///
    /// Two-step render:
    ///   1. Update(): Graphics.Blit(webCamTexture → blitRT)  — converts OES → ARGB32
    ///   2. OnPostRender(): GL quads with correct rotation + V-flip UVs
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera       cam;
        private WebCamTexture webCamTexture;
        private RenderTexture blitRT;
        private Material      drawMat;
        private bool          cameraReady = false;
        private int           rotAngle    = 0;
        private bool          isMirrored  = false;
        private bool          invertV     = false; // true on Vulkan (graphicsUVStartsAtTop)

        public WebCamTexture CameraTexture => webCamTexture;
        public bool          IsReady       => cameraReady;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags    = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.allowHDR      = false;
                cam.allowMSAA     = false;
                // depth=100: render LAST so AR/main cameras cannot overwrite our feed
                cam.depth         = 100;
            }

            // Disable ARCameraBackground (magenta on OpenGL ES)
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "ARCameraBackground")
                {
                    mb.enabled = false;
                    Debug.Log($"[CameraFix] Disabled ARCameraBackground on {mb.gameObject.name}");
                }
            }

            // Disable ALL other cameras so they cannot clear our camera feed
            foreach (var c in FindObjectsOfType<Camera>())
            {
                if (c == cam) continue;
                c.enabled = false;
                Debug.Log($"[CameraFix] Disabled camera: {c.gameObject.name} (was depth={c.depth})");
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

            string camName = "";
            foreach (var device in WebCamTexture.devices)
            {
                Debug.Log($"[CameraFix] Device: {device.name} front={device.isFrontFacing}");
                if (!device.isFrontFacing) { camName = device.name; break; }
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

            float timeout = 10f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0) { Debug.LogError("[CameraFix] Camera timeout!"); yield break; }
                yield return null;
            }

            yield return new WaitForSeconds(0.3f);

            rotAngle   = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;

            // Vulkan (Android 15 default): Graphics.Blit stores RT with Y=0 at TOP,
            // but GL.TexCoord2 addresses V=0 at BOTTOM → must invert V axis.
            invertV = SystemInfo.graphicsUVStartsAtTop;

            Debug.Log($"[CameraFix] Ready: {webCamTexture.width}x{webCamTexture.height} " +
                      $"rotAngle={rotAngle} mirror={isMirrored} " +
                      $"invertV={invertV} api={SystemInfo.graphicsDeviceType}");

            blitRT = new RenderTexture(webCamTexture.width, webCamTexture.height, 0, RenderTextureFormat.ARGB32);
            blitRT.Create();

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            drawMat = new Material(shader);

            cameraReady = true;
        }

        private void Update()
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying) return;
            if (!webCamTexture.didUpdateThisFrame) return;
            Graphics.Blit(webCamTexture, blitRT);
        }

        private void OnPostRender()
        {
            if (!cameraReady || blitRT == null || drawMat == null) return;

            drawMat.mainTexture = blitRT;

            GL.PushMatrix();
            GL.LoadOrtho();
            drawMat.SetPass(0);
            GL.Begin(GL.QUADS);

            // v0/v1 account for Vulkan Y-axis inversion:
            // invertV=false (OpenGL ES): v0=0 (bottom), v1=1 (top)  → standard
            // invertV=true  (Vulkan)   : v0=1 (top),    v1=0 (bottom) → flipped
            float v0 = invertV ? 1f : 0f;
            float v1 = invertV ? 0f : 1f;

            // Vertex order: BL(0,0) → BR(1,0) → TR(1,1) → TL(0,1) in screen space
            if (rotAngle == 0 || rotAngle == 360)
            {
                if (isMirrored)
                {
                    GL.TexCoord2(1, v1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, v1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, v0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, v0); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(0, v1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, v1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, v0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, v0); GL.Vertex3(0, 1, 0);
                }
            }
            else if (rotAngle == 90)
            {
                // Portrait mode: sensor landscape → rotate 90° CW for display
                if (isMirrored)
                {
                    GL.TexCoord2(0, v0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, v1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, v1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, v0); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(1, v0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, v1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, v1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, v0); GL.Vertex3(0, 1, 0);
                }
            }
            else if (rotAngle == 180)
            {
                if (isMirrored)
                {
                    GL.TexCoord2(0, v1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, v1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, v0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, v0); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(1, v0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, v0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, v1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, v1); GL.Vertex3(0, 1, 0);
                }
            }
            else // 270
            {
                if (isMirrored)
                {
                    GL.TexCoord2(1, v1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, v0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, v0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, v1); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(0, v1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, v0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, v0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, v1); GL.Vertex3(0, 1, 0);
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null) { webCamTexture.Stop(); webCamTexture = null; }
            if (blitRT != null) { blitRT.Release(); Destroy(blitRT); blitRT = null; }
            if (drawMat != null) { Destroy(drawMat); drawMat = null; }
        }
    }
}
