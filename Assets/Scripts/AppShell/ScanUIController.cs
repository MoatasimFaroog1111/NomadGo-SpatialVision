using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NomadGo.AppShell
{
    public class ScanUIController : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Button startScanButton;
        [SerializeField] private Button stopScanButton;
        [SerializeField] private Button exportSessionButton;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private GameObject scanPanel;
        [SerializeField] private GameObject resultsPanel;

        private bool isScanning = false;

        private void Start()
        {
            if (startScanButton != null)
                startScanButton.onClick.AddListener(OnStartScan);

            if (stopScanButton != null)
                stopScanButton.onClick.AddListener(OnStopScan);

            if (exportSessionButton != null)
                exportSessionButton.onClick.AddListener(OnExportSession);

            SetScanState(false);
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
            if (AppManager.Instance == null) return;

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
                UpdateStatus($"Session exported: {path}");
                Debug.Log($"[ScanUI] Session exported to: {path}");
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
