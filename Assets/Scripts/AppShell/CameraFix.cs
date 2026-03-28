using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
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
        private string        diagText    = "CameraFix v11: starting...";

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
                cam.depth           = 100;
            }

            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "ARCameraBackground")
                {
                    mb.enabled = false;
                    Debug.Log($"[CameraFix] Disabled ARCameraBackground on {mb.gameObject.name}");
                }
            }

            foreach (var c in FindObjectsOfType<Camera>())
            {
                if (c == cam) continue;
                c.enabled = false;
                Debug.Log($"[CameraFix] Disabled camera: {c.gameObject.name}");
            }
        }

        private void Start() { StartCoroutine(StartCamera()); }

        private IEnumerator StartCamera()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                diagText = "v11: CAMERA PERMISSION DENIED";
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
                diagText = "v11: NO CAMERA FOUND";
                Debug.LogError("[CameraFix] No camera device found!");
                yield break;
            }

            diagText = $"v11: opening {camName}";
            Debug.Log($"[CameraFix] Opening: {camName}");
            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            float timeout = 30f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                diagText = $"v11: waiting cam w={webCamTexture.width} t={timeout:F0}s";
                if (timeout <= 0)
                {
                    diagText = "v11: CAM TIMEOUT";
                    Debug.LogError("[CameraFix] Camera timeout!");
                    yield break;
                }
                yield return null;
            }

            yield return new WaitForSeconds(2f);

            rotAngle   = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;
            invertV    = SystemInfo.graphicsUVStartsAtTop;

            diagText = $"v11 READY rot={rotAngle} mir={isMirrored} inv={invertV} api={SystemInfo.graphicsDeviceType}";
            Debug.Log($"[CameraFix] {diagText}");

            blitRT = new RenderTexture(webCamTexture.width, webCamTexture.height, 0, RenderTextureFormat.ARGB32);
            blitRT.Create();

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            drawMat = new Material(shader);

            cameraReady = true;
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize  = 20;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.yellow;
            int w = Screen.width;
            int h = Screen.height;
            GUI.Label(new Rect(10, h / 2 - 20, w - 20, 40), diagText, style);
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

            float v0 = invertV ? 1f : 0f;
            float v1 = invertV ? 0f : 1f;

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
            // the device reports 90°, which corrects the inversion.
            else if (rotAngle == 90)
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
            else if (rotAngle == 270)
            {
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
            else // 180
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
