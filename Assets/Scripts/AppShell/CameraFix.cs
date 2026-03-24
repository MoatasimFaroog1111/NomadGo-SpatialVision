using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
    /// <summary>
    /// RESTORED v5: Back to OnPostRender + GL approach (was working in first screenshot).
    ///
    /// Root cause of all PINK screen issues:
    /// - RawImage/Canvas uses UI/Default shader which CANNOT sample GL_TEXTURE_EXTERNAL_OES (Android WebCamTexture)
    /// - GL.DrawTexture in OnPostRender CAN handle OES texture because Unity sets up the correct GL state
    ///
    /// Two-step approach for reliability:
    ///   1. Graphics.Blit(webCamTexture → tempRT): converts OES → regular ARGB32 RenderTexture
    ///   2. OnPostRender + GL quads with tempRT: draws to screen with correct rotation UVs
    ///
    /// This eliminates the OES sampling issue completely while using the proven OnPostRender approach.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private RenderTexture blitRT;   // intermediate: OES → ARGB32
        private Material drawMat;
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
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.depth = -10;
            }

            // Disable ARCameraBackground (causes magenta if ARFoundation not set up)
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
            Debug.Log($"[CameraFix] Ready: {webCamTexture.width}x{webCamTexture.height} " +
                      $"rotAngle={rotAngle} mirror={isMirrored}");

            // Intermediate ARGB32 RT — same size as camera sensor output
            blitRT = new RenderTexture(webCamTexture.width, webCamTexture.height, 0, RenderTextureFormat.ARGB32);
            blitRT.Create();

            // Simple unlit material for GL drawing
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            drawMat = new Material(shader);

            cameraReady = true;
        }

        private void Update()
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying) return;
            if (!webCamTexture.didUpdateThisFrame) return;

            // STEP 1: Convert OES texture → regular ARGB32 via Graphics.Blit
            // This is safe to call in Update — it handles GL_TEXTURE_EXTERNAL_OES correctly
            Graphics.Blit(webCamTexture, blitRT);
        }

        // STEP 2: Draw blitRT to full screen using GL in OnPostRender
        // OnPostRender is the correct place for GL screen-space drawing
        private void OnPostRender()
        {
            if (!cameraReady || blitRT == null || drawMat == null) return;

            drawMat.mainTexture = blitRT;

            GL.PushMatrix();
            GL.LoadOrtho();

            drawMat.SetPass(0);
            GL.Begin(GL.QUADS);

            if (rotAngle == 0 || rotAngle == 360)
            {
                // Landscape or no rotation — flip V for Android Y-axis
                if (isMirrored)
                {
                    GL.TexCoord2(1, 1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 1, 0);
                }
            }
            else if (rotAngle == 90)
            {
                // Portrait mode — rotate 90° CW + V-flip for Android
                // Verified on Moto G84 5G (Android 15, back camera)
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
            else if (rotAngle == 180)
            {
                if (isMirrored)
                {
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 1, 0);
                }
                else
                {
                    GL.TexCoord2(1, 0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, 0); GL.Vertex3(1, 0, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(0, 1, 0);
                }
            }
            else // 270
            {
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
