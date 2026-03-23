using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace NomadGo.AppShell
{
    /// <summary>
    /// Fixes camera background rendering for ARFoundation on Android.
    /// Ensures the AR camera background is properly displayed instead of a blue screen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFix : MonoBehaviour
    {
        private Camera cam;
        private ARCameraBackground arBackground;
        private ARCameraManager arCameraManager;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            arBackground = GetComponent<ARCameraBackground>();
            arCameraManager = GetComponent<ARCameraManager>();

            // Fix camera clear flags for AR
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                Debug.Log("[CameraFix] Camera flags fixed for AR rendering.");
            }
        }

        private void Start()
        {
            // Add ARCameraBackground if missing
            if (arBackground == null)
            {
                arBackground = gameObject.AddComponent<ARCameraBackground>();
                Debug.Log("[CameraFix] Added ARCameraBackground component.");
            }

            // Add ARCameraManager if missing
            if (arCameraManager == null)
            {
                arCameraManager = gameObject.AddComponent<ARCameraManager>();
                arCameraManager.autoFocusRequested = true;
                arCameraManager.requestedFacingDirection = CameraFacingDirection.World;
                Debug.Log("[CameraFix] Added ARCameraManager component.");
            }

            // Fix camera clear flags after AR setup
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }

        private void Update()
        {
            // Ensure camera flags stay correct
            if (cam != null && cam.clearFlags != CameraClearFlags.SolidColor)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }
        }
    }
}
