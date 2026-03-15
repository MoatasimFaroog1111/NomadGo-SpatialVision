using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace NomadGo.AppShell
{
    public class AppManager : MonoBehaviour
    {
        public static AppManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private ARSession arSession;
        [SerializeField] private Camera arCamera;

        private AppConfig appConfig;
        private bool isInitialized = false;

        public AppConfig Config => appConfig;
        public Camera ARCamera => arCamera;
        public ARSession ARSession => arSession;

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
                return;
            }

            appConfig = JsonUtility.FromJson<AppConfig>(configText.text);
            Debug.Log($"[AppManager] Config loaded: {appConfig.app.name} v{appConfig.app.version}");
        }

        private void InitializeSubsystems()
        {
            if (appConfig == null)
            {
                Debug.LogError("[AppManager] Cannot initialize — config not loaded.");
                return;
            }

            var diagnosticsManager = FindObjectOfType<Diagnostics.DiagnosticsManager>();
            if (diagnosticsManager != null)
            {
                diagnosticsManager.Initialize(appConfig.diagnostics);
                Debug.Log("[AppManager] DiagnosticsManager initialized.");
            }

            var sessionStorage = FindObjectOfType<Storage.SessionStorage>();
            if (sessionStorage != null)
            {
                sessionStorage.Initialize(appConfig.storage);
                Debug.Log("[AppManager] SessionStorage initialized.");
            }

            var syncManager = FindObjectOfType<Sync.SyncPulseManager>();
            if (syncManager != null)
            {
                syncManager.Initialize(appConfig.sync);
                Debug.Log("[AppManager] SyncPulseManager initialized.");
            }

            var visionProcessor = FindObjectOfType<Vision.FrameProcessor>();
            if (visionProcessor != null)
            {
                visionProcessor.Initialize(appConfig.model);
                Debug.Log("[AppManager] FrameProcessor initialized.");
            }

            var countManager = FindObjectOfType<Counting.CountManager>();
            if (countManager != null)
            {
                countManager.Initialize(appConfig.counting);
                Debug.Log("[AppManager] CountManager initialized.");
            }

            isInitialized = true;
            Debug.Log("[AppManager] All subsystems initialized successfully.");
        }

        public void StartScan()
        {
            if (!isInitialized)
            {
                Debug.LogError("[AppManager] Cannot start scan — subsystems not initialized.");
                return;
            }

            Debug.Log("[AppManager] Starting scan session...");

            if (arSession != null)
            {
                arSession.enabled = true;
            }

            var sessionStorage = FindObjectOfType<Storage.SessionStorage>();
            if (sessionStorage != null)
            {
                sessionStorage.StartNewSession();
            }

            var visionProcessor = FindObjectOfType<Vision.FrameProcessor>();
            if (visionProcessor != null)
            {
                visionProcessor.StartProcessing();
            }

            var syncManager = FindObjectOfType<Sync.SyncPulseManager>();
            if (syncManager != null)
            {
                syncManager.StartPulsing();
            }

            Debug.Log("[AppManager] Scan session started.");
        }

        public void StopScan()
        {
            Debug.Log("[AppManager] Stopping scan session...");

            var visionProcessor = FindObjectOfType<Vision.FrameProcessor>();
            if (visionProcessor != null)
            {
                visionProcessor.StopProcessing();
            }

            var syncManager = FindObjectOfType<Sync.SyncPulseManager>();
            if (syncManager != null)
            {
                syncManager.StopPulsing();
            }

            var sessionStorage = FindObjectOfType<Storage.SessionStorage>();
            if (sessionStorage != null)
            {
                sessionStorage.EndCurrentSession();
            }

            Debug.Log("[AppManager] Scan session stopped.");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
