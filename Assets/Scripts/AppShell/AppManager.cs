using UnityEngine;

namespace NomadGo.AppShell
{
    public class AppManager : MonoBehaviour
    {
        public static AppManager Instance { get; private set; }

        [Header("References (all optional)")]
        [SerializeField] private GameObject arSessionObject;        [SerializeField] private Camera arCamera;

        private AppConfig appConfig;
        private bool isInitialized = false;

        public AppConfig Config => appConfig;
        public Camera ARCamera => arCamera;
        public bool IsInitialized => isInitialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadConfiguration();
        }

        private void Start()
        {
            InitializeSubsystems();
        }

        private void LoadConfiguration()
        {
            TextAsset configText = Resources.Load<TextAsset>("CONFIG");
            if (configText == null)
            {
                Debug.LogError("[AppManager] CONFIG.json not found in Resources folder.");
                appConfig = CreateDefaultConfig();
                return;
            }

            try
            {
                appConfig = JsonUtility.FromJson<AppConfig>(configText.text);
                Debug.Log($"[AppManager] Config loaded: {appConfig.app.name} v{appConfig.app.version}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AppManager] Config parse error: {ex.Message}. Using defaults.");
                appConfig = CreateDefaultConfig();
            }
        }

        private AppConfig CreateDefaultConfig()
        {
            return new AppConfig
            {
                app = new AppInfo { name = "NomadGo", version = "1.0.0", build = 1 },
                model = new ModelConfig
                {
                    path = "Models/yolov8n.onnx",
                    labels_path = "Models/labels.txt",
                    input_width = 640,
                    input_height = 640,
                    confidence_threshold = 0.45f,
                    nms_threshold = 0.5f,
                    max_detections = 100
                },
                counting = new CountingConfig
                {
                    row_cluster_vertical_gap = 50f,
                    row_limit = 6,
                    iou_threshold = 0.4f,
                    tracking_max_age_frames = 15,
                    min_detection_confidence = 0.45f
                },
                spatial = new SpatialConfig
                {
                    enable_depth_refinement = false,
                    plane_detection_mode = "Horizontal",
                    max_plane_count = 10
                },
                sync = new SyncConfig
                {
                    base_url = "http://localhost:5000/api/pulse",
                    pulse_interval_seconds = 5f,
                    retry_max_attempts = 5,
                    retry_base_delay_seconds = 2f,
                    retry_max_delay_seconds = 60f,
                    queue_persistent = true
                },
                storage = new StorageConfig
                {
                    provider = "json",
                    autosave_interval_seconds = 2f,
                    session_export_path = "Sessions/"
                },
                diagnostics = new DiagnosticsConfig
                {
                    show_fps_overlay = true,
                    log_inference_time = true,
                    show_memory_monitor = false,
                    log_tracking_events = false,
                    verbose_mode = false
                }
            };
        }

        private void InitializeSubsystems()
        {
            if (appConfig == null)
            {
                Debug.LogError("[AppManager] Config not available.");
                return;
            }

            var diagnosticsManager = FindObjectOfType<Diagnostics.DiagnosticsManager>();
            if (diagnosticsManager != null)
            {
                try { diagnosticsManager.Initialize(appConfig.diagnostics); }
                catch (System.Exception ex) { Debug.LogError($"[AppManager] DiagnosticsManager init error: {ex.Message}"); }
            }

            var sessionStorage = FindObjectOfType<Storage.SessionStorage>();
            if (sessionStorage != null)
            {
                try { sessionStorage.Initialize(appConfig.storage); }
                catch (System.Exception ex) { Debug.LogError($"[AppManager] SessionStorage init error: {ex.Message}"); }
            }

            var syncManager = FindObjectOfType<Sync.SyncPulseManager>();
            if (syncManager != null)
            {
                try { syncManager.Initialize(appConfig.sync); }
                catch (System.Exception ex) { Debug.LogError($"[AppManager] SyncManager init error: {ex.Message}"); }
            }

            var visionProcessor = FindObjectOfType<Vision.FrameProcessor>();
            if (visionProcessor != null)
            {
                try { visionProcessor.Initialize(appConfig.model); }
                catch (System.Exception ex) { Debug.LogError($"[AppManager] FrameProcessor init error: {ex.Message}"); }
            }

            var countManager = FindObjectOfType<Counting.CountManager>();
            if (countManager != null)
            {
                try { countManager.Initialize(appConfig.counting); }
                catch (System.Exception ex) { Debug.LogError($"[AppManager] CountManager init error: {ex.Message}"); }
            }

            isInitialized = true;
            Debug.Log("[AppManager] Subsystems initialized.");
        }

        public void StartScan()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("[AppManager] Subsystems not ready. Re-initializing...");
                InitializeSubsystems();
            }

            Debug.Log("[AppManager] Starting scan...");

            if (arSessionObject != null)
                arSessionObject.SetActive(true);

            var sessionStorage = FindObjectOfType<Storage.SessionStorage>();
            sessionStorage?.StartNewSession();

            var visionProcessor = FindObjectOfType<Vision.FrameProcessor>();
            visionProcessor?.StartProcessing();

            var syncManager = FindObjectOfType<Sync.SyncPulseManager>();
            syncManager?.StartPulsing();

            Debug.Log("[AppManager] Scan started.");
        }

        public void StopScan()
        {
            Debug.Log("[AppManager] Stopping scan...");

            var visionProcessor = FindObjectOfType<Vision.FrameProcessor>();
            visionProcessor?.StopProcessing();

            var syncManager = FindObjectOfType<Sync.SyncPulseManager>();
            syncManager?.StopPulsing();

            var sessionStorage = FindObjectOfType<Storage.SessionStorage>();
            sessionStorage?.EndCurrentSession();

            Debug.Log("[AppManager] Scan stopped.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
