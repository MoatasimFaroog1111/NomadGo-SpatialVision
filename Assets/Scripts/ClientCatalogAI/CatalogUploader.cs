using System.IO;
using UnityEngine;

public class CatalogUploader : MonoBehaviour
{
    public static CatalogUploader Instance;

    private void Awake()
    {
        Instance = this;
    }

    public void PickCatalogFile()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var pickerClass = new AndroidJavaClass("com.nomadgo.spatialvision.CatalogFilePickerActivity"))
        using (var intent = new AndroidJavaObject("android.content.Intent", activity, pickerClass))
        {
            activity.Call("startActivity", intent);
        }
#else
        Debug.Log("[CatalogUploader] File picker works only on Android device.");
#endif
    }

    public void OnCatalogImported(string message)
    {
        Debug.Log("[CatalogUploader] " + message);

        if (ClientCatalogManager.Instance != null)
            ClientCatalogManager.Instance.Load();
    }

    public void OnCatalogImportFailed(string message)
    {
        Debug.LogError("[CatalogUploader] Import failed: " + message);
    }

    public void ImportFromPath(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            Debug.LogError("[CatalogUploader] File not found: " + sourcePath);
            return;
        }

        string dest = Path.Combine(Application.persistentDataPath, "client_catalog.json");
        File.Copy(sourcePath, dest, true);

        Debug.Log("[CatalogUploader] Catalog uploaded to: " + dest);

        if (ClientCatalogManager.Instance != null)
            ClientCatalogManager.Instance.Load();
    }
}
