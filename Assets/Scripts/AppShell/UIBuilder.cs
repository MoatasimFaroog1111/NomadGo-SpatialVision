using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Builds the entire UI at runtime.
    /// Buttons: Start Scan (green), Stop Scan (red), Export (blue).
    /// Status bar at top with semi-transparent background.
    /// </summary>
    public class UIBuilder : MonoBehaviour
    {
        private Button startBtn;
        private Button stopBtn;
        private Button exportBtn;
        private TextMeshProUGUI statusTxt;
        private bool isScanning = false;

        private void Start()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            // ── Canvas ──────────────────────────────────────────────────────────
            var canvasGO = new GameObject("UICanvas");
            DontDestroyOnLoad(canvasGO);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // ── EventSystem ──────────────────────────────────────────────────────
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var root = canvasGO.transform;

            // ── Status bar (top, semi-transparent dark background) ───────────────
            var statusGO = new GameObject("StatusBar");
            statusGO.transform.SetParent(root, false);
            var statusRT = statusGO.AddComponent<RectTransform>();
            statusRT.anchorMin = new Vector2(0, 1);
            statusRT.anchorMax = new Vector2(1, 1);
            statusRT.pivot = new Vector2(0.5f, 1f);
            statusRT.anchoredPosition = new Vector2(0, 0);
            statusRT.sizeDelta = new Vector2(0, 70);

            var statusBg = statusGO.AddComponent<Image>();
            statusBg.color = new Color(0, 0, 0, 0.55f);
            statusBg.raycastTarget = false;

            statusTxt = statusGO.AddComponent<TextMeshProUGUI>();
            statusTxt.text = "NomadGo Ready";
            statusTxt.fontSize = 26;
            statusTxt.color = Color.white;
            statusTxt.alignment = TextAlignmentOptions.Center;
            statusTxt.raycastTarget = false;

            // ── Start Scan button (bottom center, green) ─────────────────────────
            startBtn = MakeButton(root, "StartScanBtn", "▶  Start Scan",
                new Color32(20, 160, 20, 220),
                anchorMin: new Vector2(0.1f, 0f),
                anchorMax: new Vector2(0.9f, 0f),
                anchoredPos: new Vector2(0, 80),
                size: new Vector2(0, 110));
            startBtn.onClick.AddListener(OnStartScan);

            // ── Stop Scan button (same position, red) — hidden initially ─────────
            stopBtn = MakeButton(root, "StopScanBtn", "■  Stop Scan",
                new Color32(200, 30, 30, 220),
                anchorMin: new Vector2(0.1f, 0f),
                anchorMax: new Vector2(0.9f, 0f),
                anchoredPos: new Vector2(0, 80),
                size: new Vector2(0, 110));
            stopBtn.onClick.AddListener(OnStopScan);
            stopBtn.gameObject.SetActive(false);

            // ── Export button (bottom right, blue) ──────────────────────────────
            exportBtn = MakeButton(root, "ExportBtn", "Export",
                new Color32(30, 80, 200, 220),
                anchorMin: new Vector2(0.65f, 0f),
                anchorMax: new Vector2(0.95f, 0f),
                anchoredPos: new Vector2(0, 210),
                size: new Vector2(0, 80));
            exportBtn.onClick.AddListener(OnExport);

            Debug.Log("[UIBuilder] UI built successfully.");
        }

        // ── Button callbacks ─────────────────────────────────────────────────────

        private void OnStartScan()
        {
            isScanning = true;
            startBtn.gameObject.SetActive(false);
            stopBtn.gameObject.SetActive(true);
            SetStatus("Scanning... (YOLO active)");

            if (AppManager.Instance != null)
            {
                AppManager.Instance.StartScan();
            }
            else
            {
                // Fallback: find FrameProcessor directly
                var fp = FindObjectOfType<Vision.FrameProcessor>();
                if (fp != null) fp.StartProcessing();
                else Debug.LogWarning("[UIBuilder] FrameProcessor not found.");
            }
        }

        private void OnStopScan()
        {
            isScanning = false;
            startBtn.gameObject.SetActive(true);
            stopBtn.gameObject.SetActive(false);
            SetStatus("Scan stopped.");

            if (AppManager.Instance != null)
                AppManager.Instance.StopScan();
            else
            {
                var fp = FindObjectOfType<Vision.FrameProcessor>();
                if (fp != null) fp.StopProcessing();
            }
        }

        private void OnExport()
        {
            var storage = FindObjectOfType<Storage.SessionStorage>();
            if (storage != null)
            {
                string path = storage.ExportCurrentSession();
                SetStatus($"Exported: {System.IO.Path.GetFileName(path)}");
            }
            else
            {
                SetStatus("No session data to export.");
            }
        }

        private void SetStatus(string msg)
        {
            if (statusTxt != null) statusTxt.text = msg;
        }

        // ── Helper ───────────────────────────────────────────────────────────────

        private Button MakeButton(Transform parent, string name, string label, Color32 color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;

            // Rounded look via sprite (optional — works without)
            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = color;
            Color hc = color; hc.r = Mathf.Min(1f, color.r + 0.15f); hc.g = Mathf.Min(1f, color.g + 0.15f); hc.b = Mathf.Min(1f, color.b + 0.15f);
            cols.highlightedColor = hc;
            Color pc = color; pc.r *= 0.7f; pc.g *= 0.7f; pc.b *= 0.7f;
            cols.pressedColor = pc;
            btn.colors = cols;
            btn.targetGraphic = img;

            // Label
            var lblGO = new GameObject("Label");
            lblGO.transform.SetParent(go.transform, false);
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;

            var tmp = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 40;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;

            return btn;
        }
    }
}
