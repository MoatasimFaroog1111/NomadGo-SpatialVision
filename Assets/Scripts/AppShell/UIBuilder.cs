using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Builds the entire UI at runtime — buttons, status text, etc.
    /// This avoids Unity Scene YAML complexity and ensures UI always works.
    /// </summary>
    public class UIBuilder : MonoBehaviour
    {
        private Canvas canvas;
        private Button startBtn;
        private Button stopBtn;
        private Button exportBtn;
        private TextMeshProUGUI statusTxt;
        private bool isScanning = false;

        private void Start()
        {
            BuildUI();
            // Auto-start scan after 2 seconds (camera should be ready by then)
            StartCoroutine(AutoStartScan());
        }

        private IEnumerator AutoStartScan()
        {
            yield return new WaitForSeconds(3f);
            if (!isScanning)
            {
                Debug.Log("[UIBuilder] Auto-starting scan...");
                OnStartScan();
            }
        }

        private void BuildUI()
        {
            // Create Canvas
            var canvasGO = new GameObject("UICanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Create EventSystem if not exists
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Status text at top
            statusTxt = CreateText(canvasGO.transform, "StatusText", "Ready - Tap Start Scan",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -50f), new Vector2(0f, 80f), 28);

            // Start Scan button (green, bottom center)
            startBtn = CreateButton(canvasGO.transform, "StartScanBtn", "▶ Start Scan",
                new Color(0.1f, 0.7f, 0.1f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 100f), new Vector2(420f, 110f));
            startBtn.onClick.AddListener(OnStartScan);

            // Stop Scan button (red, bottom center) - hidden initially
            stopBtn = CreateButton(canvasGO.transform, "StopScanBtn", "■ Stop Scan",
                new Color(0.8f, 0.1f, 0.1f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 100f), new Vector2(420f, 110f));
            stopBtn.onClick.AddListener(OnStopScan);
            stopBtn.gameObject.SetActive(false);

            // Export button (blue, bottom right)
            exportBtn = CreateButton(canvasGO.transform, "ExportBtn", "Export",
                new Color(0.1f, 0.3f, 0.8f),
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-110f, 100f), new Vector2(200f, 80f));
            exportBtn.onClick.AddListener(OnExport);

            Debug.Log("[UIBuilder] UI built successfully.");
        }

        private void OnStartScan()
        {
            if (AppManager.Instance != null)
            {
                AppManager.Instance.StartScan();
                Debug.Log("[UIBuilder] Scan started.");
            }
            else
            {
                Debug.LogWarning("[UIBuilder] AppManager not found, starting scan manually...");
                var fp = FindObjectOfType<Vision.FrameProcessor>();
                if (fp != null) fp.StartProcessing();
            }

            isScanning = true;
            startBtn.gameObject.SetActive(false);
            stopBtn.gameObject.SetActive(true);
            SetStatus("Scanning... (YOLO active)");
        }

        private void OnStopScan()
        {
            if (AppManager.Instance != null)
                AppManager.Instance.StopScan();
            else
            {
                var fp = FindObjectOfType<Vision.FrameProcessor>();
                if (fp != null) fp.StopProcessing();
            }

            isScanning = false;
            startBtn.gameObject.SetActive(true);
            stopBtn.gameObject.SetActive(false);
            SetStatus("Scan stopped.");
        }

        private void OnExport()
        {
            var storage = FindObjectOfType<Storage.SessionStorage>();
            if (storage != null)
            {
                string path = storage.ExportCurrentSession();
                SetStatus($"Exported: {path}");
            }
            else
            {
                SetStatus("No session to export.");
            }
        }

        private void SetStatus(string msg)
        {
            if (statusTxt != null)
                statusTxt.text = msg;
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private Button CreateButton(Transform parent, string name, string label, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = color;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = color;
            colors.highlightedColor = color * 1.2f;
            colors.pressedColor = color * 0.7f;
            btn.colors = colors;
            btn.targetGraphic = img;

            // Label
            var txtGO = new GameObject("Label");
            txtGO.transform.SetParent(go.transform, false);
            var txtRT = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 38;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;

            return btn;
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta, float fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            // Background
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.6f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return tmp;
        }
    }
}
