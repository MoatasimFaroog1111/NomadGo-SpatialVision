using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    public class UIBuilder : MonoBehaviour
    {
        private bool isScanning = false;
        private string statusMessage = "Ready";

        private float btnHeight;
        private float btnMargin;
        private float statusHeight;

        private List<Vision.DetectionResult> latestDetections = new List<Vision.DetectionResult>();

        private void Start()
        {
            EnsureCatalogSystem();

            if (GetComponent<CatalogIndicatorUI>() == null)
                gameObject.AddComponent<CatalogIndicatorUI>();
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

        private void Update()
        {
            var fp = AppManager.Instance?.FrameProcessor;

            if (isScanning && fp != null)
                latestDetections = fp.LatestDetections ?? new List<Vision.DetectionResult>();
        }

        private void OnGUI()
        {
            float W = Screen.width;
            float H = Screen.height;

            btnHeight = H * 0.08f;
            btnMargin = H * 0.02f;
            statusHeight = H * 0.06f;

            GUI.Box(new Rect(0, 0, W, statusHeight), statusMessage);

            float uploadY = H - (btnHeight * 3);

            if (GUI.Button(new Rect(btnMargin, uploadY, W - 2 * btnMargin, btnHeight), "Upload Items File"))
                OnUploadCatalog();

            float scanY = H - btnHeight - btnMargin;

            if (!isScanning)
            {
                if (GUI.Button(new Rect(btnMargin, scanY, W - 2 * btnMargin, btnHeight), "Start Scan"))
                    OnStartScan();
            }
            else
            {
                if (GUI.Button(new Rect(btnMargin, scanY, W - 2 * btnMargin, btnHeight), "Stop Scan"))
                    OnStopScan();
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

            statusMessage = "Select JSON...";
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
