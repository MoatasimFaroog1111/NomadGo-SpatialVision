using UnityEditor;
using UnityEngine;
using Unity.Barracuda;
using Unity.Barracuda.ONNX;
using System.IO;

public class ConvertONNXToBarracuda
{
    public static void Convert()
    {
        string onnxPath = "Assets/StreamingAssets/Models/yolov8n.onnx";
        string outputPath = "Assets/StreamingAssets/Models/yolov8n.nn";

        Debug.Log($"[ConvertONNX] Reading ONNX from: {onnxPath}");

        if (!File.Exists(onnxPath))
        {
            Debug.LogError($"[ConvertONNX] ONNX file not found: {onnxPath}");
            EditorApplication.Exit(1);
            return;
        }

        try
        {
            byte[] onnxBytes = File.ReadAllBytes(onnxPath);
            Debug.Log($"[ConvertONNX] ONNX size: {onnxBytes.Length / 1024 / 1024f:F1} MB");

            // treatErrorsAsWarnings=true: ignore unsupported ops, continue conversion
            var converter = new ONNXModelConverter(
                optimizeModel: false,
                treatErrorsAsWarnings: true,
                forceArbitraryBatchSize: true);

            Model model = converter.Convert(onnxBytes);
            Debug.Log($"[ConvertONNX] ONNX converted. Layers: {model.layers.Count}");

            ModelWriter.Save(outputPath, model);
            Debug.Log($"[ConvertONNX] ✅ Saved: {outputPath}");

            long nnSize = new FileInfo(outputPath).Length;
            Debug.Log($"[ConvertONNX] .nn size: {nnSize / 1024 / 1024f:F1} MB");

            AssetDatabase.Refresh();
            EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ConvertONNX] FAILED: {ex.GetType().Name}: {ex.Message}");
            EditorApplication.Exit(1);
        }
    }
}
