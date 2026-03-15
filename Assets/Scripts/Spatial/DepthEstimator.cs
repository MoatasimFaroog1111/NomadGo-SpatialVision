using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.Collections;

namespace NomadGo.Spatial
{
    public class DepthEstimator : MonoBehaviour
    {
        [SerializeField] private AROcclusionManager occlusionManager;

        private bool depthAvailable = false;

        public bool DepthAvailable => depthAvailable;

        private void Update()
        {
            if (occlusionManager == null) return;

            var envDepth = occlusionManager.environmentDepthTexture;
            depthAvailable = envDepth != null;
        }

        public float EstimateDepthAtScreenPoint(Vector2 normalizedScreenPoint)
        {
            if (occlusionManager == null || !depthAvailable)
            {
                return -1f;
            }

            var depthTexture = occlusionManager.environmentDepthTexture;
            if (depthTexture == null) return -1f;

            int x = Mathf.Clamp((int)(normalizedScreenPoint.x * depthTexture.width), 0, depthTexture.width - 1);
            int y = Mathf.Clamp((int)(normalizedScreenPoint.y * depthTexture.height), 0, depthTexture.height - 1);

            var pixels = depthTexture.GetPixels();
            if (pixels == null || pixels.Length == 0) return -1f;

            int index = y * depthTexture.width + x;
            if (index < 0 || index >= pixels.Length) return -1f;

            float depth = pixels[index].r;
            return depth;
        }

        public float EstimateDepthAtBoundingBox(Rect boundingBox)
        {
            Vector2 center = new Vector2(
                (boundingBox.xMin + boundingBox.xMax) / 2f,
                (boundingBox.yMin + boundingBox.yMax) / 2f
            );

            float centerDepth = EstimateDepthAtScreenPoint(center);
            if (centerDepth < 0) return -1f;

            float topDepth = EstimateDepthAtScreenPoint(new Vector2(center.x, boundingBox.yMin));
            float bottomDepth = EstimateDepthAtScreenPoint(new Vector2(center.x, boundingBox.yMax));

            float avgDepth = centerDepth;
            int count = 1;

            if (topDepth > 0) { avgDepth += topDepth; count++; }
            if (bottomDepth > 0) { avgDepth += bottomDepth; count++; }

            return avgDepth / count;
        }
    }
}
