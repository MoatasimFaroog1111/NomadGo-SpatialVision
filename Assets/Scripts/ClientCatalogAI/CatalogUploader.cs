using System.IO;
using UnityEngine;

public class CatalogUploader : MonoBehaviour
{
    public void ImportFromPath(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            Debug.LogError("File not found");
            return;
        }

        string dest = Path.Combine(Application.persistentDataPath, "client_catalog.json");

        File.Copy(sourcePath, dest, true);

        Debug.Log("[Catalog] Uploaded!");

        ClientCatalogManager.Instance.Load();
    }
}