using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    public class UIBuilder : MonoBehaviour
    {
        public static UIBuilder Instance;

        private bool isScanning;
        private bool showReport;

        private string statusMessage = "Ready";
        private string catalogMessage = "Catalog: Not Loaded";
        private string reportText = "";

        private Vector2 reportScroll;

        private GUIStyle buttonStyle;
        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle successStyle;
        private GUIStyle errorStyle;
        private GUIStyle detectionStyle;
        private GUIStyle reportStyle;

        private List<Vision.DetectionResult> latestDetections = new List<Vision.DetectionResult>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            EnsureCatalogSystem();
            RefreshCatalogStatus();
        }

        private void Update()
        {
            var fp = AppManager.Instance != null ? AppManager.Instance.FrameProcessor : null;

            if (fp != null)
            {
                latestDetections = fp.LatestDetections ?? new List<Vision.DetectionResult>();

                if (isScanning)
                    statusMessage = "Scanning... detections: " + latestDetections.Count;
            }
        }

        private void EnsureCatalogSystem()
        {
            GameObject catalogSystem = GameObject.Find("CatalogSystem");

            if (catalogSystem == null)
                catalogSystem = new GameObject("CatalogSystem");

            if (catalogSystem.GetComponent<global::ClientCatalogManager>() == null)
                catalogSystem.AddComponent<global::ClientCatalogManager>();

            if (catalogSystem.GetComponent<global::CatalogUploader>() == null)
                catalogSystem.AddComponent<global::CatalogUploader>();

            DontDestroyOnLoad(catalogSystem);
        }

        private void InitStyles()
        {
            int big = Mathf.RoundToInt(Screen.height * 0.03f);
            int med = Mathf.RoundToInt(Screen.height * 0.022f);
            int small = Mathf.RoundToInt(Screen.height * 0.018f);

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = big,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = big,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            titleStyle.normal.textColor = Color.white;

            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = med,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            statusStyle.normal.textColor = Color.white;

            successStyle = new GUIStyle(statusStyle);
            successStyle.normal.textColor = Color.green;

            errorStyle = new GUIStyle(statusStyle);
            errorStyle.normal.textColor = Color.red;

            detectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = med,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
            detectionStyle.normal.textColor = Color.green;

            reportStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = small,
                wordWrap = true
            };
            reportStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            InitStyles();

            float W = Screen.width;
            float H = Screen.height;
            float margin = 18f;

            DrawTopStatus(W, H);

            if (isScanning)
                DrawDetections(W, H);

            if (showReport && !isScanning)
                DrawReportPanel(W, H, margin);

            DrawControlPanel(W, H, margin);
        }

        private void DrawTopStatus(float W, float H)
        {
            GUI.Box(new Rect(0, 0, W, H * 0.065f), "");
            GUI.Label(new Rect(0, 0, W, H * 0.065f), statusMessage, titleStyle);

            GUIStyle catStyle = catalogMessage.Contains("Loaded") || catalogMessage.Contains("successful")
                ? successStyle
                : errorStyle;

            GUI.Label(new Rect(10, H * 0.07f, W - 20, H * 0.05f), catalogMessage, catStyle);
        }

        private void DrawControlPanel(float W, float H, float margin)
        {
            float panelH = H * 0.24f;
            float panelY = H - panelH - 12f;
            float btnH = H * 0.065f;

            GUI.Box(new Rect(margin, panelY, W - margin * 2, panelH), "");

            GUI.Label(new Rect(margin + 10, panelY + 8, W - margin * 2 - 20, 42), "Client Products File", titleStyle);

            GUIStyle msgStyle = catalogMessage.Contains("Loaded") || catalogMessage.Contains("successful")
                ? successStyle
                : errorStyle;

            GUI.Label(new Rect(margin + 20, panelY + 54, W - margin * 2 - 40, 55), catalogMessage, msgStyle);

            float btnY = panelY + panelH - btnH - 16f;
            float gap = 14f;
            float btnW = (W - margin * 2 - gap * 2) / 3f;

            GUI.enabled = !isScanning;

            if (GUI.Button(new Rect(margin, btnY, btnW, btnH), "Upload", buttonStyle))
                OnUpload();

            if (GUI.Button(new Rect(margin + btnW + gap, btnY, btnW, btnH), "Report", buttonStyle))
                OnReport();

            if (GUI.Button(new Rect(margin + (btnW + gap) * 2, btnY, btnW, btnH), "↓ Export", buttonStyle))
                OnExport();

            GUI.enabled = true;

            float scanBtnY = panelY - btnH - 10f;

            if (!isScanning)
            {
                if (GUI.Button(new Rect(margin, scanBtnY, W - margin * 2, btnH), "▶ Start Scan", buttonStyle))
                    OnStartScan();
            }
            else
            {
                if (GUI.Button(new Rect(margin, scanBtnY, W - margin * 2, btnH), "■ Stop Scan", buttonStyle))
                    OnStopScan();
            }
        }

        private void DrawReportPanel(float W, float H, float margin)
        {
            float panelY = H * 0.14f;
            float panelH = H * 0.48f;

            GUI.Box(new Rect(margin, panelY, W - margin * 2, panelH), "");
            GUI.Label(new Rect(margin, panelY + 8, W - margin * 2, 44), "Products Report", titleStyle);

            Rect viewRect = new Rect(margin + 14, panelY + 60, W - margin * 2 - 28, panelH - 116);
            Rect contentRect = new Rect(0, 0, viewRect.width - 24, Mathf.Max(panelH, reportText.Length * 0.65f));

            reportScroll = GUI.BeginScrollView(viewRect, reportScroll, contentRect);
            GUI.Label(new Rect(0, 0, contentRect.width, contentRect.height), reportText, reportStyle);
            GUI.EndScrollView();

            if (GUI.Button(new Rect(margin + 18, panelY + panelH - 50, W - margin * 2 - 36, 42), "Close Report", buttonStyle))
                showReport = false;
        }

        private void DrawDetections(float W, float H)
        {
            if (latestDetections == null || latestDetections.Count == 0)
                return;

            var catalog = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();

            foreach (var det in latestDetections)
            {
                Rect b = det.boundingBox;

                float x = Mathf.Clamp(b.x * W, 0, W - 10);
                float y = Mathf.Clamp(b.y * H, 0, H - 10);
                float w = Mathf.Clamp(b.width * W, 60, W);
                float h = Mathf.Clamp(b.height * H, 60, H);

                GUI.Box(new Rect(x, y, w, h), "");

                string detectedLabel = string.IsNullOrEmpty(det.label) ? "object" : det.label;
                string productLabel = detectedLabel;

                if (catalog != null && catalog.IsLoaded)
                    productLabel = catalog.BuildDetectionDisplayName(detectedLabel);

                string label =
                    productLabel +
                    "\nAI: " + detectedLabel +
                    " | " + Mathf.RoundToInt(det.confidence * 100f) + "%";

                GUI.Label(
                    new Rect(x, Mathf.Max(0, y - 62), Mathf.Max(w, 260), 62),
                    label,
                    detectionStyle
                );
            }
        }

        private void OnUpload()
        {
            showReport = false;
            statusMessage = "Choose client products file...";

            var uploader = global::CatalogUploader.Instance ?? FindObjectOfType<global::CatalogUploader>();

            if (uploader == null)
            {
                SetCatalogUploadStatus(false, "Uploader not found.");
                return;
            }

            uploader.PickCatalogFile();
        }

        private void OnReport()
        {
            RefreshCatalogStatus();

            var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();

            if (manager == null || !manager.IsLoaded)
            {
                reportText = "Catalog not loaded.\nPlease upload client products file first.";
                catalogMessage = "Catalog: Not Loaded";
            }
            else
            {
                reportText = manager.BuildReportText();
                catalogMessage = "Catalog: Loaded (" + manager.ItemsCount + " items)";
            }

            showReport = true;
            statusMessage = "Report opened";
        }

        private void OnExport()
        {
            var exporter = FindObjectOfType<global::ReportExporter>();

            if (exporter == null)
            {
                GameObject go = new GameObject("ReportExporter");
                exporter = go.AddComponent<global::ReportExporter>();
            }

            exporter.ExportProductsReport();
        }

        private void OnStartScan()
        {
            showReport = false;
            isScanning = true;
            statusMessage = "Starting scan...";

            if (AppManager.Instance == null)
            {
                statusMessage = "Scan failed: AppManager not found.";
                isScanning = false;
                return;
            }

            AppManager.Instance.StartScan();
        }

        private void OnStopScan()
        {
            isScanning = false;
            statusMessage = "Stopped";

            if (AppManager.Instance != null)
                AppManager.Instance.StopScan();
        }

        private void RefreshCatalogStatus()
        {
            var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();

            if (manager == null)
            {
                catalogMessage = "Catalog: Not Loaded";
                return;
            }

            manager.Load();

            catalogMessage = manager.IsLoaded
                ? "Catalog: Loaded (" + manager.ItemsCount + " items)"
                : "Catalog: Not Loaded";
        }

        public void SetCatalogUploadStatus(bool? success, string text)
        {
            RefreshCatalogStatus();

            if (!string.IsNullOrEmpty(text))
                catalogMessage = text;

            statusMessage = success == false
                ? "Client products operation failed"
                : "Client products operation completed";
        }
    }
}
