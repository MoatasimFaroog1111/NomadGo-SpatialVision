using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// FIXED v4 — Root cause of PINK screen on Android identified:
    ///
    /// WebCamTexture on Android is backed by GL_TEXTURE_EXTERNAL_OES.
    /// Standard Unity UI shaders (UI/Default) use sampler2D, NOT samplerExternalOES.
    /// Sampling an OES texture with sampler2D = undefined → SOLID MAGENTA/PINK.
    ///
    /// Solution (two-step approach):
    ///   Step 1: Graphics.Blit(webCamTexture → tempRT)
    ///           Hidden/BlitCopy / Unlit/Texture correctly handle OES → regular RGB.
    ///   Step 2: GL quads with rotation UV mapping → displayRT
    ///           Apply rotation (portrait 90°) via UV coordinates, not transform.
    ///   Step 3: RawImage.texture = displayRT (regular ARGB32, no OES issues).
    ///
    /// Rotation fix: uvRect in GL.QUADS instead of RectTransform rotation (avoids clipping).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private RenderTexture displayRT;
        private RawImage cameraImage;
        private Canvas cameraCanvas;
        private bool cameraReady = false;
        private int rotAngle = 0;
        private bool isMirrored = false;
        private Material _blitMat;

        public WebCamTexture CameraTexture => webCamTexture;
        public bool IsReady => cameraReady;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.depth = -10;
            }

            // FIXED: Disable any ARCameraBackground that causes magenta via reflection
            DisableARCameraBackground();
        }

        private void DisableARCameraBackground()
        {
            // Use reflection to disable ARCameraBackground without requiring ARFoundation reference
            foreach (var comp in GetComponents<MonoBehaviour>())
            {
                if (comp != null && comp.GetType().Name == "ARCameraBackground")
                {
                    comp.enabled = false;
                    Debug.Log("[CameraFix] Disabled ARCameraBackground to prevent magenta background.");
                    break;
                }
            }

            // Also search globally
            var allComps = FindObjectsOfType<MonoBehaviour>();
            foreach (var comp in allComps)
            {
                if (comp != null && comp.GetType().Name == "ARCameraBackground")
                {
                    comp.enabled = false;
                    Debug.Log($"[CameraFix] Disabled ARCameraBackground on {comp.gameObject.name}");
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
            var canvasGO = new GameObject("CameraCanvas");
            DontDestroyOnLoad(canvasGO);

            cameraCanvas = canvasGO.AddComponent<Canvas>();
            cameraCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cameraCanvas.sortingOrder = -100;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var imgGO = new GameObject("CameraRawImage");
            imgGO.transform.SetParent(canvasGO.transform, false);

            var rt = imgGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            cameraImage = imgGO.AddComponent<RawImage>();
            cameraImage.color = Color.black;
            cameraImage.raycastTarget = false;
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
                Debug.Log($"[CameraFix] Device: {device.name} isFront={device.isFrontFacing}");
                if (!device.isFrontFacing) { camName = device.name; break; }
            }
            if (string.IsNullOrEmpty(camName) && WebCamTexture.devices.Length > 0)
                camName = WebCamTexture.devices[0].name;

            if (string.IsNullOrEmpty(camName))
            {
                Debug.LogError("[CameraFix] No camera found!");
                yield break;
            }

            webCamTexture = new WebCamTexture(camName, 1280, 720, 30);
            webCamTexture.Play();

            float t = 10f;
            while (webCamTexture.width <= 16)
            {
                t -= Time.deltaTime;
                if (t <= 0) { Debug.LogError("[CameraFix] Camera timeout!"); yield break; }
                yield return null;
            }

            yield return new WaitForSeconds(0.5f);

            rotAngle   = webCamTexture.videoRotationAngle;
            isMirrored = webCamTexture.videoVerticallyMirrored;
            Debug.Log($"[CameraFix] Ready: {webCamTexture.width}x{webCamTexture.height} rot={rotAngle} mirror={isMirrored}");

            // Create displayRT sized for the final display orientation
            // Portrait (rot=90/270): swap W and H so the RT fills the screen portrait
            int dW = (rotAngle == 90 || rotAngle == 270) ? webCamTexture.height : webCamTexture.width;
            int dH = (rotAngle == 90 || rotAngle == 270) ? webCamTexture.width  : webCamTexture.height;
            displayRT = new RenderTexture(dW, dH, 0, RenderTextureFormat.ARGB32);
            displayRT.Create();

            cameraImage.texture = displayRT;
            cameraImage.color = Color.white;
            cameraReady = true;
        }

        private void Update()
        {
            if (!cameraReady || webCamTexture == null || !webCamTexture.isPlaying) return;
            if (!webCamTexture.didUpdateThisFrame) return;
            BlitWithRotation();
        }

        /// <summary>
        /// FIXED: Two-step blit to handle OES texture + rotation.
        /// Step 1: Graphics.Blit(OES webCamTexture → tempRT) — converts OES to regular RGB.
        /// Step 2: GL quads with correct UVs → displayRT — applies rotation without clipping.
        /// </summary>
        private void BlitWithRotation()
        {
            // ── Step 1: OES → regular ARGB32 ─────────────────────────────────────
            var tempRT = RenderTexture.GetTemporary(
                webCamTexture.width, webCamTexture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(webCamTexture, tempRT);   // OES-safe blit

            // ── Step 2: rotate into displayRT via GL ──────────────────────────────
            var prevRT = RenderTexture.active;
            RenderTexture.active = displayRT;
            GL.Clear(false, true, Color.black);

            Material mat = GetBlitMaterial();
            mat.SetTexture("_MainTex", tempRT);
            mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);

            // UV mapping for each rotation + mirror combo.
            // Vertices: BL(0,0) BR(1,0) TR(1,1) TL(0,1) in GL.LoadOrtho space.
            // Android note: WebCamTexture V=0 is at bottom of sensor image, so
            // portrait 90° back-cam correct mapping (tested Moto G84 5G):
            if (!isMirrored)
            {
                switch (rotAngle)
                {
                    default:
                    case 0:
                        // Landscape, just flip V (Android Y-axis)
                        GL.TexCoord2(0, 1); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(1, 1); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(1, 0); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(0, 0); GL.Vertex3(0, 1, 0);
                        break;

                    case 90:
                        // Portrait — rotate 90° CW + V-flip
                        GL.TexCoord2(1, 1); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(0, 0); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                        break;

                    case 180:
                        GL.TexCoord2(1, 0); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(0, 0); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(0, 1); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(1, 1); GL.Vertex3(0, 1, 0);
                        break;

                    case 270:
                        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(0, 1); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(1, 0); GL.Vertex3(0, 1, 0);
                        break;
                }
            }
            else
            {
                switch (rotAngle)
                {
                    default:
                    case 0:
                        GL.TexCoord2(1, 1); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(0, 1); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(0, 0); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(1, 0); GL.Vertex3(0, 1, 0);
                        break;

                    case 90:
                        GL.TexCoord2(0, 1); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(0, 0); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(1, 0); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(1, 1); GL.Vertex3(0, 1, 0);
                        break;

                    case 180:
                        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                        break;

                    case 270:
                        GL.TexCoord2(1, 0); GL.Vertex3(0, 0, 0);
                        GL.TexCoord2(1, 1); GL.Vertex3(1, 0, 0);
                        GL.TexCoord2(0, 1); GL.Vertex3(1, 1, 0);
                        GL.TexCoord2(0, 0); GL.Vertex3(0, 1, 0);
                        break;
                }
            }

            GL.End();
            GL.PopMatrix();

            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(tempRT);
        }

        private Material GetBlitMaterial()
        {
            if (_blitMat != null) return _blitMat;

            // Use Unlit/Texture — simple, reliable, no lighting needed
            string[] candidates = { "Unlit/Texture", "Sprites/Default", "UI/Default" };
            foreach (var name in candidates)
            {
                var sh = Shader.Find(name);
                if (sh != null && sh.isSupported)
                {
                    _blitMat = new Material(sh);
                    Debug.Log($"[CameraFix] Blit shader: {name}");
                    return _blitMat;
                }
            }
            _blitMat = new Material(Shader.Find("Unlit/Texture"));
            return _blitMat;
        }

        private void OnDestroy()
        {
            cameraReady = false;
            if (webCamTexture != null) { webCamTexture.Stop(); webCamTexture = null; }
            if (displayRT != null) { displayRT.Release(); Destroy(displayRT); displayRT = null; }
            if (_blitMat != null) { Destroy(_blitMat); _blitMat = null; }
            if (cameraCanvas != null) Destroy(cameraCanvas.gameObject);
        }
    }
}
