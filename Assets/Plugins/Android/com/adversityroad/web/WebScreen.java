package com.adversityroad.web;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

import com.unity3d.player.UnityPlayer;

/**
 * A web page drawn on top of the Unity view, used as the picture of the in-game
 * television set. All calls come from C# (AndroidJavaClass.CallStatic).
 *
 * This file is deliberately ASCII-only: javac's default encoding on the build
 * machine is not guaranteed to be UTF-8, and one non-ASCII byte is a hard build
 * failure there. Every player facing string is passed in from C#.
 *
 * WHY AN OVERLAY AND NOT A TEXTURE
 * The only way to show YouTube inside an app is a WebView (extracting the media
 * stream and feeding it to a video texture breaks YouTube's terms and rots within
 * weeks). A WebView renders into the Android view hierarchy, not into a GL texture
 * we could sample from a shader, so the picture has to be a view placed exactly on
 * top of the rectangle the TV screen occupies on the display. C# projects the four
 * corners of the screen quad every frame and calls place(); when the player is not
 * roughly in front of the set, C# calls hide() and the in-world screen shows its own
 * procedural picture instead.
 *
 * WHY TOUCHES PASS THROUGH
 * The overlay can cover most of the display when the player stands close to a five
 * metre TV. If the WebView consumed touches, the player would lose the joystick and
 * be stuck. PassThrough intercepts every touch (so the page never sees it) and then
 * declines it (so Android keeps dispatching to the Unity view underneath). Playback
 * control therefore lives entirely in the in-game panel, which drives the page's own
 * <video> element through evaluateJavascript (see VIDEO below).
 */
public class WebScreen {

    /**
     * How a video is controlled once it plays.
     *
     * We load https://www.youtube.com/embed/<id> DIRECTLY instead of hand building a
     * page around the IFrame API. A hand built page has to be handed to the WebView
     * through loadDataWithBaseURL("https://www.youtube.com", ...), which only pretends
     * to come from youtube.com: the origin cannot be verified, and YouTube answers a
     * good part of the catalogue with "this video cannot be watched here" - which is
     * exactly what the player saw. The real embed page has a real origin and plays.
     *
     * The price is that the IFrame JS API is gone. It is not needed: the embed page IS
     * the player page, so its own <video> element sits in the document evaluateJavascript
     * runs in, and play / pause / mute are plain DOM calls.
     */
    private static final String VIDEO = "document.querySelector('video')";

    private static WebView sWeb;
    private static PassThrough sHost;
    private static boolean sReady;      // the player page has finished loading

    /** Container that steals touches from the page and then declines them. */
    private static class PassThrough extends FrameLayout {
        PassThrough(Context c) { super(c); }
        @Override public boolean onInterceptTouchEvent(MotionEvent e) { return true; }
        @Override public boolean onTouchEvent(MotionEvent e) { return false; }
    }

    public static boolean available() {
        return UnityPlayer.currentActivity != null;
    }

    /**
     * Show / move the overlay. x,y,w,h are in Unity's screen pixels (origin top left),
     * srcW/srcH is the size of Unity's screen those numbers were measured against.
     *
     * The rectangle MUST be rescaled here: Unity's surface and the Android view
     * hierarchy do not have to be the same number of pixels (resolution scaling, a
     * display cutout, split screen, picture in picture...). Placing Unity pixels
     * straight into view coordinates puts the picture next to the television instead
     * of on it, shrinking or growing with the mismatch.
     */
    public static void place(final int x, final int y, final int w, final int h,
                             final int srcW, final int srcH) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (!ensure(a)) return;
                float sx = 1f, sy = 1f;
                View content = a.findViewById(android.R.id.content);
                if (content != null && srcW > 0 && srcH > 0
                        && content.getWidth() > 0 && content.getHeight() > 0) {
                    sx = (float) content.getWidth() / (float) srcW;
                    sy = (float) content.getHeight() / (float) srcH;
                }
                FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(
                    Math.max(4, Math.round(w * sx)), Math.max(4, Math.round(h * sy)));
                lp.leftMargin = Math.round(x * sx);
                lp.topMargin = Math.round(y * sy);
                sHost.setLayoutParams(lp);
                if (sHost.getVisibility() != View.VISIBLE) sHost.setVisibility(View.VISIBLE);
                sHost.requestLayout();
            }
        });
    }

    /**
     * Take the picture off the display. Playback is NOT stopped: this is what makes
     * "keep playing while I walk away" (and background audio) work. Use pause() to
     * actually stop the sound.
     */
    public static void hide() {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (sHost != null) sHost.setVisibility(View.GONE);
            }
        });
    }

    /** Load a YouTube video by its 11 character id. */
    public static void playYouTube(final String videoId, final boolean muted) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null || videoId == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (!ensure(a)) return;
                sReady = false;
                // loop=1 needs playlist=<same id> - that is YouTube's own rule for
                // looping a single video. controls=0 because touches never reach the
                // page anyway (see PassThrough); the game panel is the remote.
                String url = "https://www.youtube.com/embed/" + videoId
                    + "?autoplay=1&playsinline=1&rel=0&modestbranding=1&controls=0"
                    + "&iv_load_policy=3&loop=1&playlist=" + videoId
                    + (muted ? "&mute=1" : "");
                sWeb.loadUrl(url);
            }
        });
    }

    /** Load any http(s) page directly (a plain .mp4 link, a live stream page...). */
    public static void loadUrl(final String url) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null || url == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (!ensure(a)) return;
                sReady = false;
                sWeb.loadUrl(url);
            }
        });
    }

    public static void play() { post("var v=" + VIDEO + ";if(v)v.play();"); }

    public static void pause() { post("var v=" + VIDEO + ";if(v)v.pause();"); }

    public static void mute(final boolean m) {
        post("var v=" + VIDEO + ";if(v)v.muted=" + (m ? "true" : "false") + ";");
    }

    /**
     * Ask the page whether anything is actually playing and report back to Unity via
     * UnitySendMessage(callbackObject, "OnWebPlayback", "1" or "0").
     *
     * Some videos forbid embedding; the page then shows YouTube's own "cannot be
     * watched here" card and nothing ever plays. Without this the game has no way to
     * tell that apart from "still buffering", and the player is left staring at a box.
     */
    public static void probe(final String callbackObject) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null || callbackObject == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (sWeb == null) {
                    send(callbackObject, "0");
                    return;
                }
                try {
                    sWeb.evaluateJavascript(
                        "(function(){var v=" + VIDEO
                        + ";return (v&&v.readyState>0)?'1':'0';})()",
                        new ValueCallback<String>() {
                            public void onReceiveValue(String value) {
                                send(callbackObject,
                                    value != null && value.contains("1") ? "1" : "0");
                            }
                        });
                } catch (Throwable t) {
                    send(callbackObject, "0");
                }
            }
        });
    }

    private static void send(String obj, String value) {
        try {
            UnityPlayer.UnitySendMessage(obj, "OnWebPlayback", value);
        } catch (Throwable t) {
            // the bridge object is gone; nothing to report to
        }
    }

    /**
     * Keep the page running while the app is in the background. Android does not
     * pause a WebView's media on its own - the app has to - so all we must do is
     * make sure timers keep ticking after Unity's own pause handling.
     */
    public static void keepAlive() {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (sWeb == null) return;
                sWeb.resumeTimers();
                sWeb.onResume();
            }
        });
    }

    /** Tear the overlay down completely (leaving a level, shutting the set off). */
    public static void close() {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (sWeb != null) {
                    sWeb.loadUrl("about:blank");
                    sWeb.stopLoading();
                }
                if (sHost != null) {
                    ViewGroup parent = (ViewGroup) sHost.getParent();
                    if (parent != null) parent.removeView(sHost);
                }
                if (sWeb != null) sWeb.destroy();
                sWeb = null;
                sHost = null;
                sReady = false;
            }
        });
    }

    /** Hand the link to whatever app the player has (YouTube app, browser). */
    public static void openExternal(String url) {
        Activity a = UnityPlayer.currentActivity;
        if (a == null || url == null) return;
        try {
            Intent i = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            a.startActivity(i);
        } catch (Throwable t) {
            // no app can open it - the in-game panel already said what is playing
        }
    }

    // ================= internals (UI thread only) =================

    private static boolean ensure(Activity a) {
        if (sHost != null && sWeb != null) return true;
        try {
            sWeb = new WebView(a);
            WebSettings s = sWeb.getSettings();
            s.setJavaScriptEnabled(true);
            s.setDomStorageEnabled(true);
            // Without this the page needs a tap before it may play - and taps never
            // reach the page (see PassThrough), so the TV would stay black forever.
            s.setMediaPlaybackRequiresUserGesture(false);
            s.setUseWideViewPort(true);
            s.setLoadWithOverviewMode(true);
            sWeb.setBackgroundColor(Color.BLACK);
            sWeb.setWebChromeClient(new WebChromeClient());
            sWeb.setWebViewClient(new WebViewClient() {
                @Override public void onPageFinished(WebView v, String url) {
                    sReady = true;
                }
            });
            sWeb.setLayoutParams(new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

            sHost = new PassThrough(a);
            sHost.setBackgroundColor(Color.BLACK);
            sHost.addView(sWeb);
            sHost.setVisibility(View.GONE);
            a.addContentView(sHost, new FrameLayout.LayoutParams(4, 4));
            return true;
        } catch (Throwable t) {
            sWeb = null;
            sHost = null;
            return false;
        }
    }

    private static void post(final String code) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() { js(code); }
        });
    }

    private static void js(String code) {
        if (sWeb == null) return;
        try {
            sWeb.evaluateJavascript(code, null);
        } catch (Throwable t) {
            // page not loaded yet; the next call will land
        }
    }
}
