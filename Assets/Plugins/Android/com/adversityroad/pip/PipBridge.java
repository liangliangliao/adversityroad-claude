package com.adversityroad.pip;

import android.app.Activity;
import android.app.Fragment;
import android.app.PictureInPictureParams;
import android.content.res.Configuration;
import android.os.Build;
import android.util.Rational;

import com.unity3d.player.UnityPlayer;

/**
 * Picture in picture: shrink the running game into the floating window Android
 * draws on top of whatever the player does next.
 *
 * ASCII-only on purpose (see GalleryPicker for why).
 *
 * Two halves:
 *   enter()  - ask Android for the floating window. Needs
 *              android:supportsPictureInPicture on the activity, which this plugin
 *              contributes through Assets/Plugins/Android/adversityroad.androidlib.
 *              Without that attribute the call throws and PiP is simply unavailable.
 *   the Fragment - the only way to be told the moment the window opens or closes.
 *              A headless Fragment does not have to be declared in the manifest,
 *              and android.app.Fragment gets onPictureInPictureModeChanged for free.
 *              C# uses that to fold the touch controls and the HUD away: a 480x270
 *              floating window has no room for a joystick, and taps do not reach a
 *              PiP window anyway.
 */
public class PipBridge extends Fragment {

    private static final String TAG = "ArPipBridge";
    private static final String METHOD = "OnPipChanged";
    private static String sCallbackObject = "PipBridge";
    private static PipBridge sInstance;

    /** Install the listener fragment. Safe to call more than once. */
    public static void attach(String callbackObject) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        if (callbackObject != null && callbackObject.length() > 0) {
            sCallbackObject = callbackObject;
        }
        if (sInstance != null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (sInstance != null) return;
                try {
                    sInstance = new PipBridge();
                    a.getFragmentManager().beginTransaction().add(sInstance, TAG).commit();
                } catch (Throwable t) {
                    sInstance = null;   // polling from C# still covers the state
                }
            }
        });
    }

    public static boolean supported() {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            && UnityPlayer.currentActivity != null;
    }

    /**
     * Ask for the floating window with the given aspect ratio. Android only grants
     * this while the activity is in the foreground, so call it from a button, not
     * from a pause handler.
     */
    public static boolean enter(final int num, final int den) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null || !supported()) return false;
        a.runOnUiThread(new Runnable() {
            public void run() {
                try {
                    PictureInPictureParams.Builder b = new PictureInPictureParams.Builder();
                    // Android only accepts ratios between 1:2.39 and 2.39:1.
                    b.setAspectRatio(new Rational(clamp(num, den), den));
                    a.enterPictureInPictureMode(b.build());
                } catch (Throwable t) {
                    // manifest attribute missing, or the device / policy said no
                }
            }
        });
        return true;
    }

    public static boolean inPip() {
        Activity a = UnityPlayer.currentActivity;
        if (a == null || Build.VERSION.SDK_INT < Build.VERSION_CODES.N) return false;
        try {
            return a.isInPictureInPictureMode();
        } catch (Throwable t) {
            return false;
        }
    }

    private static int clamp(int num, int den) {
        if (den <= 0) return 16;
        double max = 2.38 * den;
        double min = den / 2.38;
        if (num > max) return (int) max;
        if (num < min) return (int) Math.ceil(min);
        return num;
    }

    private void notifyUnity(boolean isIn) {
        try {
            UnityPlayer.UnitySendMessage(sCallbackObject, METHOD, isIn ? "1" : "0");
        } catch (Throwable t) {
            // the bridge object is gone; C# polling picks the state up
        }
    }

    @Override
    public void onPictureInPictureModeChanged(boolean isInPictureInPictureMode) {
        super.onPictureInPictureModeChanged(isInPictureInPictureMode);
        notifyUnity(isInPictureInPictureMode);
    }

    @Override
    public void onPictureInPictureModeChanged(boolean isInPictureInPictureMode,
                                              Configuration newConfig) {
        super.onPictureInPictureModeChanged(isInPictureInPictureMode, newConfig);
        notifyUnity(isInPictureInPictureMode);
    }
}
