using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    public class UIBuilder : MonoBehaviour
    {
        public static UIBuilder Instance { get; private set; }

        private bool isScanning = false;
        private string statusMessage = "NomadGo Ready — Press Start Scan";
        private string catalogUploadMessage = "Client products file: not uploaded";
        private Color catalogUploadColor = Color.yellow;

        private float btnHeight;
        private float btnMargin;
        private float statusHeight;
        private float panelHeight;

        private GUIStyle btnStyle;
        private GUIStyle statusStyle;
        private GUIStyle panelTitleStyle;
        private GUIStyle panelTextStyle;

        private List<Vision.DetectionResult> latestDetections = new List<Vision.DetectionResult>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            SubscribeModelDownloaderEvents();
            EnsureCatalogSystem();

            if (GetComponent<CatalogIndicatorUI>() == null)
                gameObject.AddComponent<CatalogIndicatorUI>();

            RefreshCatalogStatusFromManager();
        }

        private void EnsureCatalogSystem()
        {
            var existing = GameObject.Find("CatalogSystem");

            if (existing == null)
                existing = new GameObject("CatalogSystem");

            existing.name = "CatalogSystem";

            if (existing.GetComponent<global::ClientCatalogManager>() == null)
                existing.AddComponent<global::ClientCatalogManager>();

            if (existing.GetComponent<global::CatalogUploader>() == null)
                existing.AddComponent<global::CatalogUploader>();

            if (existing.GetComponent<global::CatalogReportExporter>() == null)
                existing.AddComponent<global::CatalogReportExporter>();

            DontDestroyOnLoad(existing);
        }

        private void SubscribeModelDownloaderEvents()
        {
            // Optional hook for future downloader events.
        }

        private void Update()
        {
            var fp = AppManager.Instance?.FrameProcessor;

            if (isScanning && fp != null)
                latestDetections = fp.LatestDetections ?? new List<Vision.DetectionResult>();
        }

        private void InitStyles()
        {
            float H = Screen.height;

            btnHeight = H * 0.07f;
            btnMargin = H * 0.012f;
            statusHeight = H * 0.055f;
            panelHeight = H * 0.205f;

            btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = Mathf.RoundToInt(H * 0.025f);
            btnStyle.alignment = TextAnchor.MiddleCenter;
            btnStyle.fontStyle = FontStyle.Bold;

            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = Mathf.RoundToInt(H * 0.021f);
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.normal.textColor = Color.white;
            statusStyle.fontStyle = FontStyle.Bold;

            panelTitleStyle = new GUIStyle(GUI.skin.label);
            panelTitleStyle.fontSize = Mathf.RoundToInt(H * 0.023f);
            panelTitleStyle.alignment = TextAnchor.UpperCenter;
            panelTitleStyle.normal.textColor = Color.white;
            panelTitleStyle.fontStyle = FontStyle.Bold;

            panelTextStyle = new GUIStyle(GUI.skin.label);
            panelTextStyle.fontSize = Mathf.RoundToInt(H * 0.019f);
            panelTextStyle.alignment = TextAnchor.MiddleCenter;
            panelTextStyle.fontStyle = FontStyle.Bold;
            panelTextStyle.wordWrap = true;
            panelTextStyle.normal.textColor = catalogUploadColor;
        }

        private void OnGUI()
        {
            InitStyles();

            float W = Screen.width;
            float H = Screen.height;
            float m = btnMargin;

            GUI.Box(new Rect(0, 0, W, statusHeight), "");
            GUI.Label(new Rect(0, 0, W, statusHeight), statusMessage, statusStyle);

            float panelY = H - (panelHeight + btnHeight + (m * 3));
            DrawCatalogPanel(W, panelY, m);

            float scanY = H - btnHeight - m;
            if (!isScanning)
            {
                if (GUI.Button(new Rect(m, scanY, W - 2 * m, btnHeight), "▶ Start Scan", btnStyle))
                    OnStartScan();
            }
            else
            {
                if (GUI.Button(new Rect(m, scanY, W - 2 * m, btnHeight), "■ Stop Scan", btnStyle))
                    OnStopScan();
            }

            if (isScanning)
                DrawDetections(W, H);
        }

        private void DrawCatalogPanel(float W, float y, float m)
        {
            float panelW = W - (2 * m);
            GUI.Box(new Rect(m, y, panelW, panelHeight), "");

            GUI.Label(new Rect(m, y + 4, panelW, btnHeight * 0.5f), "Client Products File", panelTitleStyle);

            float msgY = y + (btnHeight * 0.48f);
            GUI.Label(new Rect(m * 2, msgY, W - 4 * m, btnHeight * 0.85f), catalogUploadMessage, panelTextStyle);

            float rowY = y + panelHeight - btnHeight - m;
            float gap = m;
            float buttonW = (panelW - (gap * 2)) / 3f;

            if (GUI.Button(new Rect(m, rowY, buttonW, btnHeight), "📁 Upload", btnStyle))
                OnUploadCatalog();

            if (GUI.Button(new Rect(m + buttonW + gap, rowY, buttonW, btnHeight), "📊 Report", btnStyle))
                OnShowReport();

            if (GUI.Button(new Rect(m + ((buttonW + gap) * 2), rowY, buttonW, btnHeight), "⬇ Export", btnStyle))
                OnExportReport();
        }

        private void DrawDetections(float W, float H)
        {
            float labelH = Mathf.Max(70f, H * 0.08f);

            foreach (var det in latestDetections)
            {
                Rect b = det.boundingBox;

                float x = b.x * W;
                float y = b.y * H;
                float w = b.width * W;
                float h = b.height * H;

                GUI.Box(new Rect(x, y, w, h), "");
                string txt = $"{det.label} {det.confidence:P0}";
                GUI.Label(new Rect(x, y - labelH, w, labelH), txt);
            }
        }

        private void OnUploadCatalog()
        {
            var uploader = global::CatalogUploader.Instance ?? FindObjectOfType<global::CatalogUploader>();

            if (uploader == null)
            {
                SetCatalogUploadStatus(false, "Uploader not found inside mobile app");
                return;
            }

            statusMessage = "Choose client products file...";
            SetCatalogUploadStatus(null, "Waiting for file selection...");
            uploader.PickCatalogFile();
        }

        private void OnShowReport()
        {
            var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();

            if (manager == null || !manager.IsLoaded)
            {
                SetCatalogUploadStatus(false, "No report available. Upload the client products file first.");
                return;
            }

            SetCatalogUploadStatus(true, $"Report ready | Client: {manager.ClientName} | Products: {manager.ItemsCount}");
            statusMessage = "Products report is ready";
        }

        private void OnExportReport()
        {
            var exporter = global::CatalogReportExporter.Instance ?? FindObjectOfType<global::CatalogReportExporter>();

            if (exporter == null)
            {
                SetCatalogUploadStatus(false, "Export failed: exporter not found.");
                return;
            }

            SetCatalogUploadStatus(null, "Exporting Excel and PDF files...");
            var result = exporter.ExportExcelAndPdf();

            if (result.success)
            {
                SetCatalogUploadStatus(true, result.message);
                statusMessage = "Export successful";
            }
            else
            {
                SetCatalogUploadStatus(false, result.message);
            }
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

        private void RefreshCatalogStatusFromManager()
        {
            var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();

            if (manager != null && manager.IsLoaded)
                SetCatalogUploadStatus(true, $"Client products loaded successfully: {manager.ItemsCount} items");
            else
                SetCatalogUploadStatus(null, "Client products file: not uploaded");
        }

        public void SetCatalogUploadStatus(bool? success, string message)
        {
            catalogUploadMessage = message;

            if (success == true)
            {
                catalogUploadColor = new Color(0.25f, 1f, 0.25f);
                statusMessage = "Client products file uploaded successfully";
            }
            else if (success == false)
            {
                catalogUploadColor = new Color(1f, 0.3f, 0.3f);
                statusMessage = "Client products operation failed";
            }
            else
            {
                catalogUploadColor = Color.yellow;
            }
        }
    }
}
