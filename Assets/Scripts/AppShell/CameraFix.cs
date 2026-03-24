using System.Collections;
using UnityEngine;

namespace NomadGo.AppShell
{
    /// <summary>
    /// CameraFix v8 — Root-cause fixes:
    ///
    /// BLACK SCREEN: ARCore was taking exclusive camera access when arSessionObject
    ///   was activated. Fixed in AppManager (arSessionObject never activated).
    ///   Additional guard: disable ALL AR components + other cameras in Awake().
    ///
    /// CAMERA FLIP: Added runtime UV mode selector (8 modes). User taps the
    ///   on-screen "UV" button to cycle until image appears correct. Mode saved
    ///   in PlayerPrefs so it persists across sessions.
    ///
    /// Modes for rotAngle=90 (portrait, back camera):
    ///   0 = original   1 = V-flip   2 = U-flip   3 = UV-flip (180°)
    ///   4 = swap UVs   5 = swap+Vf  6 = swap+Uf  7 = swap+UVf
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
        private int           uvMode      = 0; // 0-7, saved in PlayerPrefs

        public WebCamTexture CameraTexture => webCamTexture;
        public bool          IsReady       => cameraReady;

        private void Awake()
        {
            // Load saved UV preference
            uvMode = PlayerPrefs.GetInt("CameraUVMode", 0);

            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags    = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.allowHDR      = false;
                cam.allowMSAA     = false;
                cam.depth         = 100; // Render LAST
            }

            // Disable every AR component so ARCore cannot steal the camera device
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
                    Debug.Log($"[CameraFix] Disabled {t}");
                }
            }

            // Disable all cameras except this one
            foreach (var c in FindObjectsOfType<Camera>())
            {
                if (c == cam) continue;
                c.enabled = false;
            }
        }

        private void Start() => StartCoroutine(StartCamera());

        private IEnumerator StartCamera()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("[CameraFix] Camera permission denied!");
                yield break;
            }

            string camName = "";
            foreach (var d in WebCamTexture.devices)
            {
                Debug.Log($"[CameraFix] Device: {d.name} front={d.isFrontFacing}");
                if (!d.isFrontFacing) { camName = d.name; break; }
            }
            if (string.IsNullOrEmpty(camName) && WebCamTexture.devices.Length > 0)
                camName = WebCamTexture.devices[0].name;
            if (string.IsNullOrEmpty(camName))
            {
                Debug.LogError("[CameraFix] No camera device found!");
                yield break;
            }

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
                      $"rot={rotAngle} mirror={isMirrored} " +
                      $"uvStartsAtTop={SystemInfo.graphicsUVStartsAtTop} " +
                      $"api={SystemInfo.graphicsDeviceType}");

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
            // Blit every frame — don't rely on didUpdateThisFrame (unreliable on some Android)
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

            // Vertex order: BL(0,0) → BR(1,0) → TR(1,1) → TL(0,1)
            DrawCameraQuad(rotAngle, isMirrored);

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Compute UV for each screen corner, applying uvMode transform on top.
        /// Base UV table for (rotAngle, isMirrored) → 4 UVs [BL, BR, TR, TL].
        /// uvMode 0-7 applies the 8 possible flip/swap combinations.
        /// </summary>
        private void DrawCameraQuad(int rot, bool mir)
        {
            // Base UVs: [BL, BR, TR, TL] for each (rot, mirror) combination
            // (u, v) with v=0 at bottom (GL convention)
            Vector2 bl, br, tr, tl;

            if (rot == 0 || rot == 360)
            {
                if (mir) { bl=(1,1); br=(0,1); tr=(0,0); tl=(1,0); }
                else     { bl=(0,1); br=(1,1); tr=(1,0); tl=(0,0); }
            }
            else if (rot == 90)
            {
                if (mir) { bl=(0,0); br=(0,1); tr=(1,1); tl=(1,0); }
                else     { bl=(1,0); br=(1,1); tr=(0,1); tl=(0,0); }
            }
            else if (rot == 180)
            {
                if (mir) { bl=(0,1); br=(1,1); tr=(1,0); tl=(0,0); }
                else     { bl=(1,0); br=(0,0); tr=(0,1); tl=(1,1); }
            }
            else // 270
            {
                if (mir) { bl=(1,1); br=(1,0); tr=(0,0); tl=(0,1); }
                else     { bl=(0,1); br=(0,0); tr=(1,0); tl=(1,1); }
            }

            // Apply uvMode transform (0=none, 1=flipV, 2=flipU, 3=flipUV,
            //                         4=swapUV, 5=swapFlipV, 6=swapFlipU, 7=swapFlipUV)
            bl = ApplyUVMode(bl);
            br = ApplyUVMode(br);
            tr = ApplyUVMode(tr);
            tl = ApplyUVMode(tl);

            GL.TexCoord2(bl.x, bl.y); GL.Vertex3(0, 0, 0);
            GL.TexCoord2(br.x, br.y); GL.Vertex3(1, 0, 0);
            GL.TexCoord2(tr.x, tr.y); GL.Vertex3(1, 1, 0);
            GL.TexCoord2(tl.x, tl.y); GL.Vertex3(0, 1, 0);
        }

        private Vector2 ApplyUVMode(Vector2 uv)
        {
            float u = uv.x, v = uv.y;
            switch (uvMode)
            {
                case 0: return new Vector2(u,     v    ); // original
                case 1: return new Vector2(u,   1-v    ); // flip V
                case 2: return new Vector2(1-u,   v    ); // flip U
                case 3: return new Vector2(1-u, 1-v    ); // flip UV (180°)
                case 4: return new Vector2(v,     u    ); // swap UV
                case 5: return new Vector2(v,   1-u    ); // swap + flip V
                case 6: return new Vector2(1-v,   u    ); // swap + flip U
                case 7: return new Vector2(1-v, 1-u    ); // swap + flip UV
                default: return uv;
            }
        }

        private void OnGUI()
        {
            // Small diagnostic + UV cycle button — bottom-left corner
            float w = 180f, h = 36f, pad = 8f;
            float x = pad;
            float y = Screen.height - h - pad;

            // Info label
            if (cameraReady)
            {
                GUI.Box(new Rect(x, y - h - 4, w, h),
                    $"rot={rotAngle} mir={isMirrored} mode={uvMode}");
            }
            else
            {
                GUI.Box(new Rect(x, y - h - 4, w, h), "Camera starting...");
            }

            // Cycle button
            if (GUI.Button(new Rect(x, y, w, h), $"Flip/Rotate (Mode {uvMode}/7)"))
            {
                uvMode = (uvMode + 1) % 8;
                PlayerPrefs.SetInt("CameraUVMode", uvMode);
                PlayerPrefs.Save();
                Debug.Log($"[CameraFix] UV mode changed to {uvMode}");
            }
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
