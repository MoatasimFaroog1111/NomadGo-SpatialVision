using System.IO;
using System.Linq;
using UnityEngine;

public class ClientCatalogManager : MonoBehaviour
{
    public static ClientCatalogManager Instance;

    private ClientCatalog catalog;

    private string path;

    // 🔥 حالة الكتالوج
    public bool IsLoaded { get; private set; } = false;

    // 🔥 عدد العناصر
    public int ItemsCount => catalog?.items?.Count ?? 0;

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
            return;
        }

        try
        {
            string json = File.ReadAllText(path);

            catalog = JsonUtility.FromJson<ClientCatalog>(json);

            IsLoaded = catalog != null &&
                       catalog.items != null &&
                       catalog.items.Count > 0;

            Debug.Log($"[Catalog] Loaded {ItemsCount} items.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[Catalog] Failed to load JSON: " + ex.Message);
            IsLoaded = false;
        }
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
