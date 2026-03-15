using System.Collections.Generic;
using UnityEngine;
using NomadGo.Vision;
using NomadGo.Counting;

namespace NomadGo.AROverlay
{
    public class OverlayRenderer : MonoBehaviour
    {
        [Header("Overlay Settings")]
        [SerializeField] private Color boundingBoxColor = Color.green;
        [SerializeField] private Color rowLineColor = Color.cyan;
        [SerializeField] private float lineWidth = 2f;
        [SerializeField] private int fontSize = 14;
        [SerializeField] private Color labelBackgroundColor = new Color(0, 0, 0, 0.7f);
        [SerializeField] private Color labelTextColor = Color.white;

        private List<DetectionResult> currentDetections = new List<DetectionResult>();
        private List<RowCluster> currentClusters = new List<RowCluster>();
        private int totalCount = 0;
        private Dictionary<string, int> countsByLabel = new Dictionary<string, int>();

        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle countStyle;
        private GUIStyle rowStyle;
        private bool stylesInitialized = false;

        private void Start()
        {
            var countManager = FindObjectOfType<CountManager>();
            if (countManager != null)
            {
                countManager.OnCountsUpdated += OnCountsUpdated;
            }

            var frameProcessor = FindObjectOfType<FrameProcessor>();
            if (frameProcessor != null)
            {
                frameProcessor.OnDetectionsUpdated += OnDetectionsUpdated;
            }
        }

        private void OnDetectionsUpdated(List<DetectionResult> detections)
        {
            currentDetections = detections;
        }

        private void OnCountsUpdated(int total, Dictionary<string, int> counts, List<RowCluster> clusters)
        {
            totalCount = total;
            countsByLabel = counts;
            currentClusters = clusters;
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            boxStyle = new GUIStyle();
            Texture2D boxTex = new Texture2D(1, 1);
            boxTex.SetPixel(0, 0, boundingBoxColor);
            boxTex.Apply();
            boxStyle.normal.background = boxTex;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = fontSize;
            labelStyle.normal.textColor = labelTextColor;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontStyle = FontStyle.Bold;

            Texture2D labelBg = new Texture2D(1, 1);
            labelBg.SetPixel(0, 0, labelBackgroundColor);
            labelBg.Apply();
            labelStyle.normal.background = labelBg;
            labelStyle.padding = new RectOffset(4, 4, 2, 2);

            countStyle = new GUIStyle(GUI.skin.label);
            countStyle.fontSize = fontSize + 8;
            countStyle.normal.textColor = Color.white;
            countStyle.alignment = TextAnchor.UpperLeft;
            countStyle.fontStyle = FontStyle.Bold;

            Texture2D countBg = new Texture2D(1, 1);
            countBg.SetPixel(0, 0, new Color(0, 0, 0, 0.8f));
            countBg.Apply();
            countStyle.normal.background = countBg;
            countStyle.padding = new RectOffset(8, 8, 4, 4);

            rowStyle = new GUIStyle();
            Texture2D rowTex = new Texture2D(1, 1);
            rowTex.SetPixel(0, 0, new Color(rowLineColor.r, rowLineColor.g, rowLineColor.b, 0.3f));
            rowTex.Apply();
            rowStyle.normal.background = rowTex;

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitializeStyles();

            DrawBoundingBoxes();
            DrawRowIndicators();
            DrawCountOverlay();
        }

        private void DrawBoundingBoxes()
        {
            foreach (var det in currentDetections)
            {
                Rect box = det.boundingBox;

                GUI.Box(new Rect(box.x, box.y, box.width, lineWidth), GUIContent.none, boxStyle);
                GUI.Box(new Rect(box.x, box.yMax - lineWidth, box.width, lineWidth), GUIContent.none, boxStyle);
                GUI.Box(new Rect(box.x, box.y, lineWidth, box.height), GUIContent.none, boxStyle);
                GUI.Box(new Rect(box.xMax - lineWidth, box.y, lineWidth, box.height), GUIContent.none, boxStyle);

                string labelText = $"{det.label} {det.confidence:F0}%";
                Vector2 labelSize = labelStyle.CalcSize(new GUIContent(labelText));
                Rect labelRect = new Rect(box.x, box.y - labelSize.y - 2, labelSize.x + 8, labelSize.y + 4);
                GUI.Label(labelRect, labelText, labelStyle);
            }
        }

        private void DrawRowIndicators()
        {
            foreach (var cluster in currentClusters)
            {
                float rowY = cluster.yCenter;
                GUI.Box(new Rect(0, rowY - 1, Screen.width, 2), GUIContent.none, rowStyle);

                string rowText = $"Row {cluster.rowIndex + 1}: {cluster.Count} items";
                GUI.Label(new Rect(10, rowY - 20, 200, 20), rowText, labelStyle);
            }
        }

        private void DrawCountOverlay()
        {
            float yOffset = 10;
            float xOffset = 10;

            GUI.Label(new Rect(xOffset, yOffset, 300, 40), $"Total Count: {totalCount}", countStyle);
            yOffset += 45;

            foreach (var kvp in countsByLabel)
            {
                GUI.Label(new Rect(xOffset, yOffset, 250, 25), $"  {kvp.Key}: {kvp.Value}", labelStyle);
                yOffset += 28;
            }
        }

        private void OnDestroy()
        {
            var countManager = FindObjectOfType<CountManager>();
            if (countManager != null)
            {
                countManager.OnCountsUpdated -= OnCountsUpdated;
            }
        }
    }
}
