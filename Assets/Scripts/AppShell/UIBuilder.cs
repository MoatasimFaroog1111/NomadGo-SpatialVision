using System.Collections.Generic;
using UnityEngine;
using NomadGo.Vision;

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
        private List<DetectionResult> latestDetections = new List<DetectionResult>();

        private GUIStyle titleStyle, smallStyle, buttonStyle, greenStyle, redStyle, boxTextStyle, reportStyle;

        private void Awake() => Instance = this;

        private void Start()
        {
            EnsureCatalogSystem();
            RefreshCatalogStatus();
        }

        private void EnsureCatalogSystem()
        {
            GameObject go = GameObject.Find("CatalogSystem") ?? new GameObject("CatalogSystem");
            if (go.GetComponent<global::ClientCatalogManager>() == null) go.AddComponent<global::ClientCatalogManager>();
            if (go.GetComponent<global::CatalogUploader>() == null) go.AddComponent<global::CatalogUploader>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            var fp = AppManager.Instance != null ? AppManager.Instance.FrameProcessor : null;
            if (fp == null) return;

            latestDetections = fp.LatestDetections ?? new List<DetectionResult>();
            if (!isScanning) return;

            if (fp.IsEngineLoading) statusMessage = "Loading AI model...";
            else if (!fp.IsEngineReady) statusMessage = "AI model not ready";
            else if (!fp.IsProcessing) statusMessage = "Camera/model ready - starting...";
            else statusMessage = "Scanning... detections: " + latestDetections.Count;
        }

        private void InitStyles()
        {
            int big = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.030f), 28, 52);
            int med = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.021f), 20, 36);
            int small = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.017f), 16, 28);

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = big, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            titleStyle.normal.textColor = Color.white;

            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = small, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            smallStyle.normal.textColor = Color.white;

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = med, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            greenStyle = new GUIStyle(smallStyle); greenStyle.normal.textColor = Color.green;
            redStyle = new GUIStyle(smallStyle); redStyle.normal.textColor = Color.red;

            boxTextStyle = new GUIStyle(GUI.skin.label) { fontSize = small, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            boxTextStyle.normal.textColor = Color.white;

            reportStyle = new GUIStyle(GUI.skin.label) { fontSize = small, alignment = TextAnchor.UpperLeft, wordWrap = true };
            reportStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            InitStyles();
            float W = Screen.width, H = Screen.height;
            float margin = Mathf.Max(12, W * 0.025f);
            float topH = Mathf.Clamp(H * 0.075f, 70, 120);
            float panelH = Mathf.Clamp(H * 0.245f, 250, 360);
            float panelY = H - panelH - margin;

            DrawTopBar(W, topH);
            DrawDetections(W, H);
            if (showReport && !isScanning) DrawReportPanel(W, H, margin, topH, panelY);
            DrawBottomPanel(W, H, margin, panelY, panelH);
        }

        private void DrawTopBar(float W, float topH)
        {
            GUI.Box(new Rect(0, 0, W, topH), "");
            GUI.Label(new Rect(8, 0, W - 16, topH * 0.62f), statusMessage, titleStyle);
            GUI.Label(new Rect(8, topH * 0.58f, W - 16, topH * 0.42f), catalogMessage, catalogMessage.Contains("Loaded") ? greenStyle : redStyle);
        }

        private void DrawBottomPanel(float W, float H, float m, float y, float h)
        {
            GUI.Box(new Rect(m, y, W - 2 * m, h), "");
            GUI.Label(new Rect(m + 10, y + 8, W - 2 * m - 20, 48), "Client Products File", titleStyle);

            GUIStyle msgStyle = catalogMessage.Contains("Loaded") || catalogMessage.Contains("successful") ? greenStyle : redStyle;
            GUI.Label(new Rect(m + 18, y + 58, W - 2 * m - 36, 58), catalogMessage, msgStyle);

            float gap = 14;
            float btnH = Mathf.Clamp(H * 0.060f, 58, 84);
            float btnW = (W - 2 * m - 2 * gap) / 3f;
            float btnY = y + h - (btnH * 2) - 26;

            GUI.enabled = !isScanning;
            if (GUI.Button(new Rect(m, btnY, btnW, btnH), "Upload", buttonStyle)) OnUpload();
            if (GUI.Button(new Rect(m + btnW + gap, btnY, btnW, btnH), "Report", buttonStyle)) OnReport();
            if (GUI.Button(new Rect(m + (btnW + gap) * 2, btnY, btnW, btnH), "Export", buttonStyle)) OnExport();
            GUI.enabled = true;

            float scanY = btnY + btnH + 14;
            if (!isScanning)
            {
                if (GUI.Button(new Rect(m, scanY, W - 2 * m, btnH), "▶ Start Scan", buttonStyle)) OnStartScan();
            }
            else
            {
                if (GUI.Button(new Rect(m, scanY, W - 2 * m, btnH), "■ Stop Scan", buttonStyle)) OnStopScan();
            }
        }

        private void DrawReportPanel(float W, float H, float m, float topH, float bottomPanelY)
        {
            float y = topH + 12;
            float h = bottomPanelY - y - 12;
            if (h < 220) return;

            GUI.Box(new Rect(m, y, W - 2 * m, h), "");
            GUI.Label(new Rect(m + 8, y + 8, W - 2 * m - 16, 48), "Products Report", titleStyle);

            Rect view = new Rect(m + 18, y + 62, W - 2 * m - 36, h - 122);
            float contentH = Mathf.Max(view.height + 10, reportText.Length * 0.62f);
            reportScroll = GUI.BeginScrollView(view, reportScroll, new Rect(0, 0, view.width - 24, contentH));
            GUI.Label(new Rect(0, 0, view.width - 28, contentH), reportText, reportStyle);
            GUI.EndScrollView();

            if (GUI.Button(new Rect(m + 18, y + h - 52, W - 2 * m - 36, 42), "Close Report", buttonStyle)) showReport = false;
        }

        private void DrawDetections(float W, float H)
        {
            if (!isScanning || latestDetections == null || latestDetections.Count == 0) return;

            var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();
            foreach (var det in latestDetections)
            {
                Rect b = det.boundingBox;
                float x = b.x * W, y = b.y * H, w = b.width * W, h = b.height * H;
                GUI.Box(new Rect(x, y, w, h), "");

                string name = det.label;
                if (manager != null && manager.IsLoaded)
                {
                    var item = manager.MatchByVisual(det.label);
                    if (item != null) name = item.name + " | " + det.label;
                }
                string text = name + " " + Mathf.RoundToInt(det.confidence * 100f) + "%";
                GUI.Label(new Rect(x, Mathf.Max(0, y - 36), Mathf.Max(w, 220), 34), text, greenStyle);
            }
        }

        private void OnUpload()
        {
            showReport = false;
            statusMessage = "Choose client products file";
            var uploader = global::CatalogUploader.Instance ?? FindObjectOfType<global::CatalogUploader>();
            if (uploader == null) { SetCatalogUploadStatus(false, "Uploader not found"); return; }
            uploader.PickCatalogFile();
        }

        private void OnReport()
        {
            var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();
            if (manager == null) { reportText = "Catalog manager not found."; catalogMessage = "Catalog: Not Loaded"; }
            else { manager.Load(); reportText = manager.BuildReportText(); catalogMessage = manager.IsLoaded ? "Catalog: Loaded (" + manager.ItemsCount + " items)" : "Catalog: Not Loaded"; }
            showReport = true;
            statusMessage = "Report opened";
        }

        private void OnExport()
        {
            var exporter = FindObjectOfType<global::ReportExporter>();
            if (exporter == null) exporter = new GameObject("ReportExporter").AddComponent<global::ReportExporter>();
            exporter.ExportProductsReport();
        }

        private void OnStartScan()
        {
            showReport = false;
            isScanning = true;
            statusMessage = "Starting scan...";
            if (AppManager.Instance == null) { statusMessage = "Scan failed: AppManager not found"; return; }
            AppManager.Instance.StartScan();
        }

        private void OnStopScan()
        {
            isScanning = false;
            statusMessage = "Stopped";
            if (AppManager.Instance != null) AppManager.Instance.StopScan();
        }

        private void RefreshCatalogStatus()
        {
            var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();
            if (manager != null)
            {
                manager.Load();
                catalogMessage = manager.IsLoaded ? "Catalog: Loaded (" + manager.ItemsCount + " items)" : "Catalog: Not Loaded";
            }
            else catalogMessage = "Catalog: Not Loaded";
        }

        public void SetCatalogUploadStatus(bool? success, string text)
        {
            RefreshCatalogStatus();
            if (!string.IsNullOrEmpty(text)) catalogMessage = text;
            statusMessage = success == false ? "Client products operation failed" : "Client products operation completed";
        }
    }
}
