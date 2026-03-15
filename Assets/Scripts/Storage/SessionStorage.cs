using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NomadGo.Storage
{
    public class SessionStorage : MonoBehaviour
    {
        [SerializeField] private JSONStorageProvider storageProvider;

        private SessionData currentSession;
        private float autosaveInterval = 2f;
        private float autosaveTimer = 0f;
        private bool isSessionActive = false;

        public SessionData CurrentSession => currentSession;
        public bool IsSessionActive => isSessionActive;

        public void Initialize(AppShell.StorageConfig config)
        {
            autosaveInterval = config.autosave_interval_seconds;

            if (storageProvider == null)
            {
                storageProvider = gameObject.AddComponent<JSONStorageProvider>();
            }

            storageProvider.Initialize(config.session_export_path);
            Debug.Log($"[SessionStorage] Initialized. Autosave interval: {autosaveInterval}s");
        }

        public void StartNewSession()
        {
            currentSession = new SessionData
            {
                sessionId = Guid.NewGuid().ToString("N").Substring(0, 12),
                startTime = DateTime.UtcNow.ToString("o"),
                deviceId = SystemInfo.deviceUniqueIdentifier,
                totalItemsCounted = 0
            };

            isSessionActive = true;
            autosaveTimer = 0f;

            LogEvent("session_start", "New scan session started.");
            Debug.Log($"[SessionStorage] New session started: {currentSession.sessionId}");
        }

        public void EndCurrentSession()
        {
            if (currentSession == null) return;

            currentSession.endTime = DateTime.UtcNow.ToString("o");
            isSessionActive = false;

            LogEvent("session_end", "Scan session ended.");
            SaveCurrentSession();

            Debug.Log($"[SessionStorage] Session ended: {currentSession.sessionId}");
        }

        private void Update()
        {
            if (!isSessionActive || currentSession == null) return;

            autosaveTimer += Time.deltaTime;
            if (autosaveTimer >= autosaveInterval)
            {
                autosaveTimer = 0f;
                SaveCurrentSession();
                CaptureSnapshot();
            }
        }

        private void CaptureSnapshot()
        {
            var countManager = FindObjectOfType<Counting.CountManager>();
            var frameProcessor = FindObjectOfType<Vision.FrameProcessor>();

            if (countManager == null) return;

            var snapshot = new SessionSnapshot
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                totalCount = countManager.TotalCount,
                rowCount = countManager.CurrentClusters.Count,
                inferenceTimeMs = frameProcessor != null ? frameProcessor.LastInferenceTimeMs : 0f,
                fps = 1f / Time.deltaTime
            };

            foreach (var kvp in countManager.CurrentCounts)
            {
                snapshot.countsByLabel.Add(new LabelCount { label = kvp.Key, count = kvp.Value });
            }

            currentSession.snapshots.Add(snapshot);
            currentSession.totalItemsCounted = countManager.TotalCount;
        }

        private void SaveCurrentSession()
        {
            if (currentSession == null || storageProvider == null) return;
            storageProvider.SaveSession(currentSession);
        }

        public string ExportCurrentSession()
        {
            if (currentSession == null || storageProvider == null)
            {
                Debug.LogWarning("[SessionStorage] No session to export.");
                return null;
            }

            return storageProvider.ExportSession(currentSession);
        }

        public void LogEvent(string eventType, string details)
        {
            if (currentSession == null) return;

            currentSession.events.Add(new SessionEvent
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                eventType = eventType,
                details = details
            });
        }

        public string[] GetAllSessionIds()
        {
            if (storageProvider == null) return new string[0];
            return storageProvider.ListSessions();
        }

        public SessionData LoadSession(string sessionId)
        {
            if (storageProvider == null) return null;
            return storageProvider.LoadSession(sessionId);
        }
    }
}
