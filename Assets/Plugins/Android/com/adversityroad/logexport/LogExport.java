package com.adversityroad.logexport;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.DocumentsContract;

import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;

import com.unity3d.player.UnityPlayer;

/**
 * Lets the player choose ANY folder for the debug log, and copies the log there.
 *
 * WHY THIS EXISTS
 * Application.persistentDataPath on Android is /storage/emulated/0/Android/data/<pkg>/files.
 * Since Android 11 that directory is hidden from file managers by scoped storage, so the
 * player literally cannot reach the file. Writing to /sdcard/Download directly is also
 * blocked on API 29+ without legacy storage. The supported way to hand a file to the user
 * is the Storage Access Framework: the user picks a folder once, the app gets a persistable
 * write grant for exactly that folder, and no runtime permission is involved at all.
 *
 * NOTE: this file is deliberately ASCII-only. javac's default encoding on the build machine
 * is not guaranteed to be UTF-8, and a non-ASCII byte in a source file is a hard build
 * failure there. All user facing strings are passed in from C#.
 *
 * Three entry points, all called from C# via AndroidJavaClass.CallStatic:
 *   pickFolder(callbackObject, chooserTitle) - open the system folder picker; the chosen
 *       tree URI arrives at UnitySendMessage(callbackObject, "OnLogFolderPicked", uri).
 *   exportFile(treeUri, srcPath, displayName, mime) - copy a local file into that folder,
 *       overwriting a document of the same name. Returns the document URI, or "" on failure.
 *   folderLabel(treeUri) - a short human readable name for the chosen folder (UI only).
 *
 * Why a headless Fragment (same as GalleryPicker): a Fragment needs no manifest entry,
 * so this plugin never touches AndroidManifest.xml.
 */
public class LogExport extends Fragment {

    private static final int REQUEST_CODE = 45092;
    private static final String METHOD = "OnLogFolderPicked";
    private static String sCallbackObject = "MoveLoggerBridge";
    private static String sChooserTitle = "Choose a folder for the log";

    // ---------------------------------------------------------------- pick

    /** Open the system folder picker. callbackObject = Unity GameObject to answer. */
    public static void pickFolder(String callbackObject, String chooserTitle) {
        if (callbackObject != null && callbackObject.length() > 0) {
            sCallbackObject = callbackObject;
        }
        if (chooserTitle != null && chooserTitle.length() > 0) {
            sChooserTitle = chooserTitle;
        }
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null || Build.VERSION.SDK_INT < 21) {
            UnityPlayer.UnitySendMessage(sCallbackObject, METHOD, "");
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    LogExport fragment = new LogExport();
                    activity.getFragmentManager()
                            .beginTransaction()
                            .add(fragment, "AdversityRoadLogExport")
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
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                    | Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                    | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
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
            // Persist the grant so the folder still works after the app restarts.
            // Without this the URI is only valid for this process lifetime, and the
            // player would have to re-pick the folder every single session.
            try {
                getActivity().getContentResolver().takePersistableUriPermission(uri,
                        Intent.FLAG_GRANT_READ_URI_PERMISSION
                                | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
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

    // -------------------------------------------------------------- export

    /**
     * Copy srcPath into the chosen folder as displayName, replacing any existing
     * document with that name. Returns the document URI, or "" if anything failed.
     */
    public static String exportFile(String treeUriString, String srcPath,
                                    String displayName, String mime) {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null || treeUriString == null || srcPath == null
                || displayName == null || Build.VERSION.SDK_INT < 21) {
            return "";
        }
        InputStream in = null;
        OutputStream out = null;
        try {
            Uri tree = Uri.parse(treeUriString);
            String docId = DocumentsContract.getTreeDocumentId(tree);
            Uri dir = DocumentsContract.buildDocumentUriUsingTree(tree, docId);

            // Same name twice would otherwise create "log (1).csv", "log (2).csv"...
            // Delete first so re-exporting the same run always lands on one file.
            Uri existing = findChild(activity, tree, dir, displayName);
            if (existing != null) {
                try {
                    DocumentsContract.deleteDocument(activity.getContentResolver(), existing);
                } catch (Exception ignored) {
                }
            }
            Uri doc = DocumentsContract.createDocument(activity.getContentResolver(), dir,
                    mime == null || mime.length() == 0 ? "text/csv" : mime, displayName);
            if (doc == null) {
                return "";
            }
            in = new FileInputStream(srcPath);
            out = activity.getContentResolver().openOutputStream(doc);
            if (out == null) {
                return "";
            }
            byte[] buffer = new byte[65536];
            int read;
            while ((read = in.read(buffer)) > 0) {
                out.write(buffer, 0, read);
            }
            out.flush();
            return doc.toString();
        } catch (Exception e) {
            return "";
        } finally {
            try { if (in != null) in.close(); } catch (Exception ignored) { }
            try { if (out != null) out.close(); } catch (Exception ignored) { }
        }
    }

    private static Uri findChild(Activity activity, Uri tree, Uri dir, String displayName) {
        android.database.Cursor c = null;
        try {
            Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(tree,
                    DocumentsContract.getDocumentId(dir));
            c = activity.getContentResolver().query(children, new String[] {
                    DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                    DocumentsContract.Document.COLUMN_DISPLAY_NAME }, null, null, null);
            if (c == null) {
                return null;
            }
            while (c.moveToNext()) {
                if (displayName.equals(c.getString(1))) {
                    return DocumentsContract.buildDocumentUriUsingTree(tree, c.getString(0));
                }
            }
            return null;
        } catch (Exception e) {
            return null;
        } finally {
            try { if (c != null) c.close(); } catch (Exception ignored) { }
        }
    }

    /** Short readable name of the chosen folder, for the settings screen. */
    public static String folderLabel(String treeUriString) {
        if (treeUriString == null || treeUriString.length() == 0) {
            return "";
        }
        try {
            Uri tree = Uri.parse(treeUriString);
            String id = DocumentsContract.getTreeDocumentId(tree);
            if (id == null) {
                return treeUriString;
            }
            // Tree ids look like "primary:Download/logs" - the part after ':' is the path.
            int colon = id.indexOf(':');
            String path = colon >= 0 && colon + 1 < id.length() ? id.substring(colon + 1) : id;
            return path.length() == 0 ? "(root)" : path;
        } catch (Exception e) {
            return treeUriString;
        }
    }
}
