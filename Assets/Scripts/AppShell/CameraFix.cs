using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
    /// <summary>
    /// CameraFix v8 — Black-screen + flip fixes for Moto G84 5G (Android 15, Vulkan)
    ///
    /// Black screen root cause: cam.depth=-10 meant our OnPostRender GL drawing happened
    /// BEFORE other cameras (AR camera, depth=0) which then cleared the screen to black.
    /// Fix: cam.depth=100 → we render LAST, nothing can overwrite our camera feed.
    ///
    /// Camera flip root cause: On Android Vulkan, Unity performs a Y-flip at the
    /// swapchain level when presenting. This means GL.LoadOrtho Y=0 actually maps to
    /// the TOP of the physical screen (opposite of OpenGL ES convention).
    /// Additionally Graphics.Blit on Vulkan stores RT with V=0 at BOTTOM (not top),
    /// making graphicsUVStartsAtTop misleading for WebCamTexture → RenderTexture blits.
    /// Fix: invertV = !graphicsUVStartsAtTop (negate — Vulkan needs no UV flip here).
    ///
    /// Two-step render:
    ///   1. Update(): Graphics.Blit(webCamTexture → blitRT)  — converts OES → ARGB32
    ///   2. OnPostRender(): GL quads with correct rotation + corrected V UVs
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera        cam;
        private WebCamTexture webCamTexture;
        private RenderTexture blitRT;
        private Material      drawMat;
        private bool          cameraReady = false;
        private int           rotAngle    = 0;
        private bool          isMirrored  = false;
        private bool          invertV     = false;

        public WebCamTexture CameraTexture => webCamTexture;
        public bool          IsReady       => cameraReady;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags      = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.allowHDR        = false;
                cam.allowMSAA       = false;
                // depth=100: render LAST so AR/main cameras cannot overwrite our feed
                cam.depth           = 100;
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

            // Extended timeout: 30 s for slow devices
            float timeout = 30f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0) { Debug.LogError("[CameraFix] Camera timeout!"); yield break; }
                yield return null;
            }

            yield return new WaitForSeconds(0.3f);

            rotAngle   = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;

            // On Android Vulkan, Unity flips the swapchain output automatically.
            // Graphics.Blit(WebCamTexture→RT) on Vulkan stores V=0 at BOTTOM (same as
            // OpenGL ES) because the OES → RT blit is handled by the camera driver, not
            // Unity's Vulkan renderer.  Therefore we do NOT flip V on Vulkan.
            // invertV = true only on OpenGL ES where the RT V=0 is at top after blit.
            invertV = !SystemInfo.graphicsUVStartsAtTop;

            Debug.Log($"[CameraFix] v8 Ready: {webCamTexture.width}x{webCamTexture.height} " +
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

            // v0/v1 account for the RT V-axis convention after Graphics.Blit:
            // invertV=false (Vulkan, no flip): v0=0 (bottom), v1=1 (top)
            // invertV=true  (OpenGL ES, flip): v0=1 (top),    v1=0 (bottom)
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
