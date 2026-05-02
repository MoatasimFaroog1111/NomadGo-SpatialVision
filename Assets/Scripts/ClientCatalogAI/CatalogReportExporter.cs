using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class CatalogReportExporter : MonoBehaviour
{
    public static CatalogReportExporter Instance;

    private void Awake()
    {
        Instance = this;
    }

    public ExportResult ExportExcelAndPdf()
    {
        var manager = ClientCatalogManager.Instance ?? FindObjectOfType<ClientCatalogManager>();

        if (manager == null || !manager.IsLoaded)
            return ExportResult.Fail("No client products file loaded. Upload the file first.");

        List<CatalogItem> items = manager.GetItems();
        if (items.Count == 0)
            return ExportResult.Fail("No products found inside the uploaded file.");

        try
        {
            string safeClient = MakeSafeFileName(manager.ClientName);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string excelName = $"{safeClient}_Products_Report_{stamp}.xls";
            string pdfName = $"{safeClient}_Products_Report_{stamp}.pdf";

            string excelPath = Path.Combine(Application.persistentDataPath, excelName);
            string pdfPath = Path.Combine(Application.persistentDataPath, pdfName);

            WriteExcelHtml(excelPath, manager.ClientName, items);
            WriteSimplePdf(pdfPath, manager.ClientName, items);

#if UNITY_ANDROID && !UNITY_EDITOR
            string excelDownload = SaveToAndroidDownloads(excelPath, excelName, "application/vnd.ms-excel");
            string pdfDownload = SaveToAndroidDownloads(pdfPath, pdfName, "application/pdf");

            return ExportResult.Ok(
                "Export completed successfully. Excel and PDF saved to Downloads.",
                string.IsNullOrEmpty(excelDownload) ? excelPath : excelDownload,
                string.IsNullOrEmpty(pdfDownload) ? pdfPath : pdfDownload
            );
#else
            return ExportResult.Ok("Export completed successfully.", excelPath, pdfPath);
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError("[CatalogReportExporter] Export failed: " + ex);
            return ExportResult.Fail("Export failed: " + ex.Message);
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private string SaveToAndroidDownloads(string sourcePath, string displayName, string mimeType)
    {
        try
        {
            using (var bridge = new AndroidJavaClass("com.nomadgo.spatialvision.FileExportBridge"))
            {
                return bridge.CallStatic<string>("saveFileToDownloads", sourcePath, displayName, mimeType);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[CatalogReportExporter] Android Downloads save failed: " + ex.Message);
            return string.Empty;
        }
    }
#endif

    private void WriteExcelHtml(string path, string clientName, List<CatalogItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><head><meta charset='utf-8'></head><body>");
        sb.AppendLine($"<h2>Client Products Report</h2>");
        sb.AppendLine($"<p><b>Client:</b> {Html(clientName)}</p>");
        sb.AppendLine($"<p><b>Generated:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        sb.AppendLine("<table border='1'>");
        sb.AppendLine("<tr><th>#</th><th>SKU</th><th>Name</th><th>Category</th><th>Barcode</th><th>Visual Class</th><th>Image Hint</th></tr>");

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine("<tr>" +
                $"<td>{i + 1}</td>" +
                $"<td>{Html(item.sku)}</td>" +
                $"<td>{Html(item.name)}</td>" +
                $"<td>{Html(item.category)}</td>" +
                $"<td>{Html(item.barcode)}</td>" +
                $"<td>{Html(item.visual_class)}</td>" +
                $"<td>{Html(item.image_hint)}</td>" +
                "</tr>");
        }

        sb.AppendLine("</table></body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private void WriteSimplePdf(string path, string clientName, List<CatalogItem> items)
    {
        // Lightweight PDF writer without external packages. Good for mobile export reports.
        var lines = new List<string>();
        lines.Add("Client Products Report");
        lines.Add("Client: " + clientName);
        lines.Add("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        lines.Add("Total Products: " + items.Count);
        lines.Add("------------------------------------------------------------");

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            lines.Add($"{i + 1}. SKU: {Safe(it.sku)} | Name: {Safe(it.name)} | Category: {Safe(it.category)}");
            lines.Add($"   Barcode: {Safe(it.barcode)} | Visual: {Safe(it.visual_class)} | Hint: {Safe(it.image_hint)}");
        }

        string streamText = BuildPdfTextStream(lines);
        byte[] streamBytes = Encoding.ASCII.GetBytes(streamText);

        var pdf = new StringBuilder();
        var offsets = new List<int>();
        Action<string> add = s => pdf.Append(s);

        add("%PDF-1.4\n");
        offsets.Add(pdf.Length); add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets.Add(pdf.Length); add("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        offsets.Add(pdf.Length); add("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");
        offsets.Add(pdf.Length); add($"4 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n{streamText}endstream\nendobj\n");
        offsets.Add(pdf.Length); add("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        int xref = pdf.Length;
        add("xref\n0 6\n0000000000 65535 f \n");
        foreach (int off in offsets)
            add(off.ToString("0000000000") + " 00000 n \n");
        add("trailer\n<< /Root 1 0 R /Size 6 >>\nstartxref\n" + xref + "\n%%EOF");

        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }

    private string BuildPdfTextStream(List<string> lines)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n/F1 10 Tf\n50 800 Td\n14 TL\n");

        int maxLines = Mathf.Min(lines.Count, 52); // one-page lightweight report
        for (int i = 0; i < maxLines; i++)
        {
            string line = EscapePdf(TrimTo(lines[i], 95));
            sb.Append($"({line}) Tj\nT*\n");
        }

        if (lines.Count > maxLines)
            sb.Append($"(... Report truncated on PDF preview. Excel file contains all {lines.Count} lines.) Tj\n");

        sb.Append("ET\n");
        return sb.ToString();
    }

    private string Html(string value)
    {
        return Safe(value).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private string EscapePdf(string value)
    {
        return Safe(value).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private string Safe(string value) => string.IsNullOrEmpty(value) ? "" : value;

    private string TrimTo(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value.Substring(0, max - 3) + "...";
    }

    private string MakeSafeFileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Client";
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace(' ', '_');
    }
}

public class ExportResult
{
    public bool success;
    public string message;
    public string excelPath;
    public string pdfPath;

    public static ExportResult Ok(string message, string excelPath, string pdfPath)
    {
        return new ExportResult { success = true, message = message, excelPath = excelPath, pdfPath = pdfPath };
    }

    public static ExportResult Fail(string message)
    {
        return new ExportResult { success = false, message = message };
    }
}
