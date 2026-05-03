using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    public class UIBuilder : MonoBehaviour
    {
        public static UIBuilder Instance;

        private bool isScanning = false;
        private bool showReport = false;

        private string statusMessage = "Ready";
        private string catalogMessage = "Catalog: Not Loaded";
        private string reportText = "";

        private Vector2 reportScroll;

        private GUIStyle buttonStyle;
        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle successStyle;
        private GUIStyle errorStyle;
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

        private void Update()
        {
            var fp = AppManager.Instance != null ? AppManager.Instance.FrameProcessor : null;

            if (fp != null)
            {
                latestDetections = fp.LatestDetections ?? new List<Vision.DetectionResult>();

                if (isScanning)
                {
                    if (!fp.IsProcessing)
                        statusMessage = "Scan starting... waiting for camera/model";
                    else
                        statusMessage = "Scanning... detections: " + latestDetections.Count;
                }
            }
        }

        private void InitStyles()
        {
            int fsBig = Mathf.RoundToInt(Screen.height * 0.032f);
            int fsMed = Mathf.RoundToInt(Screen.height * 0.024f);
            int fsSmall = Mathf.RoundToInt(Screen.height * 0.020f);

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = fsBig;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.alignment = TextAnchor.MiddleCenter;

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = fsBig;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = Color.white;

            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = fsMed;
            statusStyle.fontStyle = FontStyle.Bold;
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.wordWrap = true;
            statusStyle.normal.textColor = Color.white;

            successStyle = new GUIStyle(statusStyle);
            successStyle.normal.textColor = Color.green;

            errorStyle = new GUIStyle(statusStyle);
            errorStyle.normal.textColor = Color.red;

            reportStyle = new GUIStyle(GUI.skin.label);
            reportStyle.fontSize = fsSmall;
            reportStyle.normal.textColor = Color.white;
            reportStyle.wordWrap = true;
        }

        private void OnGUI()
        {
            InitStyles();

            float W = Screen.width;
            float H = Screen.height;
            float m = 22f;

            DrawTopStatus(W, H);

            if (showReport)
                DrawReportPanel(W, H, m);

            DrawBottomPanel(W, H, m);

            if (isScanning)
                DrawDetections(W, H);
        }

        private void DrawTopStatus(float W, float H)
        {
            GUI.Box(new Rect(0, 0, W, H * 0.06f), "");
            GUI.Label(new Rect(0, 0, W, H * 0.06f), statusMessage, titleStyle);

            GUIStyle catStyle = catalogMessage.Contains("Loaded") ? successStyle : errorStyle;
            GUI.Label(new Rect(10, H * 0.06f, W - 20, H * 0.055f), catalogMessage, catStyle);
        }

        private void DrawBottomPanel(float W, float H, float m)
        {
            float panelH = H * 0.28f;
            float panelY = H - panelH - m;
            float btnH = H * 0.075f;

            GUI.Box(new Rect(m, panelY, W - (m * 2), panelH), "");

            GUI.Label(new Rect(m, panelY + 10, W - (m * 2), 50), "Client Products File", titleStyle);

            GUIStyle msgStyle = catalogMessage.Contains("Loaded") ? successStyle : errorStyle;
            GUI.Label(new Rect(m + 20, panelY + 65, W - (m * 2) - 40, 85), catalogMessage, msgStyle);

            float btnY = panelY + panelH - btnH - 20;
            float btnW = (W - (m * 2) - 40) / 3f;

            if (GUI.Button(new Rect(m, btnY, btnW, btnH), "Upload", buttonStyle))
                OnUpload();

            if (GUI.Button(new Rect(m + btnW + 20, btnY, btnW, btnH), "Report", buttonStyle))
                OnReport();

            if (GUI.Button(new Rect(m + ((btnW + 20) * 2), btnY, btnW, btnH), "↓ Export", buttonStyle))
                OnExport();

            float scanY = H - btnH - 8;

            if (!isScanning)
            {
                if (GUI.Button(new Rect(m, scanY, W - (m * 2), btnH), "▶ Start Scan", buttonStyle))
                    OnStartScan();
            }
            else
            {
                if (GUI.Button(new Rect(m, scanY, W - (m * 2), btnH), "■ Stop Scan", buttonStyle))
                    OnStopScan();
            }
        }

        private void DrawReportPanel(float W, float H, float m)
        {
            float panelY = H * 0.14f;
            float panelH = H * 0.48f;

            GUI.Box(new Rect(m, panelY, W - (m * 2), panelH), "");
            GUI.Label(new Rect(m, panelY + 10, W - (m * 2), 45), "Products Report", titleStyle);

            Rect viewRect = new Rect(m + 15, panelY + 65, W - (m * 2) - 30, panelH - 130);
            Rect contentRect = new Rect(0, 0, viewRect.width - 25, Mathf.Max(panelH, reportText.Length * 0.75f));

            reportScroll = GUI.BeginScrollView(viewRect, reportScroll, contentRect);
            GUI.Label(new Rect(0, 0, contentRect.width, contentRect.height), reportText, reportStyle);
            GUI.EndScrollView();

            if (GUI.Button(new Rect(m + 20, panelY + panelH - 55, W - (m * 2) - 40, 45), "Close Report", buttonStyle))
                showReport = false;
        }

        private void DrawDetections(float W, float H)
        {
            foreach (var det in latestDetections)
            {
                Rect b = det.boundingBox;

                float x = b.x * W;
                float y = b.y * H;
                float w = b.width * W;
                float h = b.height * H;

                GUI.Box(new Rect(x, y, w, h), "");

                string label = det.label + " " + Mathf.RoundToInt(det.confidence * 100f) + "%";

                var manager = global::ClientCatalogManager.Instance ?? FindObjectOfType<global::ClientCatalogManager>();
                if (manager != null && manager.IsLoaded)
                {
                    var item = manager.MatchByVisual(det.label);
                    if (item != null)
                        label = item.name + " | " + label;
                }

                GUI.Label(new Rect(x, Mathf.Max(0, y - 40), Mathf.Max(w, 180), 40), label, successStyle);
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
            SetCatalogUploadStatus(true, "Export completed successfully. Excel and PDF saved to Downloads.");
        }

        private void OnStartScan()
        {
            isScanning = true;
            showReport = false;
            statusMessage = "Starting scan...";

            if (AppManager.Instance == null)
            {
                statusMessage = "Scan failed: AppManager not found.";
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

            if (manager != null)
            {
                manager.Load();

                if (manager.IsLoaded)
                    catalogMessage = "Catalog: Loaded (" + manager.ItemsCount + " items)";
                else
                    catalogMessage = "Catalog: Not Loaded";
            }
            else
            {
                catalogMessage = "Catalog: Not Loaded";
            }
        }

        public void SetCatalogUploadStatus(bool? success, string text)
        {
            RefreshCatalogStatus();

            if (!string.IsNullOrEmpty(text))
                catalogMessage = text;

            if (success == true)
                statusMessage = "Client products operation completed";
            else if (success == false)
                statusMessage = "Client products operation failed";
            else
                statusMessage = text;
        }
    }
}
