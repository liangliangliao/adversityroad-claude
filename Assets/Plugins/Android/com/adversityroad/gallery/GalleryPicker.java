package com.adversityroad.gallery;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;

import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

import com.unity3d.player.UnityPlayer;

/**
 * Opens the system image picker and reports the chosen content:// URI back to Unity.
 *
 * NOTE: this file is deliberately ASCII-only. javac's default encoding on the build
 * machine is not guaranteed to be UTF-8, and a non-ASCII byte in a source file is a
 * hard build failure there. All user facing strings are passed in from C#.
 *
 * Two entry points, both called from C# via AndroidJavaClass.CallStatic:
 *   pick(callbackObject, chooserTitle) - open the picker; the result arrives at
 *       UnitySendMessage(callbackObject, "OnGalleryPicked", uriOrEmptyString).
 *   copyToFile(uri, dstPath) - copy the content of a content:// URI to a plain file.
 *
 * Why ACTION_GET_CONTENT: it shows the system gallery UI the player already knows,
 * and it needs NO read-media permission at all (the user grants access to the single
 * item they pick). Querying MediaStore ourselves needs a runtime permission and draws
 * a list that is not the system gallery.
 *
 * Why a headless Fragment instead of an Activity: a Fragment does not have to be
 * declared in AndroidManifest.xml, so this plugin never touches the manifest.
 */
public class GalleryPicker extends Fragment {

    private static final int REQUEST_CODE = 45091;
    private static final String METHOD = "OnGalleryPicked";
    private static String sCallbackObject = "GalleryPickerBridge";
    private static String sChooserTitle = "Pick an image";

    /** Copy a content:// URI into a plain file. Works on every API level. */
    public static boolean copyToFile(String uriString, String dstPath) {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null || uriString == null || dstPath == null) {
            return false;
        }
        InputStream in = null;
        OutputStream out = null;
        try {
            Uri uri = Uri.parse(uriString);
            in = activity.getContentResolver().openInputStream(uri);
            if (in == null) {
                return false;
            }
            out = new FileOutputStream(dstPath);
            byte[] buffer = new byte[65536];
            int read;
            while ((read = in.read(buffer)) > 0) {
                out.write(buffer, 0, read);
            }
            out.flush();
            return true;
        } catch (Exception e) {
            return false;
        } finally {
            try { if (in != null) in.close(); } catch (Exception ignored) { }
            try { if (out != null) out.close(); } catch (Exception ignored) { }
        }
    }

    /** Open the system picker. callbackObject = name of the Unity GameObject to answer. */
    public static void pick(String callbackObject, String chooserTitle) {
        if (callbackObject != null && callbackObject.length() > 0) {
            sCallbackObject = callbackObject;
        }
        if (chooserTitle != null && chooserTitle.length() > 0) {
            sChooserTitle = chooserTitle;
        }
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            UnityPlayer.UnitySendMessage(sCallbackObject, METHOD, "");
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    GalleryPicker fragment = new GalleryPicker();
                    activity.getFragmentManager()
                            .beginTransaction()
                            .add(fragment, "AdversityRoadGalleryPicker")
                            .commitAllowingStateLoss();
                } catch (Exception e) {
                    UnityPlayer.UnitySendMessage(sCallbackObject, METHOD, "");
                }
            }
        });
    }

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        try {
            Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
            intent.setType("image/*");
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            startActivityForResult(Intent.createChooser(intent, sChooserTitle), REQUEST_CODE);
        } catch (Exception e) {
            finishWith("");
        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_CODE) {
            return;
        }
        String result = "";
        if (resultCode == Activity.RESULT_OK && data != null && data.getData() != null) {
            Uri uri = data.getData();
            // Best effort: keep read access across restarts. Not all providers allow it.
            try {
                getActivity().getContentResolver().takePersistableUriPermission(
                        uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
            } catch (Exception ignored) {
            }
            result = uri.toString();
        }
        finishWith(result);
    }

    private void finishWith(String result) {
        UnityPlayer.UnitySendMessage(sCallbackObject, METHOD, result == null ? "" : result);
        try {
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        } catch (Exception ignored) {
        }
    }
}
