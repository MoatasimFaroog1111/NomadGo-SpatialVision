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
        NotifyUI(null, "File picker works only after building and installing the Android APK.");
#endif
    }

    public void OnCatalogImported(string message)
    {
        Debug.Log("[CatalogUploader] SUCCESS: " + message);

        string path = Path.Combine(Application.persistentDataPath, "client_catalog.json");
        bool exists = File.Exists(path);

        Debug.Log("[CatalogUploader] File exists: " + exists + " | " + path);

        if (!exists)
        {
            NotifyUI(false, "Upload failed: file was not saved inside the app storage.");
            return;
        }

        if (ClientCatalogManager.Instance != null)
        {
            ClientCatalogManager.Instance.Load();

            if (ClientCatalogManager.Instance.IsLoaded)
            {
                NotifyUI(true, "Upload successful — products loaded: " + ClientCatalogManager.Instance.ItemsCount);
            }
            else
            {
                NotifyUI(false, "Upload completed, but file format is invalid or contains no products.");
            }
        }
        else
        {
            Debug.LogError("[CatalogUploader] Manager is NULL");
            NotifyUI(false, "Upload failed: catalog manager not found.");
        }
    }

    public void OnCatalogImportFailed(string message)
    {
        Debug.LogError("[CatalogUploader] Import failed: " + message);
        NotifyUI(false, "Upload failed: " + message);
    }

    public void ImportFromPath(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            Debug.LogError("[CatalogUploader] File not found: " + sourcePath);
            NotifyUI(false, "Upload failed: file not found.");
            return;
        }

        string dest = Path.Combine(Application.persistentDataPath, "client_catalog.json");
        File.Copy(sourcePath, dest, true);

        Debug.Log("[CatalogUploader] Catalog uploaded to: " + dest);

        if (ClientCatalogManager.Instance != null)
        {
            ClientCatalogManager.Instance.Load();
            NotifyUI(ClientCatalogManager.Instance.IsLoaded,
                ClientCatalogManager.Instance.IsLoaded
                    ? "Upload successful — products loaded: " + ClientCatalogManager.Instance.ItemsCount
                    : "Upload completed, but file format is invalid or contains no products.");
        }
    }

    private void NotifyUI(bool? success, string text)
    {
        var ui = NomadGo.AppShell.UIBuilder.Instance ?? FindObjectOfType<NomadGo.AppShell.UIBuilder>();
        if (ui != null)
            ui.SetCatalogUploadStatus(success, text);
    }
}
