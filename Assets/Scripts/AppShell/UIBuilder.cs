using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NomadGo.AppShell
{
    /// <summary>
    /// FIXED: Builds entire UI at runtime.
    /// Buttons: Start Scan, Stop Scan, Export, Reports.
    /// Added: Reports panel with session history.
    /// Fixed: Canvas sorting, EventSystem safety, button anchors.
    /// </summary>
    public class UIBuilder : MonoBehaviour
    {
        private Button startBtn;
        private Button stopBtn;
        private Button exportBtn;
        private Button reportsBtn;
        private TextMeshProUGUI statusTxt;
        private GameObject reportsPanel;
        private TextMeshProUGUI reportsTxt;
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
            // FIXED: Use FindObjectsOfType to avoid duplicate EventSystems
            var existingSystems = FindObjectsOfType<UnityEngine.EventSystems.EventSystem>();
            if (existingSystems == null || existingSystems.Length == 0)
            {
                var esGO = new GameObject("EventSystem");
                DontDestroyOnLoad(esGO);
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var root = canvasGO.transform;

            // ── Status bar ────────────────────────────────────────────────────
            statusTxt = MakeStatusBar(root);

            // ── Start Scan button ─────────────────────────────────────────────
            startBtn = MakeButton(root, "StartScanBtn", "\u25B6  Start Scan",
                new Color32(20, 160, 20, 220),
                new Vector2(0.05f, 0f), new Vector2(0.95f, 0f),
                new Vector2(0, 80), new Vector2(0, 110));
            startBtn.onClick.AddListener(OnStartScan);

            // ── Stop Scan button ──────────────────────────────────────────────
            stopBtn = MakeButton(root, "StopScanBtn", "\u25A0  Stop Scan",
                new Color32(200, 30, 30, 220),
                new Vector2(0.05f, 0f), new Vector2(0.95f, 0f),
                new Vector2(0, 80), new Vector2(0, 110));
            stopBtn.onClick.AddListener(OnStopScan);
            stopBtn.gameObject.SetActive(false);

            // ── Export button ─────────────────────────────────────────────────
            exportBtn = MakeButton(root, "ExportBtn", "Export",
                new Color32(30, 80, 200, 220),
                new Vector2(0.05f, 0f), new Vector2(0.48f, 0f),
                new Vector2(0, 210), new Vector2(0, 80));
            exportBtn.onClick.AddListener(OnExport);

            // ── Reports button ────────────────────────────────────────────────
            reportsBtn = MakeButton(root, "ReportsBtn", "Reports",
                new Color32(120, 40, 160, 220),
                new Vector2(0.52f, 0f), new Vector2(0.95f, 0f),
                new Vector2(0, 210), new Vector2(0, 80));
            reportsBtn.onClick.AddListener(OnToggleReports);

            // ── Reports Panel ─────────────────────────────────────────────────
            reportsPanel = BuildReportsPanel(root);
            reportsPanel.SetActive(false);

            Debug.Log("[UIBuilder] UI built successfully with Reports panel.");
        }

        private TextMeshProUGUI MakeStatusBar(Transform root)
        {
            var go = new GameObject("StatusBar");
            go.transform.SetParent(root, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -5);
            rt.sizeDelta = new Vector2(0, 50);

            // Fully transparent background - no pink bar!
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0f);
            bg.raycastTarget = false;

            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.text = "NomadGo Ready";
            txt.fontSize = 22;
            txt.color = new Color(1f, 1f, 1f, 0.9f);
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;

            return txt;
        }

        private GameObject BuildReportsPanel(Transform root)
        {
            var panelGO = new GameObject("ReportsPanel");
            panelGO.transform.SetParent(root, false);

            var rt = panelGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(20, 310);
            rt.offsetMax = new Vector2(-20, -80);

            var bg = panelGO.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.85f);

            // Title
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.anchoredPosition = Vector2.zero;
            titleRT.sizeDelta = new Vector2(0, 60);
            var titleTxt = titleGO.AddComponent<TextMeshProUGUI>();
            titleTxt.text = "SESSION REPORTS";
            titleTxt.fontSize = 32;
            titleTxt.color = Color.cyan;
            titleTxt.alignment = TextAlignmentOptions.Center;
            titleTxt.fontStyle = FontStyles.Bold;
            titleTxt.raycastTarget = false;

            // Content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(panelGO.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 0);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.offsetMin = new Vector2(10, 10);
            contentRT.offsetMax = new Vector2(-10, -65);

            reportsTxt = contentGO.AddComponent<TextMeshProUGUI>();
            reportsTxt.text = "No sessions recorded yet.\nStart a scan to create a report.";
            reportsTxt.fontSize = 22;
            reportsTxt.color = Color.white;
            reportsTxt.alignment = TextAlignmentOptions.TopLeft;
            reportsTxt.raycastTarget = false;

            // Close button
            var closeBtn = MakeButton(panelGO.transform, "CloseReportsBtn", "X Close",
                new Color32(180, 60, 60, 220),
                new Vector2(0.7f, 0f), new Vector2(1f, 0f),
                new Vector2(0, 5), new Vector2(0, 55));
            closeBtn.onClick.AddListener(() => reportsPanel.SetActive(false));

            // Refresh button
            var refreshBtn = MakeButton(panelGO.transform, "RefreshReportsBtn", "Refresh",
                new Color32(60, 120, 180, 220),
                new Vector2(0f, 0f), new Vector2(0.65f, 0f),
                new Vector2(0, 5), new Vector2(0, 55));
            refreshBtn.onClick.AddListener(RefreshReports);

            return panelGO;
        }

        // ── Button callbacks ─────────────────────────────────────────────────────

        private void OnStartScan()
        {
            isScanning = true;
            startBtn.gameObject.SetActive(false);
            stopBtn.gameObject.SetActive(true);
            reportsPanel.SetActive(false);
            SetStatus("Scanning... (Camera active)");

            if (AppManager.Instance != null)
                AppManager.Instance.StartScan();
            else
            {
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
            SetStatus("Scan stopped. Check Reports for results.");

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
                if (!string.IsNullOrEmpty(path))
                    SetStatus($"Exported: {System.IO.Path.GetFileName(path)}");
                else
                    SetStatus("No active session to export.");
            }
            else
            {
                SetStatus("Storage not available.");
            }
        }

        private void OnToggleReports()
        {
            bool show = !reportsPanel.activeSelf;
            reportsPanel.SetActive(show);
            if (show) RefreshReports();
        }

        private void RefreshReports()
        {
            var storage = FindObjectOfType<Storage.SessionStorage>();
            if (storage == null)
            {
                if (reportsTxt != null)
                    reportsTxt.text = "Storage system not available.";
                return;
            }

            string[] sessionIds = storage.GetAllSessionIds();

            if (sessionIds == null || sessionIds.Length == 0)
            {
                if (reportsTxt != null)
                    reportsTxt.text = "No sessions recorded yet.\nStart a scan to create a report.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Total Sessions: {sessionIds.Length}\n");

            // Show last 5 sessions
            int start = Mathf.Max(0, sessionIds.Length - 5);
            for (int i = sessionIds.Length - 1; i >= start; i--)
            {
                var session = storage.LoadSession(sessionIds[i]);
                if (session != null)
                {
                    sb.AppendLine($"Session: {session.sessionId}");
                    sb.AppendLine($"  Start: {session.startTime}");
                    if (!string.IsNullOrEmpty(session.endTime))
                        sb.AppendLine($"  End:   {session.endTime}");
                    sb.AppendLine($"  Items Counted: {session.totalItemsCounted}");
                    sb.AppendLine($"  Snapshots: {session.snapshots?.Count ?? 0}");
                    sb.AppendLine();
                }
            }

            // Also show current session if active
            if (storage.IsSessionActive && storage.CurrentSession != null)
            {
                var cur = storage.CurrentSession;
                sb.Insert(0, $"[CURRENT SESSION: {cur.sessionId}]\n" +
                             $"  Items: {cur.totalItemsCounted}, Snapshots: {cur.snapshots?.Count ?? 0}\n\n");
            }

            if (reportsTxt != null)
                reportsTxt.text = sb.ToString();
        }

        private void SetStatus(string msg)
        {
            if (statusTxt != null) statusTxt.text = msg;
        }

        // ── Helper: make a button ─────────────────────────────────────────────

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

            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = color;
            Color hc = color;
            hc.r = Mathf.Min(1f, color.r + 0.15f);
            hc.g = Mathf.Min(1f, color.g + 0.15f);
            hc.b = Mathf.Min(1f, color.b + 0.15f);
            cols.highlightedColor = hc;
            Color pc = color;
            pc.r *= 0.7f; pc.g *= 0.7f; pc.b *= 0.7f;
            cols.pressedColor = pc;
            btn.colors = cols;
            btn.targetGraphic = img;

            var lblGO = new GameObject("Label");
            lblGO.transform.SetParent(go.transform, false);
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;

            var tmp = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 36;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;

            return btn;
        }
    }
}
