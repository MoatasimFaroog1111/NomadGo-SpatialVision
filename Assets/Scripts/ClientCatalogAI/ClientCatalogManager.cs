using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class ClientCatalogManager : MonoBehaviour
{
    public static ClientCatalogManager Instance;

    [SerializeField] private ClientCatalog catalog;
    private string path;

    public bool IsLoaded { get; private set; } = false;
    public int ItemsCount => catalog?.items?.Count ?? 0;
    public string ClientName => string.IsNullOrEmpty(catalog?.client_name) ? "Unknown Client" : catalog.client_name;

    private void Awake()
    {
        Instance = this;
        path = Path.Combine(Application.persistentDataPath, "client_catalog.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(path))
        {
            IsLoaded = false;
            Debug.LogWarning("[Catalog] No file found: " + path);
            return;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            catalog = JsonUtility.FromJson<ClientCatalog>(json);
            IsLoaded = catalog != null && catalog.items != null && catalog.items.Count > 0;
            Debug.Log("[Catalog] Loaded " + ItemsCount + " items.");
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            Debug.LogError("[Catalog] Failed to load JSON: " + ex);
        }
    }

    public CatalogItem[] GetItems()
    {
        if (catalog == null || catalog.items == null) return new CatalogItem[0];
        return catalog.items.ToArray();
    }

    public CatalogItem MatchByVisual(string detectedClass)
    {
        if (catalog?.items == null || string.IsNullOrEmpty(detectedClass)) return null;

        string detected = Normalize(detectedClass);
        return catalog.items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.visual_class) &&
            (Normalize(i.visual_class) == detected || detected.Contains(Normalize(i.visual_class)) || Normalize(i.visual_class).Contains(detected))
        );
    }

    public CatalogItem MatchByBarcode(string code)
    {
        if (catalog?.items == null || string.IsNullOrEmpty(code)) return null;
        return catalog.items.FirstOrDefault(i => !string.IsNullOrEmpty(i.barcode) && i.barcode.Trim() == code.Trim());
    }

    public string BuildReportText()
    {
        Load();
        if (!IsLoaded) return "Catalog not loaded.\nPlease upload client products file first.";

        var sb = new StringBuilder();
        sb.AppendLine("CLIENT PRODUCTS REPORT");
        sb.AppendLine("Client: " + ClientName);
        sb.AppendLine("Total Products: " + ItemsCount);
        sb.AppendLine("------------------------------");

        for (int i = 0; i < catalog.items.Count; i++)
        {
            var item = catalog.items[i];
            sb.AppendLine((i + 1) + ". " + Safe(item.name));
            sb.AppendLine("SKU: " + Safe(item.sku));
            sb.AppendLine("Category: " + Safe(item.category));
            sb.AppendLine("Barcode: " + Safe(item.barcode));
            sb.AppendLine("Visual Class: " + Safe(item.visual_class));
            sb.AppendLine("Hint: " + Safe(item.image_hint));
            sb.AppendLine("------------------------------");
        }
        return sb.ToString();
    }

    private string Safe(string value) => string.IsNullOrEmpty(value) ? "-" : value;

    private string Normalize(string value)
    {
        return value.ToLowerInvariant().Trim().Replace(" ", "").Replace("_", "").Replace("-", "");
    }
}
