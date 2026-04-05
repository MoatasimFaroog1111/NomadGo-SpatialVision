using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace NomadGo.Sync
{
    public class SyncPulseManager : MonoBehaviour
    {
        [SerializeField] private NetworkMonitor networkMonitor;
        [SerializeField] private PulseQueue pulseQueue;

        // Injected by AppManager — no FindObjectOfType at runtime
        private Counting.CountManager   countManager;
        private Storage.SessionStorage  sessionStorage;

        private string baseUrl;
        private float  pulseInterval    = 5f;
        private int    retryMaxAttempts = 5;
        private float  retryBaseDelay   = 2f;
        private float  retryMaxDelay    = 60f;
        private bool   isPulsing        = false;
        private Coroutine pulseCoroutine;
        private int totalPulsesSent   = 0;
        private int totalPulsesFailed = 0;

        public int TotalPulsesSent    => totalPulsesSent;
        public int TotalPulsesFailed  => totalPulsesFailed;
        public int PendingPulseCount  => pulseQueue != null ? pulseQueue.Count : 0;

        public void Initialize(AppShell.SyncConfig config)
        {
            baseUrl           = config.base_url;
            pulseInterval     = config.pulse_interval_seconds;
            retryMaxAttempts  = config.retry_max_attempts;
            retryBaseDelay    = config.retry_base_delay_seconds;
            retryMaxDelay     = config.retry_max_delay_seconds;

            if (networkMonitor == null)
                networkMonitor = gameObject.AddComponent<NetworkMonitor>();

            if (pulseQueue == null)
                pulseQueue = gameObject.AddComponent<PulseQueue>();

            pulseQueue.Initialize(config.queue_persistent);

            networkMonitor.OnNetworkStatusChanged += OnNetworkStatusChanged;

            Debug.Log($"[SyncPulse] Initialized. URL: {baseUrl}, Interval: {pulseInterval}s");
        }

        /// <summary>Called by AppManager after all subsystems are ready.</summary>
        public void InjectReferences(Counting.CountManager cm, Storage.SessionStorage ss)
        {
            countManager   = cm;
            sessionStorage = ss;
        }

        public void StartPulsing()
        {
            if (isPulsing) return;
            isPulsing      = true;
            pulseCoroutine = StartCoroutine(PulseLoop());
            Debug.Log("[SyncPulse] Pulsing started.");
        }

        public void StopPulsing()
        {
            isPulsing = false;
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
            }
            Debug.Log("[SyncPulse] Pulsing stopped.");
        }

        private IEnumerator PulseLoop()
        {
            while (isPulsing)
            {
                yield return new WaitForSeconds(pulseInterval);

                EnqueueCurrentState();

                if (networkMonitor.IsOnline)
                    yield return ProcessQueue();
                else
                    Debug.Log("[SyncPulse] Offline — pulse queued.");
            }
        }

        private void EnqueueCurrentState()
        {
            if (countManager == null || sessionStorage == null) return;
            if (sessionStorage.CurrentSession == null) return;

            var pulse = new PulseData
            {
                sessionId  = sessionStorage.CurrentSession.sessionId,
                timestamp  = DateTime.UtcNow.ToString("o"),
                totalCount = countManager.TotalCount,
                rowCount   = countManager.CurrentClusters.Count,
                deviceId   = SystemInfo.deviceUniqueIdentifier
            };

            foreach (var kvp in countManager.CurrentCounts)
                pulse.countsByLabel.Add(new Storage.LabelCount { label = kvp.Key, count = kvp.Value });

            pulseQueue.Enqueue(pulse);
        }

        private IEnumerator ProcessQueue()
        {
            while (pulseQueue.Count > 0 && networkMonitor.IsOnline)
            {
                PulseData pulse = pulseQueue.Peek();
                if (pulse == null) break;

                bool success = false;
                yield return SendPulse(pulse, result => { success = result; });

                if (success)
                {
                    pulseQueue.Dequeue();
                    totalPulsesSent++;
                    Debug.Log($"[SyncPulse] Sent: {pulse.pulseId}");
                }
                else
                {
                    pulseQueue.Dequeue();

                    if (pulse.attemptCount >= retryMaxAttempts)
                    {
                        totalPulsesFailed++;
                        Debug.LogWarning($"[SyncPulse] Dropped after {retryMaxAttempts} attempts: {pulse.pulseId}");
                    }
                    else
                    {
                        pulseQueue.RequeueWithRetry(pulse);
                        float delay = Mathf.Min(retryBaseDelay * Mathf.Pow(2, pulse.attemptCount), retryMaxDelay);
                        Debug.Log($"[SyncPulse] Retry in {delay}s: {pulse.pulseId}");
                        yield return new WaitForSeconds(delay);
                    }
                }
            }
        }

        private IEnumerator SendPulse(PulseData pulse, Action<bool> callback)
        {
            string json = JsonUtility.ToJson(pulse);

            using (var request = new UnityWebRequest(baseUrl, "POST"))
            {
                byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
                request.uploadHandler   = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    callback(true);
                else
                {
                    Debug.LogWarning($"[SyncPulse] Failed: {request.error}");
                    callback(false);
                }
            }
        }

        private void OnNetworkStatusChanged(bool online)
        {
            if (online && isPulsing && pulseQueue.Count > 0)
            {
                Debug.Log("[SyncPulse] Network restored — flushing queue...");
                StartCoroutine(ProcessQueue());
            }
        }

        private void OnDestroy()
        {
            StopPulsing();
            if (networkMonitor != null)
                networkMonitor.OnNetworkStatusChanged -= OnNetworkStatusChanged;
        }
    }
}
