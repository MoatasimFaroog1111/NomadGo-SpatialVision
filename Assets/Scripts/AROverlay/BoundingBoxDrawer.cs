using UnityEngine;
using NomadGo.Vision;

namespace NomadGo.AROverlay
{
    public class BoundingBoxDrawer : MonoBehaviour
    {
        [Header("Box Settings")]
        [SerializeField] private Color defaultColor = Color.green;
        [SerializeField] private Color highConfidenceColor = Color.yellow;
        [SerializeField] private float highConfidenceThreshold = 0.8f;
        [SerializeField] private float cornerLength = 10f;
        [SerializeField] private float borderWidth = 2f;

        private Material lineMaterial;

        private void Start()
        {
            CreateLineMaterial();
        }

        private void CreateLineMaterial()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("[BoundingBoxDrawer] Shader not found.");
                return;
            }

            lineMaterial = new Material(shader);
            lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
        }

        public void DrawBox(DetectionResult detection)
        {
            if (lineMaterial == null) return;

            Color color = detection.confidence >= highConfidenceThreshold
                ? highConfidenceColor
                : defaultColor;

            Rect box = detection.boundingBox;
            DrawRectOutline(box, color);
            DrawCorners(box, color);
        }

        private void DrawRectOutline(Rect rect, Color color)
        {
            GL.PushMatrix();
            lineMaterial.SetPass(0);
            GL.LoadPixelMatrix();

            GL.Begin(GL.QUADS);
            GL.Color(color);

            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), borderWidth);
            DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), borderWidth);
            DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), borderWidth);
            DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), borderWidth);

            GL.End();
            GL.PopMatrix();
        }

        private void DrawCorners(Rect rect, Color color)
        {
            GL.PushMatrix();
            lineMaterial.SetPass(0);
            GL.LoadPixelMatrix();

            GL.Begin(GL.QUADS);
            GL.Color(color);

            float cw = borderWidth * 2;

            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin + cornerLength, rect.yMin), cw);
            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMin + cornerLength), cw);

            DrawLine(new Vector2(rect.xMax - cornerLength, rect.yMin), new Vector2(rect.xMax, rect.yMin), cw);
            DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMin + cornerLength), cw);

            DrawLine(new Vector2(rect.xMin, rect.yMax - cornerLength), new Vector2(rect.xMin, rect.yMax), cw);
            DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin + cornerLength, rect.yMax), cw);

            DrawLine(new Vector2(rect.xMax, rect.yMax - cornerLength), new Vector2(rect.xMax, rect.yMax), cw);
            DrawLine(new Vector2(rect.xMax - cornerLength, rect.yMax), new Vector2(rect.xMax, rect.yMax), cw);

            GL.End();
            GL.PopMatrix();
        }

        private void DrawLine(Vector2 start, Vector2 end, float width)
        {
            Vector2 dir = (end - start).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * width * 0.5f;

            GL.Vertex3(start.x + perp.x, start.y + perp.y, 0);
            GL.Vertex3(start.x - perp.x, start.y - perp.y, 0);
            GL.Vertex3(end.x - perp.x, end.y - perp.y, 0);
            GL.Vertex3(end.x + perp.x, end.y + perp.y, 0);
        }

        private void OnDestroy()
        {
            if (lineMaterial != null)
            {
                DestroyImmediate(lineMaterial);
            }
        }
    }
}
