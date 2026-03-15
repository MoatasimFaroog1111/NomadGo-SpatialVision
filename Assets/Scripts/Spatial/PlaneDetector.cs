using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

namespace NomadGo.Spatial
{
    public class PlaneDetector : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Material planeMaterial;
        [SerializeField] private bool showPlaneVisuals = true;
        [SerializeField] private int maxPlaneCount = 10;

        private Dictionary<TrackableId, GameObject> planeVisuals = new Dictionary<TrackableId, GameObject>();

        public int DetectedPlaneCount => planeVisuals.Count;

        private void OnEnable()
        {
            if (planeManager != null)
            {
                planeManager.planesChanged += HandlePlanesChanged;
            }
        }

        private void OnDisable()
        {
            if (planeManager != null)
            {
                planeManager.planesChanged -= HandlePlanesChanged;
            }
        }

        public void Configure(int maxPlanes, string detectionMode)
        {
            maxPlaneCount = maxPlanes;

            if (planeManager != null)
            {
                switch (detectionMode)
                {
                    case "Horizontal":
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                        break;
                    case "Vertical":
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
                        break;
                    case "Everything":
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
                        break;
                    default:
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                        break;
                }
            }

            Debug.Log($"[PlaneDetector] Configured: maxPlanes={maxPlanes}, mode={detectionMode}");
        }

        private void HandlePlanesChanged(ARPlanesChangedEventArgs args)
        {
            foreach (var plane in args.added)
            {
                if (planeVisuals.Count >= maxPlaneCount)
                {
                    Debug.Log($"[PlaneDetector] Max plane count ({maxPlaneCount}) reached. Ignoring new plane.");
                    continue;
                }

                if (showPlaneVisuals && !planeVisuals.ContainsKey(plane.trackableId))
                {
                    planeVisuals[plane.trackableId] = plane.gameObject;
                    Debug.Log($"[PlaneDetector] Plane added: {plane.trackableId}");
                }
            }

            foreach (var plane in args.removed)
            {
                if (planeVisuals.ContainsKey(plane.trackableId))
                {
                    planeVisuals.Remove(plane.trackableId);
                    Debug.Log($"[PlaneDetector] Plane removed: {plane.trackableId}");
                }
            }
        }

        public void SetPlaneVisualsEnabled(bool enabled)
        {
            showPlaneVisuals = enabled;
            foreach (var kvp in planeVisuals)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.SetActive(enabled);
                }
            }
        }
    }
}
