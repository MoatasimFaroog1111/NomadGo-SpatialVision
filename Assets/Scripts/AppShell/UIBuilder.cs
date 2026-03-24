using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    /// <summary>
    /// FIXED v3: Use OnGUI() for ALL buttons.
    /// UGUI Canvas (Image/Button with shaders) causes PINK rendering on many Android devices.
    /// OnGUI is immediate-mode, works on ALL Android hardware without shader compilation.
    /// Layout:
    ///   Top bar:   Status text
    ///   Bottom:    [Start Scan] or [Stop Scan]
    ///              [Export]  [Reports]
    ///   Reports panel shown over camera when active.
    /// </summary>
    public class UIBuilder : MonoBehaviour
    {
        private bool isScanning = false;
        private bool showReports = false;
        private string statusMessage = "NomadGo Ready — Press Start Scan";
        private string reportsContent = "No sessions recorded yet.\nStart a scan to create a report.";

        // OnGUI styles
        private GUIStyle btnStyle;
        private GUIStyle statusStyle;
        private GUIStyle panelStyle;
        private GUIStyle textStyle;
        private GUIStyle titleStyle;
        private bool stylesInit = false;

        // Safe area
        private Rect safeArea;
        private float btnHeight;
        private float btnMargin;
        private float statusHeight;

        private void Start()
        {
            safeArea = Screen.safeArea;
        }

        private void InitStyles()
        {
            if (stylesInit) return;

            float scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
            scale = Mathf.Clamp(scale, 1f, 3f);

            btnHeight   = 120f * scale;
            btnMargin   = 20f  * scale;
            statusHeight = 80f * scale;

            Texture2D MakeTex(Color c)
            {
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }

            btnStyle = new GUIStyle();
            btnStyle.fontSize = Mathf.RoundToInt(36 * scale);
            btnStyle.fontStyle = FontStyle.Bold;
            btnStyle.normal.textColor = Color.white;
            btnStyle.alignment = TextAnchor.MiddleCenter;
            btnStyle.normal.background    = MakeTex(new Color(0.08f, 0.63f, 0.08f, 0.92f));
            btnStyle.active.background    = MakeTex(new Color(0.04f, 0.45f, 0.04f, 0.95f));
            btnStyle.hover.background     = MakeTex(new Color(0.12f, 0.75f, 0.12f, 0.92f));

            statusStyle = new GUIStyle();
            statusStyle.fontSize = Mathf.RoundToInt(26 * scale);
            statusStyle.normal.textColor = Color.white;
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.normal.background = MakeTex(new Color(0, 0, 0, 0.65f));

            panelStyle = new GUIStyle();
            panelStyle.normal.background = MakeTex(new Color(0, 0.05f, 0.12f, 0.9f));
            panelStyle.padding = new RectOffset(16, 16, 16, 16);

            textStyle = new GUIStyle();
            textStyle.fontSize = Mathf.RoundToInt(22 * scale);
            textStyle.normal.textColor = Color.white;
            textStyle.alignment = TextAnchor.UpperLeft;
            textStyle.wordWrap = true;

            titleStyle = new GUIStyle();
            titleStyle.fontSize = Mathf.RoundToInt(30 * scale);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.cyan;
            titleStyle.alignment = TextAnchor.MiddleCenter;

            stylesInit = true;
        }

        private void OnGUI()
        {
            InitStyles();

            float W = Screen.width;
            float H = Screen.height;
            float m = btnMargin;

            // ── Status bar (top) ──────────────────────────────────────────────
            GUI.Box(new Rect(0, 0, W, statusHeight), GUIContent.none, statusStyle);
            GUI.Label(new Rect(0, 0, W, statusHeight), statusMessage, statusStyle);

            // ── Reports panel (overlay) ────────────────────────────────────────
            if (showReports)
            {
                DrawReportsPanel(W, H);
                return; // Don't show buttons under panel
            }

            // ── Buttons (bottom) ──────────────────────────────────────────────
            float bottomY = H - m - btnHeight;
            float halfW   = (W - 3 * m) / 2f;

            // Row 2 (above): Export | Reports
            float row2Y = bottomY - m - btnHeight;

            DrawButton(new Rect(m, row2Y, halfW, btnHeight),
                "Export", new Color(0.12f, 0.31f, 0.78f, 0.92f), OnExport);

            DrawButton(new Rect(2 * m + halfW, row2Y, halfW, btnHeight),
                "Reports", new Color(0.47f, 0.16f, 0.63f, 0.92f), OnToggleReports);

            // Row 1 (bottom): Start/Stop full-width
            if (!isScanning)
            {
                DrawButton(new Rect(m, bottomY, W - 2 * m, btnHeight),
                    "\u25B6  Start Scan", new Color(0.08f, 0.63f, 0.08f, 0.92f), OnStartScan);
            }
            else
            {
                DrawButton(new Rect(m, bottomY, W - 2 * m, btnHeight),
                    "\u25A0  Stop Scan", new Color(0.78f, 0.12f, 0.12f, 0.92f), OnStopScan);
            }
        }

        private void DrawButton(Rect rect, string label, Color color, System.Action onClick)
        {
            Texture2D normalTex = new Texture2D(1, 1);
            normalTex.SetPixel(0, 0, color);
            normalTex.Apply();
            btnStyle.normal.background = normalTex;

            Color hoverColor = new Color(
                Mathf.Min(1f, color.r + 0.12f),
                Mathf.Min(1f, color.g + 0.12f),
                Mathf.Min(1f, color.b + 0.12f),
                color.a);
            Texture2D hoverTex = new Texture2D(1, 1);
            hoverTex.SetPixel(0, 0, hoverColor);
            hoverTex.Apply();
            btnStyle.hover.background = hoverTex;
            btnStyle.active.background = hoverTex;

            if (GUI.Button(rect, label, btnStyle))
                onClick?.Invoke();
        }

        private void DrawReportsPanel(float W, float H)
        {
            float panelX = 20;
            float panelY = statusHeight + 10;
            float panelW = W - 40;
            float panelH = H - statusHeight - 20;

            GUI.Box(new Rect(panelX, panelY, panelW, panelH), GUIContent.none, panelStyle);

            // Title
            GUI.Label(new Rect(panelX, panelY + 10, panelW, 60), "SESSION REPORTS", titleStyle);

            // Content (scrollable area approximation)
            float contentY = panelY + 75;
            float contentH = panelH - 75 - btnHeight - 20;
            GUI.Label(new Rect(panelX + 10, contentY, panelW - 20, contentH), reportsContent, textStyle);

            // Buttons at bottom of panel
            float bY = panelY + panelH - btnHeight - 10;
            float bW = (panelW - 30) / 2f;

            DrawButton(new Rect(panelX + 10, bY, bW, btnHeight),
                "Refresh", new Color(0.12f, 0.47f, 0.71f, 0.92f), RefreshReports);

            DrawButton(new Rect(panelX + 20 + bW, bY, bW, btnHeight),
                "X  Close", new Color(0.71f, 0.24f, 0.24f, 0.92f), () => showReports = false);
        }

        // ── Callbacks ──────────────────────────────────────────────────────────

        private void OnStartScan()
        {
            isScanning = true;
            SetStatus("Scanning... (Camera active)");

            if (AppManager.Instance != null)
                AppManager.Instance.StartScan();
            else
            {
                var fp = FindObjectOfType<Vision.FrameProcessor>();
                if (fp != null) fp.StartProcessing();
            }
        }

        private void OnStopScan()
        {
            isScanning = false;
            SetStatus("Scan stopped. Check Reports for results.");

            if (AppManager.Instance != null)
                AppManager.Instance.StopScan();
            else
            {
                var fp = FindObjectOfType<Vision.FrameProcessor>();
                fp?.StopProcessing();
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
            showReports = !showReports;
            if (showReports) RefreshReports();
        }

        private void RefreshReports()
        {
            var storage = FindObjectOfType<Storage.SessionStorage>();
            if (storage == null)
            {
                reportsContent = "Storage system not available.";
                return;
            }

            string[] sessionIds = storage.GetAllSessionIds();
            if (sessionIds == null || sessionIds.Length == 0)
            {
                reportsContent = "No sessions recorded yet.\nStart a scan to create a report.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Total Sessions: {sessionIds.Length}\n");

            int start = Mathf.Max(0, sessionIds.Length - 5);
            for (int i = sessionIds.Length - 1; i >= start; i--)
            {
                var session = storage.LoadSession(sessionIds[i]);
                if (session != null)
                {
                    sb.AppendLine($"ID: {session.sessionId}");
                    sb.AppendLine($"  Start:   {session.startTime}");
                    if (!string.IsNullOrEmpty(session.endTime))
                        sb.AppendLine($"  End:     {session.endTime}");
                    sb.AppendLine($"  Items:   {session.totalItemsCounted}");
                    sb.AppendLine($"  Snapshots: {session.snapshots?.Count ?? 0}");
                    sb.AppendLine();
                }
            }

            if (storage.IsSessionActive && storage.CurrentSession != null)
            {
                var cur = storage.CurrentSession;
                sb.Insert(0, $"[ACTIVE SESSION: {cur.sessionId}]\n" +
                             $"  Items: {cur.totalItemsCounted}\n\n");
            }

            reportsContent = sb.ToString();
        }

        private void SetStatus(string msg) => statusMessage = msg;
    }
}
