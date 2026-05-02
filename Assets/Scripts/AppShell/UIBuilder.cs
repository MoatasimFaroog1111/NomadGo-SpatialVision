using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    public class UIBuilder : MonoBehaviour
    {
        private bool isScanning = false;
        private bool showReports = false;
        private string statusMessage = "NomadGo Ready — Press Start Scan";
        private string reportsContent = "No sessions recorded yet.\nStart a scan to create a report.";

        private bool modelDownloadInProgress = false;
        private float modelDownloadProgress = 0f;
        private bool modelUpdateAvailable = false;
        private bool modelJustDownloaded = false;
        private string modelIndicatorText = "";

        private Dictionary<string, int> detectedByLabel = new Dictionary<string, int>();
        private List<Vision.DetectionResult> latestDetections = new List<Vision.DetectionResult>();

        private GUIStyle btnStyle;
        private GUIStyle statusStyle;

        private float btnHeight;
        private float btnMargin;
        private float statusHeight;

        private void Start()
        {
            SubscribeModelDownloaderEvents();
            EnsureCatalogSystem();

            if (GetComponent<CatalogIndicatorUI>() == null)
            {
                gameObject.AddComponent<CatalogIndicatorUI>();
            }
        }

        private void EnsureCatalogSystem()
        {
            var existing = GameObject.Find("CatalogSystem");
            if (existing == null)
                existing = new GameObject("CatalogSystem");

            if (existing.GetComponent<global::ClientCatalogManager>() == null)
                existing.AddComponent<global::ClientCatalogManager>();

            if (existing.GetComponent<global::CatalogUploader>() == null)
                existing.AddComponent<global::CatalogUploader>();

            DontDestroyOnLoad(existing);
        }

        private void SubscribeModelDownloaderEvents()
        {
            var app = AppManager.Instance;
            if (app == null) return;

            var dl = app.ModelDownloader;
            if (dl == null) return;

            dl.OnProgress += (p) =>
            {
                modelDownloadInProgress = true;
                modelDownloadProgress = p;
                modelIndicatorText = $"Downloading {p * 100f:F0}%";
            };

            dl.OnComplete += (success) =>
            {
                modelDownloadInProgress = false;
                modelDownloadProgress = 0f;
                modelUpdateAvailable = false;
                modelIndicatorText = success ? "Model Ready" : "Download Failed";
            };
        }

        private void Update()
        {
            var fp = AppManager.Instance?.FrameProcessor;

            if (!isScanning)
                return;

            if (fp != null)
                latestDetections = fp.LatestDetections ?? new List<Vision.DetectionResult>();
        }

        private void InitStyles()
        {
            float H = Screen.height;

            btnHeight = H * 0.075f;
            btnMargin = H * 0.012f;
            statusHeight = H * 0.055f;

            btnStyle = new GUIStyle();
            btnStyle.fontSize = Mathf.RoundToInt(H * 0.025f);
            btnStyle.normal.textColor = Color.white;
            btnStyle.alignment = TextAnchor.MiddleCenter;

            statusStyle = new GUIStyle();
            statusStyle.fontSize = Mathf.RoundToInt(H * 0.02f);
            statusStyle.normal.textColor = Color.white;
            statusStyle.alignment = TextAnchor.MiddleCenter;
        }

        private void OnGUI()
        {
            InitStyles();

            float W = Screen.width;
            float H = Screen.height;
            float m = btnMargin;

            // 🔥 Status
            GUI.Box(new Rect(0, 0, W, statusHeight), "");
            GUI.Label(new Rect(0, 0, W, statusHeight), statusMessage, statusStyle);

            // 🔥 زر Upload
            float uploadY = H - (btnHeight * 3);

            DrawButton(
                new Rect(m, uploadY, W - 2 * m, btnHeight),
                "Upload Items File",
                new Color(0.1f, 0.45f, 0.85f),
                OnUploadCatalog
            );

            // 🔥 Start / Stop
            float scanY = H - btnHeight - m;

            if (!isScanning)
            {
                DrawButton(
                    new Rect(m, scanY, W - 2 * m, btnHeight),
                    "▶ Start Scan",
                    Color.green,
                    OnStartScan
                );
            }
            else
            {
                DrawButton(
                    new Rect(m, scanY, W - 2 * m, btnHeight),
                    "■ Stop Scan",
                    Color.red,
                    OnStopScan
                );
            }

            // 🔥 رسم النتائج
            if (isScanning)
                DrawDetections(W, H);
        }

        private void DrawDetections(float W, float H)
        {
            float labelH = Mathf.Max(70f, H * 0.075f);

            foreach (var det in latestDetections)
            {
                Rect b = det.boundingBox;

                float x = b.x * W;
                float y = b.y * H;
                float w = b.width * W;
                float h = b.height * H;

                GUI.Box(new Rect(x, y, w, h), "");

                string txt = $"{det.label} {det.confidence:P0}";

                GUI.Label(
                    new Rect(x, y - labelH, w, labelH),
                    txt
                );
            }
        }

        private void DrawButton(Rect rect, string text, Color color, System.Action action)
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = color;

            if (GUI.Button(rect, text, btnStyle))
                action?.Invoke();

            GUI.backgroundColor = old;
        }

        private void OnUploadCatalog()
        {
            var uploader = global::CatalogUploader.Instance ?? FindObjectOfType<global::CatalogUploader>();

            if (uploader == null)
            {
                statusMessage = "Uploader not found";
                return;
            }

            statusMessage = "Choose JSON file...";
            uploader.PickCatalogFile();
        }

        private void OnStartScan()
        {
            isScanning = true;
            statusMessage = "Scanning...";

            AppManager.Instance?.StartScan();
        }

        private void OnStopScan()
        {
            isScanning = false;
            statusMessage = "Stopped";

            AppManager.Instance?.StopScan();
        }
    }
}
