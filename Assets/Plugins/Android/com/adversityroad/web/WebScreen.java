package com.adversityroad.web;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
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
 * control therefore lives entirely in the in-game panel, which talks to the page
 * through the YouTube IFrame API functions defined in PAGE below.
 */
public class WebScreen {

    /** Player page: the YouTube IFrame API plus the four controls C# needs. */
    private static final String PAGE =
        "<!DOCTYPE html><html><head>" +
        "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
        "<style>html,body{margin:0;padding:0;background:#000;overflow:hidden}" +
        "#p{width:100vw;height:100vh}</style></head><body><div id='p'></div>" +
        "<script src='https://www.youtube.com/iframe_api'></script><script>" +
        "var pl=null,want='__ID__',mute=__MUTE__;" +
        "function onYouTubeIframeAPIReady(){pl=new YT.Player('p',{videoId:want," +
        "playerVars:{autoplay:1,playsinline:1,rel:0,modestbranding:1,controls:0,fs:0}," +
        "events:{onReady:function(e){if(mute)e.target.mute();e.target.playVideo();}," +
        "onStateChange:function(e){if(e.data==YT.PlayerState.ENDED)e.target.playVideo();}}});}" +
        "function arLoad(id){want=id;if(pl){pl.loadVideoById(id);pl.playVideo();}}" +
        "function arPlay(){if(pl)pl.playVideo();}" +
        "function arPause(){if(pl)pl.pauseVideo();}" +
        "function arMute(m){mute=m;if(pl){if(m)pl.mute();else pl.unMute();}}" +
        "</script></body></html>";

    private static WebView sWeb;
    private static PassThrough sHost;
    private static boolean sReady;      // the player page has finished loading
    private static String sPending;     // video id asked for before the page was ready

    /** Container that steals touches from the page and then declines them. */
    private static class PassThrough extends FrameLayout {
        PassThrough(Context c) { super(c); }
        @Override public boolean onInterceptTouchEvent(MotionEvent e) { return true; }
        @Override public boolean onTouchEvent(MotionEvent e) { return false; }
    }

    public static boolean available() {
        return UnityPlayer.currentActivity != null;
    }

    /** Show / move the overlay. x,y are display pixels from the top left corner. */
    public static void place(final int x, final int y, final int w, final int h) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (!ensure(a)) return;
                FrameLayout.LayoutParams lp =
                    new FrameLayout.LayoutParams(Math.max(4, w), Math.max(4, h));
                lp.leftMargin = x;
                lp.topMargin = y;
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
                if (sReady) {
                    js("arLoad('" + videoId + "')");
                    js("arMute(" + (muted ? "true" : "false") + ")");
                    return;
                }
                sPending = videoId;
                String page = PAGE.replace("__ID__", videoId)
                                  .replace("__MUTE__", muted ? "true" : "false");
                // The youtube.com base URL matters: the IFrame API refuses to run
                // from an about:blank / file:// origin, which is why a naive
                // loadData() shows nothing but a black box.
                sWeb.loadDataWithBaseURL("https://www.youtube.com", page,
                    "text/html", "utf-8", null);
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
                sPending = null;
                sWeb.loadUrl(url);
            }
        });
    }

    public static void play() { post("arPlay()"); }

    public static void pause() { post("arPause()"); }

    public static void mute(final boolean m) { post("arMute(" + (m ? "true" : "false") + ")"); }

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
                sPending = null;
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
                    if (sPending != null) {
                        js("arLoad('" + sPending + "')");
                        sPending = null;
                    }
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
