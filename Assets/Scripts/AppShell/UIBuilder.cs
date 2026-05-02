using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    public class UIBuilder : MonoBehaviour
    {
        private bool isScanning = false;
        private string statusMessage = "NomadGo Ready — Press Start Scan";

        private float btnHeight;
        private float btnMargin;
        private float statusHeight;

        private GUIStyle btnStyle;
        private GUIStyle statusStyle;

        private List<Vision.DetectionResult> latestDetections = new List<Vision.DetectionResult>();

        private void Start()
        {
            SubscribeModelDownloaderEvents();
            EnsureCatalogSystem();

            // 🔥 مهم: إضافة مؤشر الكتالوج
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

            existing.name = "CatalogSystem";

            if (existing.GetComponent<global::ClientCatalogManager>() == null)
                existing.AddComponent<global::ClientCatalogManager>();

            if (existing.GetComponent<global::CatalogUploader>() == null)
                existing.AddComponent<global::CatalogUploader>();

            DontDestroyOnLoad(existing);
        }

        private void SubscribeModelDownloaderEvents()
        {
            // Optional — ما راح يكسر شيء لو فاضي
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

            btnHeight = H * 0.08f;
            btnMargin = H * 0.015f;
            statusHeight = H * 0.06f;

            btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = Mathf.RoundToInt(H * 0.03f);
            btnStyle.alignment = TextAnchor.MiddleCenter;

            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = Mathf.RoundToInt(H * 0.022f);
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            InitStyles();

            float W = Screen.width;
            float H = Screen.height;
            float m = btnMargin;

            // 🔝 Status
            GUI.Box(new Rect(0, 0, W, statusHeight), "");
            GUI.Label(new Rect(0, 0, W, statusHeight), statusMessage, statusStyle);

            // 🔼 Upload Button
            float uploadY = H - (btnHeight * 3);

            if (GUI.Button(new Rect(m, uploadY, W - 2 * m, btnHeight), "Upload Items File", btnStyle))
                OnUploadCatalog();

            // ▶ Start / Stop
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

            // 🎯 Draw detections
            if (isScanning)
                DrawDetections(W, H);
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
