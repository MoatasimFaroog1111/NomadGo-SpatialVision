using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

namespace NomadGo.Spatial
{
    public class SpatialManager : MonoBehaviour
    {
        [Header("AR Components")]
        [SerializeField] private ARPlaneManager arPlaneManager;
        [SerializeField] private ARRaycastManager arRaycastManager;

        private List<ARPlane> detectedPlanes = new List<ARPlane>();
        private bool isTracking = false;

        public bool IsTracking => isTracking;
        public List<ARPlane> DetectedPlanes => detectedPlanes;

        public delegate void PlaneDetectedHandler(ARPlane plane);
        public event PlaneDetectedHandler OnPlaneDetected;

        public delegate void TrackingStateChangedHandler(bool isTracking);
        public event TrackingStateChangedHandler OnTrackingStateChanged;

        private void OnEnable()
        {
            if (arPlaneManager != null)
            {
                arPlaneManager.planesChanged += OnPlanesChanged;
            }
        }

        private void OnDisable()
        {
            if (arPlaneManager != null)
            {
                arPlaneManager.planesChanged -= OnPlanesChanged;
            }
        }

        private void Update()
        {
            bool currentTracking = ARSession.state == ARSessionState.SessionTracking;
            if (currentTracking != isTracking)
            {
                isTracking = currentTracking;
                OnTrackingStateChanged?.Invoke(isTracking);
                Debug.Log($"[SpatialManager] Tracking state changed: {isTracking}");
            }
        }

        private void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            foreach (var plane in args.added)
            {
                if (!detectedPlanes.Contains(plane))
                {
                    detectedPlanes.Add(plane);
                    OnPlaneDetected?.Invoke(plane);
                    Debug.Log($"[SpatialManager] New plane detected: {plane.trackableId}, Classification: {plane.classification}");
                }
            }

            foreach (var plane in args.removed)
            {
                detectedPlanes.Remove(plane);
            }
        }

        public bool TryGetPlaneAtScreenPoint(Vector2 screenPoint, out ARRaycastHit hit)
        {
            hit = default;
            if (arRaycastManager == null) return false;

            var hits = new List<ARRaycastHit>();
            if (arRaycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon))
            {
                hit = hits[0];
                return true;
            }
            return false;
        }

        public Vector3 GetWorldPositionFromScreen(Vector2 screenPoint)
        {
            if (TryGetPlaneAtScreenPoint(screenPoint, out ARRaycastHit hit))
            {
                return hit.pose.position;
            }
            return Vector3.zero;
        }
    }
}
