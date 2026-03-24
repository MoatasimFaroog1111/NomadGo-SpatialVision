using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NomadGo.AppShell
{
    /// <summary>
    /// FIXED: ScanUIController now works in two modes:
    /// 1. Editor-wired mode (SerializeField references assigned in Inspector)
    /// 2. Runtime auto-find mode (finds UI elements by name if not assigned)
    /// This fixes the bug where all SerializeField references were null.
    /// </summary>
    public class ScanUIController : MonoBehaviour
    {
        [Header("UI Elements (assign in Inspector OR leave null for auto-find)")]
        [SerializeField] private Button startScanButton;
        [SerializeField] private Button stopScanButton;
        [SerializeField] private Button exportSessionButton;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private GameObject scanPanel;
        [SerializeField] private GameObject resultsPanel;

        private bool isScanning = false;

        private void Start()
        {
            // FIXED: Auto-find UI elements if not assigned in Inspector
            if (startScanButton == null)
                startScanButton = FindButtonByName("StartScanBtn");
            if (stopScanButton == null)
                stopScanButton = FindButtonByName("StopScanBtn");
            if (exportSessionButton == null)
                exportSessionButton = FindButtonByName("ExportBtn");

            if (startScanButton != null)
                startScanButton.onClick.AddListener(OnStartScan);

            if (stopScanButton != null)
                stopScanButton.onClick.AddListener(OnStopScan);

            if (exportSessionButton != null)
                exportSessionButton.onClick.AddListener(OnExportSession);

            SetScanState(false);
        }

        private Button FindButtonByName(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) return go.GetComponent<Button>();
            return null;
        }

        private void OnStartScan()
        {
            if (AppManager.Instance == null)
            {
                Debug.LogError("[ScanUI] AppManager not found.");
                return;
            }

            AppManager.Instance.StartScan();
            SetScanState(true);
            UpdateStatus("Scanning...");
        }

        private void OnStopScan()
        {
            if (AppManager.Instance != null)
                AppManager.Instance.StopScan();

            SetScanState(false);
            UpdateStatus("Scan stopped.");
        }

        private void OnExportSession()
        {
            var storage = FindObjectOfType<Storage.SessionStorage>();
            if (storage != null)
            {
                string path = storage.ExportCurrentSession();
                if (!string.IsNullOrEmpty(path))
                    UpdateStatus($"Exported: {System.IO.Path.GetFileName(path)}");
                else
                    UpdateStatus("No active session to export.");
            }
        }

        private void SetScanState(bool scanning)
        {
            isScanning = scanning;

            if (startScanButton != null)
                startScanButton.gameObject.SetActive(!scanning);

            if (stopScanButton != null)
                stopScanButton.gameObject.SetActive(scanning);

            if (scanPanel != null)
                scanPanel.SetActive(scanning);

            if (resultsPanel != null)
                resultsPanel.SetActive(!scanning);
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            Debug.Log($"[ScanUI] {message}");
        }
    }
}
