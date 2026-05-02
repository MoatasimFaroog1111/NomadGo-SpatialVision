using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class ClientCatalogManager : MonoBehaviour
{
    public static ClientCatalogManager Instance;

    private ClientCatalog catalog;
    private string path;

    public bool IsLoaded { get; private set; } = false;
    public int ItemsCount => catalog?.items?.Count ?? 0;
    public string ClientName => string.IsNullOrEmpty(catalog?.client_name) ? "Client" : catalog.client_name;
    public string CatalogPath => path;

    void Awake()
    {
        Instance = this;
        path = Path.Combine(Application.persistentDataPath, "client_catalog.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning("[Catalog] No file found.");
            IsLoaded = false;
            catalog = null;
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            catalog = JsonUtility.FromJson<ClientCatalog>(json);

            IsLoaded = catalog != null && catalog.items != null && catalog.items.Count > 0;
            Debug.Log($"[Catalog] Loaded {ItemsCount} items.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Catalog] Failed to load JSON: " + ex.Message);
            IsLoaded = false;
            catalog = null;
        }
    }

    public List<CatalogItem> GetItems()
    {
        if (catalog?.items == null)
            return new List<CatalogItem>();

        return new List<CatalogItem>(catalog.items);
    }

    public CatalogItem MatchByVisual(string detectedClass)
    {
        if (catalog?.items == null)
            return null;

        return catalog.items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.visual_class) &&
            i.visual_class.ToLower() == detectedClass.ToLower()
        );
    }

    public CatalogItem MatchByBarcode(string code)
    {
        if (catalog?.items == null)
            return null;

        return catalog.items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.barcode) &&
            i.barcode == code
        );
    }
}
