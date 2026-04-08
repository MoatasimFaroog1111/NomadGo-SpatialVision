using System.Collections.Generic;
using UnityEngine;

namespace NomadGo.AppShell
{
    public class UIBuilder : MonoBehaviour
    {
        private bool isScanning = false;
        private bool showReports = false;
        private string statusMessage = "NomadGo Ready — Press Start Scan";
        private string reportsContent = "No sessions recorded yet.\nStart a scan to create a report.";

        // ---- Model download UI state ----
        private bool   modelDownloadInProgress = false;
        private float  modelDownloadProgress   = 0f;
        private bool   modelUpdateAvailable    = false;
        private bool   modelJustDownloaded     = false;
        private string modelIndicatorText      = "";
        // Cached style for the model indicator pill
        private GUIStyle modelPillStyle;
        private GUIStyle progressBarBgStyle;
        private GUIStyle progressBarFillStyle;
        private bool     modelStylesInit = false;

        // Live detection data
        private int detectedTotal = 0;
        private Dictionary<string, int> detectedByLabel = new Dictionary<string, int>();
        private List<Vision.DetectionResult> latestDetections = new List<Vision.DetectionResult>();

        private Texture2D boxTex;
        private Texture2D labelBgTex;
        private GUIStyle boxStyle;
        private GUIStyle boxLabelStyle;
        private bool boxStylesInit = false;

        private Dictionary<Color, Texture2D> texCache = new Dictionary<Color, Texture2D>();

        private Texture2D GetCachedTex(Color c)
        {
            if (!texCache.TryGetValue(c, out var t) || t == null)
            {
                t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                texCache[c] = t;
            }
            return t;
        }

        // OnGUI styles
        private GUIStyle btnStyle;
        private GUIStyle statusStyle;
        private GUIStyle panelStyle;
        private GUIStyle textStyle;
        private GUIStyle titleStyle;
        private bool stylesInit = false;

        private Rect safeArea;
        private float btnHeight;
        private float btnMargin;
        private float statusHeight;

        private void Start()
        {
            safeArea = Screen.safeArea;
            SubscribeModelDownloaderEvents();
        }

        private void SubscribeModelDownloaderEvents()
        {
            var app = AppManager.Instance;
            if (app == null) return;
            var dl = app.ModelDownloader;
            if (dl == null) return;

            // Refresh indicator now
            RefreshModelIndicator(dl);

            dl.OnProgress += (p) =>
            {
                modelDownloadInProgress = true;
                modelDownloadProgress   = p;
                modelIndicatorText      = $"Downloading model... {p * 100f:F0}%";
            };

            dl.OnComplete += (success) =>
            {
                modelDownloadInProgress = false;
                modelDownloadProgress   = 0f;
                modelUpdateAvailable    = false;

                if (success)
                {
                    modelJustDownloaded = true;
                    RefreshModelIndicator(dl);
                }
                else
                {
                    modelIndicatorText = "Download failed";
                }
            };

            dl.OnUpdateFound += () =>
            {
                modelUpdateAvailable = true;
                modelIndicatorText   = $"v{dl.CachedVersion} — Update: v{dl.PendingVersion}";
            };
        }

        private void RefreshModelIndicator(Vision.ModelDownloader dl)
        {
            if (dl == null) { modelIndicatorText = ""; return; }

            string ver = dl.HasCachedModel ? dl.CachedVersion : "";
            if (string.IsNullOrEmpty(ver))
            {
                var app = AppManager.Instance;
                ver = (app != null && app.Config != null) ? app.Config.model.model_version : "";
            }
            modelIndicatorText = string.IsNullOrEmpty(ver) ? "" : $"Model v{ver} ✓";
        }

        private void Update()
        {
            var app = AppManager.Instance;
            var fp  = app != null ? app.FrameProcessor : null;
            var cm  = app != null ? app.CountManager   : null;

            // Re-subscribe when AppManager becomes available after UIBuilder.Start
            var dl = app != null ? app.ModelDownloader : null;
            if (dl != null && string.IsNullOrEmpty(modelIndicatorText))
            {
                SubscribeModelDownloaderEvents();
            }
            // Keep indicator text live-synced while download is active
            if (dl != null && dl.IsDownloading)
            {
                modelDownloadInProgress = true;
                modelDownloadProgress   = dl.Progress;
                modelIndicatorText      = $"Downloading model... {dl.Progress * 100f:F0}%";
            }
            // Reflect live update-available state from downloader
            if (dl != null && dl.UpdateAvailable && !modelUpdateAvailable)
            {
                modelUpdateAvailable = true;
                modelIndicatorText   = $"v{dl.CachedVersion} — Update: v{dl.PendingVersion}";
            }

            // Show model loading state when NOT scanning yet
            if (!isScanning)
            {
                if (fp != null && fp.IsEngineLoading)
                    SetStatus("Loading AI model... please wait");
                else if (fp != null && fp.IsEngineReady && fp.IsInDemoMode)
                    SetStatus("AI DEMO MODE — model failed to load");
                else if (fp != null && fp.IsEngineReady)
                    SetStatus("NomadGo Ready — Press Start Scan");
                return;
            }

            if (fp != null)
                latestDetections = fp.LatestDetections ?? new List<Vision.DetectionResult>();

            if (cm != null)
            {
                detectedTotal   = cm.TotalCount;
                detectedByLabel = cm.CurrentCounts ?? new Dictionary<string, int>();

                if (detectedTotal > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"Items: {detectedTotal}  |  ");
                    foreach (var kv in detectedByLabel)
                        sb.Append($"{kv.Key}: {kv.Value}  ");
                    SetStatus(sb.ToString().TrimEnd());
                }
                else
                {
                    SetStatus("Scanning... point camera at items");
                }
            }
        }

        private void InitStyles()
        {
            if (stylesInit) return;

            // Size everything relative to screen height so it looks correct on any density.
            // DPI-based scaling over-sizes on high-PPI phones (e.g. 401-DPI Moto G84 → 2.5×).
            float H = Screen.height;
            btnHeight    = H * 0.075f;   // 7.5 % of screen
            btnMargin    = H * 0.012f;
            statusHeight = H * 0.055f;

            Texture2D MakeTex(Color c)
            {
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }

            btnStyle = new GUIStyle();
            btnStyle.fontSize = Mathf.RoundToInt(H * 0.022f);
            btnStyle.fontStyle = FontStyle.Bold;
            btnStyle.normal.textColor = Color.white;
            btnStyle.alignment = TextAnchor.MiddleCenter;
            btnStyle.normal.background = MakeTex(new Color(0.08f, 0.63f, 0.08f, 0.92f));
            btnStyle.active.background = MakeTex(new Color(0.04f, 0.45f, 0.04f, 0.95f));
            btnStyle.hover.background  = MakeTex(new Color(0.12f, 0.75f, 0.12f, 0.92f));

            statusStyle = new GUIStyle();
            statusStyle.fontSize = Mathf.RoundToInt(H * 0.016f);
            statusStyle.normal.textColor = Color.white;
            statusStyle.alignment = TextAnchor.MiddleCenter;
            statusStyle.wordWrap = true;
            statusStyle.normal.background = MakeTex(new Color(0, 0, 0, 0.70f));

            panelStyle = new GUIStyle();
            panelStyle.normal.background = MakeTex(new Color(0, 0.05f, 0.12f, 0.9f));
            panelStyle.padding = new RectOffset(16, 16, 16, 16);

            textStyle = new GUIStyle();
            textStyle.fontSize = Mathf.RoundToInt(H * 0.014f);
            textStyle.normal.textColor = Color.white;
            textStyle.alignment = TextAnchor.UpperLeft;
            textStyle.wordWrap = true;

            titleStyle = new GUIStyle();
            titleStyle.fontSize = Mathf.RoundToInt(H * 0.019f);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.cyan;
            titleStyle.alignment = TextAnchor.MiddleCenter;

            stylesInit = true;
            InitModelStyles();
        }

        private void InitModelStyles()
        {
            if (modelStylesInit) return;
            float H = Screen.height;

            modelPillStyle = new GUIStyle();
            modelPillStyle.fontSize = Mathf.RoundToInt(H * 0.013f);
            modelPillStyle.normal.textColor = new Color(0.6f, 1f, 0.6f, 1f);
            modelPillStyle.alignment = TextAnchor.MiddleRight;
            modelPillStyle.normal.background = GetCachedTex(new Color(0, 0, 0, 0.55f));
            modelPillStyle.padding = new RectOffset(4, 6, 0, 0);

            progressBarBgStyle = new GUIStyle();
            progressBarBgStyle.normal.background = GetCachedTex(new Color(0.1f, 0.1f, 0.1f, 0.85f));

            progressBarFillStyle = new GUIStyle();
            progressBarFillStyle.normal.background = GetCachedTex(new Color(0.08f, 0.63f, 0.08f, 0.92f));

            modelStylesInit = true;
        }

        private void InitBoxStyles()
        {
            if (boxStylesInit) return;

            boxTex = new Texture2D(1, 1);
            boxTex.SetPixel(0, 0, new Color(0f, 1f, 0.2f, 0.90f));
            boxTex.Apply();

            labelBgTex = new Texture2D(1, 1);
            labelBgTex.SetPixel(0, 0, new Color(0f, 0.6f, 0.1f, 0.85f));
            labelBgTex.Apply();

            boxStyle = new GUIStyle();
            boxStyle.normal.background = boxTex;

            boxLabelStyle = new GUIStyle();
            boxLabelStyle.normal.background  = labelBgTex;
            boxLabelStyle.normal.textColor   = Color.white;
            boxLabelStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.013f);
            boxLabelStyle.alignment = TextAnchor.MiddleLeft;
            boxLabelStyle.padding  = new RectOffset(4, 4, 2, 2);
            boxLabelStyle.wordWrap = false;

            boxStylesInit = true;
        }

        private void OnGUI()
        {
            InitStyles();

            float W = Screen.width;
            float H = Screen.height;
            float m = btnMargin;

            // Detection boxes (drawn behind status bar and buttons)
            if (isScanning && latestDetections != null && latestDetections.Count > 0)
                DrawDetectionBoxes(W, H);

            GUI.Box(new Rect(0, 0, W, statusHeight), GUIContent.none, statusStyle);
            GUI.Label(new Rect(0, 0, W, statusHeight), statusMessage, statusStyle);

            // Model indicator pill in top-right of status bar
            if (!string.IsNullOrEmpty(modelIndicatorText))
            {
                float pillW = W * 0.38f;
                float pillH = statusHeight;
                // Choose colour based on state
                Color pillTextColor = modelDownloadInProgress
                    ? Color.yellow
                    : (modelUpdateAvailable ? new Color(1f, 0.7f, 0.2f) : new Color(0.55f, 1f, 0.55f));
                modelPillStyle.normal.textColor = pillTextColor;
                GUI.Label(new Rect(W - pillW, 0, pillW, pillH), modelIndicatorText, modelPillStyle);
            }

            // Download progress bar (shown just below the status bar)
            if (modelDownloadInProgress)
            {
                float barH = Mathf.Max(8f, H * 0.012f);
                float barY = statusHeight;
                GUI.Box(new Rect(0, barY, W, barH), GUIContent.none, progressBarBgStyle);
                GUI.Box(new Rect(0, barY, W * Mathf.Clamp01(modelDownloadProgress), barH), GUIContent.none, progressBarFillStyle);
            }

            if (showReports)
            {
                DrawReportsPanel(W, H);
                return;
            }

            float bottomY = H - m - btnHeight;
            float halfW   = (W - 3 * m) / 2f;
            float row2Y   = bottomY - m - btnHeight;

            DrawButton(new Rect(m,          row2Y, halfW, btnHeight),
                "Export",   new Color(0.12f, 0.31f, 0.78f, 0.92f), OnExport);
            DrawButton(new Rect(2*m + halfW, row2Y, halfW, btnHeight),
                "Reports",  new Color(0.47f, 0.16f, 0.63f, 0.92f), OnToggleReports);

            // ---- Model download / update row (above Export/Reports row) ----
            if (modelJustDownloaded)
            {
                float reloadRowY = row2Y - m - btnHeight;
                DrawButton(new Rect(m, reloadRowY, W - 2*m, btnHeight),
                    "Model updated — tap to reload", new Color(0.08f, 0.45f, 0.63f, 0.92f), OnReloadModelNow);
            }
            else if (modelUpdateAvailable && !modelDownloadInProgress)
            {
                float updateRowY = row2Y - m - btnHeight;
                DrawButton(new Rect(m, updateRowY, W - 2*m, btnHeight),
                    $"Update Available ({(AppManager.Instance?.ModelDownloader?.PendingVersion ?? "")}) ↓ Download",
                    new Color(0.63f, 0.45f, 0.08f, 0.92f), OnDownloadUpdate);
            }

            if (!isScanning)
            {
                var fp = AppManager.Instance != null ? AppManager.Instance.FrameProcessor : null;
                bool engineReady = fp != null && fp.IsEngineReady;
                bool loading     = fp != null && fp.IsEngineLoading;

                if (modelDownloadInProgress)
                    DrawButton(new Rect(m, bottomY, W - 2*m, btnHeight),
                        $"Downloading model {modelDownloadProgress * 100f:F0}%", new Color(0.35f, 0.35f, 0.35f, 0.85f), null);
                else if (loading)
                    DrawButton(new Rect(m, bottomY, W - 2*m, btnHeight),
                        "Loading AI model...", new Color(0.35f, 0.35f, 0.35f, 0.85f), null);
                else if (!engineReady)
                    DrawButton(new Rect(m, bottomY, W - 2*m, btnHeight),
                        "Initializing...", new Color(0.35f, 0.35f, 0.35f, 0.85f), null);
                else
                    DrawButton(new Rect(m, bottomY, W - 2*m, btnHeight),
                        "\u25B6  Start Scan", new Color(0.08f, 0.63f, 0.08f, 0.92f), OnStartScan);
            }
            else
                DrawButton(new Rect(m, bottomY, W - 2*m, btnHeight),
                    "\u25A0  Stop Scan", new Color(0.78f, 0.12f, 0.12f, 0.92f), OnStopScan);
        }

        // ---- Model download / reload actions ----

        private void OnDownloadUpdate()
        {
            var dl = AppManager.Instance?.ModelDownloader;
            if (dl == null) return;
            dl.DownloadModel(
                p  => { modelDownloadProgress = p; },
                ok => {
                    if (ok) modelJustDownloaded = true;
                    modelUpdateAvailable = false;
                }
            );
        }

        private void OnReloadModelNow()
        {
            modelJustDownloaded = false;
            var dl = AppManager.Instance?.ModelDownloader;
            if (dl == null) return;

            var engine = FindObjectOfType<Vision.ONNXInferenceEngine>();
            if (engine != null)
            {
                engine.ReloadModel(dl.CachedModelPath, dl.CachedLabelsPath);
                RefreshModelIndicator(dl);
            }
            else
            {
                SetStatus("Model cached — will load on next launch.");
            }
        }

        private void DrawDetectionBoxes(float W, float H)
        {
            InitBoxStyles();

            float thick = Mathf.Max(3f, W * 0.004f);
            float labelH = Mathf.Max(28f, H * 0.025f);

            foreach (var det in latestDetections)
            {
                if (det == null) continue;

                Rect b = det.boundingBox; // normalized [0,1] in landscape detection frame

                // Transform landscape detection coords → portrait screen pixels
                // For Android portrait with videoRotationAngle=90 (back camera):
                //   landscape U → portrait Y (top→bottom = U=0→1)
                //   landscape V → portrait X (left→right = V=0→1)
                // Detection frame may be Y-flipped on Android RenderTexture,
                // so by = 1 - by_raw gives correct mapping.
                float sx = (1f - b.y - b.height) * W;
                float sy = b.x * H;
                float sw = b.height * W;
                float sh = b.width  * H;

                sx = Mathf.Max(0, sx);
                sy = Mathf.Max(0, sy);
                sw = Mathf.Max(40, sw);
                sh = Mathf.Max(40, sh);

                GUI.Box(new Rect(sx,           sy,           sw,     thick), GUIContent.none, boxStyle); // top
                GUI.Box(new Rect(sx,           sy+sh-thick,  sw,     thick), GUIContent.none, boxStyle); // bottom
                GUI.Box(new Rect(sx,           sy,           thick,  sh),    GUIContent.none, boxStyle); // left
                GUI.Box(new Rect(sx+sw-thick,  sy,           thick,  sh),    GUIContent.none, boxStyle); // right

                // Label
                string lbl = $" {det.label} {det.confidence:P0}";
                GUI.Label(new Rect(sx, sy - labelH, Mathf.Min(sw, W * 0.55f), labelH), lbl, boxLabelStyle);
            }
        }

        private void DrawButton(Rect rect, string label, Color color, System.Action onClick)
        {
            btnStyle.normal.background = GetCachedTex(color);

            Color hc = new Color(
                Mathf.Min(1f, color.r + 0.12f),
                Mathf.Min(1f, color.g + 0.12f),
                Mathf.Min(1f, color.b + 0.12f), color.a);
            var hTex = GetCachedTex(hc);
            btnStyle.hover.background  = hTex;
            btnStyle.active.background = hTex;

            if (GUI.Button(rect, label, btnStyle))
                onClick?.Invoke();
        }

        private void DrawReportsPanel(float W, float H)
        {
            float px = 20, py = statusHeight + 10;
            float pw = W - 40, ph = H - statusHeight - 20;

            GUI.Box(new Rect(px, py, pw, ph), GUIContent.none, panelStyle);
            GUI.Label(new Rect(px, py + 10, pw, 60), "SESSION REPORTS", titleStyle);

            float cy = py + 75;
            float ch = ph - 75 - btnHeight - 20;
            GUI.Label(new Rect(px + 10, cy, pw - 20, ch), reportsContent, textStyle);

            float bY = py + ph - btnHeight - 10;
            float bW = (pw - 30) / 2f;
            DrawButton(new Rect(px + 10,      bY, bW, btnHeight),
                "Refresh", new Color(0.12f, 0.47f, 0.71f, 0.92f), RefreshReports);
            DrawButton(new Rect(px + 20 + bW, bY, bW, btnHeight),
                "X  Close", new Color(0.71f, 0.24f, 0.24f, 0.92f), () => showReports = false);
        }

        private void OnStartScan()
        {
            isScanning = true;
            detectedTotal = 0;
            detectedByLabel.Clear();
            latestDetections.Clear();
            SetStatus("Scanning... searching for items");

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
            string summary = detectedTotal > 0
                ? $"Scan done. {detectedTotal} items found. See Reports."
                : "Scan stopped. Check Reports for results.";
            SetStatus(summary);
            latestDetections.Clear();

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
                SetStatus(!string.IsNullOrEmpty(path)
                    ? $"Exported: {System.IO.Path.GetFileName(path)}"
                    : "No active session to export.");
            }
            else { SetStatus("Storage not available."); }
        }

        private void OnToggleReports()
        {
            showReports = !showReports;
            if (showReports) RefreshReports();
        }

        private void RefreshReports()
        {
            var storage = FindObjectOfType<Storage.SessionStorage>();
            if (storage == null) { reportsContent = "Storage system not available."; return; }

            string[] ids = storage.GetAllSessionIds();
            if (ids == null || ids.Length == 0)
            {
                reportsContent = "No sessions recorded yet.\nStart a scan to create a report.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Total Sessions: {ids.Length}\n");

            int start = Mathf.Max(0, ids.Length - 5);
            for (int i = ids.Length - 1; i >= start; i--)
            {
                var session = storage.LoadSession(ids[i]);
                if (session != null)
                {
                    sb.AppendLine($"ID: {session.sessionId}");
                    sb.AppendLine($"  Start:     {session.startTime}");
                    if (!string.IsNullOrEmpty(session.endTime))
                        sb.AppendLine($"  End:       {session.endTime}");
                    sb.AppendLine($"  Items:     {session.totalItemsCounted}");
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
