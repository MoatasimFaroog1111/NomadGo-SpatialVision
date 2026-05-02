private void EnsureCatalogSystem()
{
    var existing = GameObject.Find("CatalogSystem");

    if (existing == null)
    {
        existing = new GameObject("CatalogSystem");
        Debug.Log("[Catalog] Created CatalogSystem object");
    }

    // 🔥 تأكد من الاسم (مهم جدًا)
    existing.name = "CatalogSystem";

    if (existing.GetComponent<global::ClientCatalogManager>() == null)
        existing.AddComponent<global::ClientCatalogManager>();

    if (existing.GetComponent<global::CatalogUploader>() == null)
        existing.AddComponent<global::CatalogUploader>();

    DontDestroyOnLoad(existing);
}
