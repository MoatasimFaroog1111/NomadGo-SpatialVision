package com.nomadgo.spatialvision;

import android.app.Activity;
import android.os.Bundle;
import android.content.Intent;
import android.net.Uri;
import java.io.InputStream;
import java.io.FileOutputStream;
import java.io.File;
import com.unity3d.player.UnityPlayer;

public class CatalogFilePickerActivity extends Activity {
    private static final int PICK_JSON = 9001;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("*/*");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {
            "application/json",
            "text/plain",
            "application/octet-stream"
        });

        startActivityForResult(intent, PICK_JSON);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);

        if (requestCode != PICK_JSON || resultCode != RESULT_OK || data == null || data.getData() == null) {
            UnityPlayer.UnitySendMessage("CatalogSystem", "OnCatalogImportFailed", "No file selected");
            finish();
            return;
        }

        try {
            Uri uri = data.getData();

            InputStream input = getContentResolver().openInputStream(uri);
            File outFile = new File(getFilesDir(), "client_catalog.json");
            FileOutputStream output = new FileOutputStream(outFile, false);

            byte[] buffer = new byte[8192];
            int len;

            while ((len = input.read(buffer)) > 0) {
                output.write(buffer, 0, len);
            }

            output.close();
            input.close();

            UnityPlayer.UnitySendMessage("CatalogSystem", "OnCatalogImported", "client_catalog.json imported successfully");
        } catch (Exception e) {
            UnityPlayer.UnitySendMessage("CatalogSystem", "OnCatalogImportFailed", e.toString());
        }

        finish();
    }
}
