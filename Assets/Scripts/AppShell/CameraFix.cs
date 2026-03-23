using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Camera display for Android using Graphics.Blit to fix YUV color issue.
    /// Renders the back camera correctly on Moto G84 5G.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private WebCamTexture webCamTexture;
        private RenderTexture renderTex;
        private RawImage bgImage;
        private GameObject bgCanvas;
        private Material blitMat;

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
                Debug.Log($"[CameraFix] Device: {device.name}, front: {device.isFrontFacing}");
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
                Debug.LogError("[CameraFix] No camera found!");
                yield break;
            }

            Debug.Log($"[CameraFix] Starting camera: {camName}");
            webCamTexture = new WebCamTexture(camName, 1920, 1080, 30);
            webCamTexture.Play();

            // Wait for camera to produce valid frames
            float timeout = 8f;
            while (webCamTexture.width <= 16)
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0)
                {
                    Debug.LogError("[CameraFix] Camera timed out!");
                    yield break;
                }
                yield return null;
            }

            // Extra wait for first real frame
            yield return new WaitForSeconds(0.5f);

            Debug.Log($"[CameraFix] Camera OK: {webCamTexture.width}x{webCamTexture.height}, rot={webCamTexture.videoRotationAngle}, mirror={webCamTexture.videoVerticallyMirrored}");

            // Create RenderTexture to blit into (fixes YUV format)
            renderTex = new RenderTexture(webCamTexture.width, webCamTexture.height, 0, RenderTextureFormat.ARGB32);
            renderTex.Create();

            // Create blit material
            blitMat = new Material(Shader.Find("NomadGo/CameraYUV"));
            if (blitMat == null || blitMat.shader == null || !blitMat.shader.isSupported)
            {
                Debug.LogWarning("[CameraFix] Custom shader not found, using Unlit/Texture");
                blitMat = new Material(Shader.Find("Unlit/Texture"));
            }

            // Blit WebCamTexture → RenderTexture (converts YUV to RGB)
            bool mirrored = webCamTexture.videoVerticallyMirrored;
            blitMat.SetFloat("_FlipY", mirrored ? 1f : 0f);
            Graphics.Blit(webCamTexture, renderTex, blitMat);

            CreateBackground();
        }

        private void CreateBackground()
        {
            bgCanvas = new GameObject("CamBG_Canvas");
            DontDestroyOnLoad(bgCanvas);

            var canvas = bgCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -200;

            var scaler = bgCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            bgCanvas.AddComponent<GraphicRaycaster>();

            var imgGO = new GameObject("CamBG_Image");
            imgGO.transform.SetParent(bgCanvas.transform, false);

            bgImage = imgGO.AddComponent<RawImage>();
            bgImage.texture = renderTex;  // Use RenderTexture (RGB, not YUV)
            bgImage.color = Color.white;

            var rect = imgGO.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localEulerAngles = Vector3.zero;

            // Apply rotation
            int rotation = webCamTexture.videoRotationAngle;
            rect.localEulerAngles = new Vector3(0, 0, -rotation);

            // Fix scale for rotated image
            if (rotation == 90 || rotation == 270)
            {
                float sw = Screen.width;
                float sh = Screen.height;
                float camW = webCamTexture.width;
                float camH = webCamTexture.height;
                float scale = Mathf.Max(sw / camH, sh / camW);
                rect.localScale = new Vector3(scale, scale, 1f);
            }

            Debug.Log("[CameraFix] Background created with RenderTexture!");
        }

        private void Update()
        {
            // Keep blitting every frame to update the display
            if (webCamTexture != null && webCamTexture.didUpdateThisFrame && renderTex != null && blitMat != null)
            {
                blitMat.SetFloat("_FlipY", webCamTexture.videoVerticallyMirrored ? 1f : 0f);
                Graphics.Blit(webCamTexture, renderTex, blitMat);
            }

            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }

        private void OnDestroy()
        {
            if (webCamTexture != null)
            {
                webCamTexture.Stop();
                webCamTexture = null;
            }
            if (renderTex != null)
            {
                renderTex.Release();
                renderTex = null;
            }
            if (blitMat != null)
            {
                Destroy(blitMat);
                blitMat = null;
            }
            if (bgCanvas != null)
            {
                Destroy(bgCanvas);
            }
        }
    }
}
