using System;
using System.IO;
using System.Text;
using UnityEngine;

public class ReportExporter : MonoBehaviour
{
    private string folderPath;

    private void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        folderPath = "/storage/emulated/0/Download/NomadGo/";
#else
        folderPath = Path.Combine(Application.dataPath, "Exports/");
#endif

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
    }

    public void ExportProductsReport()
    {
        try
        {
            var manager = ClientCatalogManager.Instance ?? FindObjectOfType<ClientCatalogManager>();

            if (manager == null || !manager.IsLoaded)
            {
                Debug.LogError("[ReportExporter] Catalog not loaded");
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string excelPath = Path.Combine(folderPath, $"Client_Products_{timestamp}.csv");
            string pdfPath = Path.Combine(folderPath, $"Client_Products_{timestamp}.pdf");

            ExportCSV(manager, excelPath);
            ExportPDF(manager, pdfPath);

            Debug.Log("[ReportExporter] Export done:");
            Debug.Log("Excel: " + excelPath);
            Debug.Log("PDF: " + pdfPath);

            var ui = NomadGo.AppShell.UIBuilder.Instance ?? FindObjectOfType<NomadGo.AppShell.UIBuilder>();
            if (ui != null)
                ui.SetCatalogUploadStatus(true, "Export completed successfully. Excel and PDF saved to Downloads.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[ReportExporter] Error: " + ex.Message);

            var ui = NomadGo.AppShell.UIBuilder.Instance ?? FindObjectOfType<NomadGo.AppShell.UIBuilder>();
            if (ui != null)
                ui.SetCatalogUploadStatus(false, "Export failed: " + ex.Message);
        }
    }

    // ================= Excel (CSV) =================

    private void ExportCSV(ClientCatalogManager manager, string path)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("Client,SKU,Name,Category,Barcode,Visual,Hint");

        foreach (var item in manager.GetType().GetField("catalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(manager) as dynamic)
        {
            foreach (var p in item.items)
            {
                sb.AppendLine($"{Safe(item.client_name)},{Safe(p.sku)},{Safe(p.name)},{Safe(p.category)},{Safe(p.barcode)},{Safe(p.visual_class)},{Safe(p.image_hint)}");
            }
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    // ================= PDF بسيط =================

    private void ExportPDF(ClientCatalogManager manager, string path)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("CLIENT PRODUCTS REPORT");
        sb.AppendLine("------------------------------");
        sb.AppendLine("Client: " + manager.ClientName);
        sb.AppendLine("Total: " + manager.ItemsCount);
        sb.AppendLine("------------------------------");

        string report = manager.BuildReportText();
        sb.AppendLine(report);

        // PDF فعلي يحتاج مكتبة، حالياً نحفظ كنص لكن بصيغة PDF
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private string Safe(string v)
    {
        return string.IsNullOrEmpty(v) ? "-" : v.Replace(",", " ");
    }
}
