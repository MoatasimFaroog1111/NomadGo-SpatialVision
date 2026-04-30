using System.IO;
using System.Linq;
using UnityEngine;

public class ClientCatalogManager : MonoBehaviour
{
    public static ClientCatalogManager Instance;

    private ClientCatalog catalog;
    private string path;

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
            catalog = new ClientCatalog { items = new System.Collections.Generic.List<CatalogItem>() };
            return;
        }

        string json = File.ReadAllText(path);
        catalog = JsonUtility.FromJson<ClientCatalog>(json);

        Debug.Log($"[Catalog] Loaded {catalog.items.Count} items.");
    }

    public CatalogItem MatchByVisual(string detectedClass)
    {
        if (catalog?.items == null) return null;

        return catalog.items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.visual_class) &&
            i.visual_class.ToLower() == detectedClass.ToLower()
        );
    }

    public CatalogItem MatchByBarcode(string code)
    {
        if (catalog?.items == null) return null;

        return catalog.items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.barcode) &&
            i.barcode == code
        );
    }
}