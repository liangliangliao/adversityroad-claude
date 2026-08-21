package com.adversityroad.web;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.net.Uri;
import android.util.DisplayMetrics;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

import java.io.ByteArrayInputStream;
import java.util.HashMap;
import java.util.Map;

import com.unity3d.player.UnityPlayer;

/**
 * The picture of the in-game television: a web page drawn on top of the Unity view.
 * All calls come from C# (AndroidJavaClass.CallStatic).
 *
 * ASCII-only on purpose: javac's default encoding on the build machine is not
 * guaranteed to be UTF-8, and one non-ASCII byte is a hard build failure there.
 *
 * WHY AN OVERLAY AND NOT A TEXTURE
 * The only lawful way to show YouTube inside an app is a WebView (extracting the media
 * stream and feeding it to a video texture breaks YouTube's terms and rots within
 * weeks). A WebView renders into the Android view hierarchy, not into a GL texture we
 * could sample from a shader, so the picture has to be placed on top of the rectangle
 * the TV screen occupies on the display. C# projects the screen quad every frame and
 * calls place() only while that rectangle is standing still.
 *
 * WHY ITS OWN WINDOW (this is what fixed "sound plays but the picture is black")
 * Unity's activity is declared android:hardwareAccelerated="false" - Unity draws
 * through its own surface and does not need the view hierarchy accelerated. But an
 * HTML5 <video> element can only be composited on a hardware accelerated window; in a
 * software window Chromium plays the audio and leaves the video area black, which is
 * exactly what the player saw. An activity's window flag cannot be changed after the
 * window exists, so the WebView gets its own panel window instead, created with
 * FLAG_HARDWARE_ACCELERATED. That window is also NOT_TOUCHABLE, which is a cleaner way
 * to let every touch reach the game than intercepting them in a container view (the
 * overlay can cover most of the display; if it ate touches the player would lose the
 * joystick). Playback control therefore lives in the in-game panel.
 * If the panel window is refused for any reason, the code falls back to adding the
 * WebView to the activity's content view - the old behaviour, black video included.
 *
 * WHY park() INSTEAD OF setVisibility(GONE)
 * Chromium suspends media for a WebView that is not visible, so hiding the overlay that
 * way also killed the sound - including in picture in picture mode. Parking shrinks the
 * window to one pixel in the corner instead: still "visible" as far as the WebView is
 * concerned, so playback (and background audio) continues.
 */
public class WebScreen {

    /**
     * Two ways to get a YouTube video onto the screen, because YouTube refuses embeds
     * for several different reasons and only a device can tell which one applies. C#
     * starts with mode 0 and automatically retries with mode 1 when nothing plays a few
     * seconds later (see probe()).
     *
     * MODE 0 - our own player page served from a REAL https origin.
     *   The page is not fetched from a server: shouldInterceptRequest() answers the
     *   request for PAGE_URL with the HTML below, so the WebView believes the document
     *   really came from https://tv.adversityroad.app - an ordinary third party origin.
     *   Spoofing the origin with loadDataWithBaseURL("https://www.youtube.com", ...)
     *   does not work: an embed whose Referer is youtube.com itself is not a real embed
     *   and YouTube answers it with "this video cannot be watched here" (150 / 152).
     *
     * MODE 1 - load https://www.youtube.com/embed/<id> directly, WITH a Referer header.
     *   A WebView sends no Referer of its own, and YouTube answers a referer-less embed
     *   request with "error 153 - video player configuration error".
     *
     * Both modes drop the "; wv" marker from the User-Agent: that substring is how a
     * page can tell it is inside a WebView, and YouTube refuses part of its catalogue
     * when it sees it.
     */
    private static final String VIDEO = "document.querySelector('video')";

    /** Origin the player page claims to come from. Never actually fetched. */
    private static final String ORIGIN = "https://tv.adversityroad.app";
    private static final String PAGE_URL = ORIGIN + "/player.html";

    private static final String PAGE =
        "<!DOCTYPE html><html><head>" +
        "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
        "<style>html,body{margin:0;padding:0;background:#000;overflow:hidden}" +
        "#p{width:100vw;height:100vh}</style></head><body><div id='p'></div>" +
        "<script src='https://www.youtube.com/iframe_api'></script><script>" +
        "var pl=null,arErr=0,want='__ID__',mute=__MUTE__;" +
        "function onYouTubeIframeAPIReady(){pl=new YT.Player('p',{videoId:want," +
        "playerVars:{autoplay:1,playsinline:1,rel:0,controls:0,modestbranding:1,fs:0," +
        "iv_load_policy:3,origin:'__ORIGIN__'}," +
        "events:{onReady:function(e){if(mute)e.target.mute();e.target.playVideo();}," +
        "onError:function(e){arErr=e.data;}," +
        "onStateChange:function(e){if(e.data==0)e.target.playVideo();}}});}" +
        "function arPlay(){if(pl)pl.playVideo();}" +
        "function arPause(){if(pl)pl.pauseVideo();}" +
        "function arMute(m){if(pl){if(m)pl.mute();else pl.unMute();}}" +
        "function arState(){try{if(arErr!=0)return '0:'+arErr;" +
        "var s=pl?pl.getPlayerState():-9;return (s==1||s==3)?'1':'0';}catch(e){return '0';}}" +
        "</script></body></html>";

    private static WebView sWeb;
    private static FrameLayout sFallbackHost;     // only used if the panel window fails
    private static WindowManager sWm;
    private static WindowManager.LayoutParams sLp;
    private static boolean sWindowMode;
    private static boolean sReady;
    private static int sMode;
    private static String sPageHtml = "";

    /** Container used only by the fallback path: steals touches, then declines them. */
    private static class PassThrough extends FrameLayout {
        PassThrough(Context c) { super(c); }
        @Override public boolean onInterceptTouchEvent(MotionEvent e) { return true; }
        @Override public boolean onTouchEvent(MotionEvent e) { return false; }
    }

    public static boolean available() {
        return UnityPlayer.currentActivity != null;
    }

    /** True when the picture is on its own hardware accelerated window (video works). */
    public static boolean accelerated() {
        return sWindowMode;
    }

    /**
     * Show / move the picture. x,y,w,h are in Unity's screen pixels (origin top left),
     * srcW/srcH is the Unity screen size they were measured against.
     *
     * The rectangle is rescaled here because Unity's surface and the Android display do
     * not have to be the same number of pixels (resolution scaling, a display cutout,
     * split screen, picture in picture). Placing Unity pixels straight into window
     * coordinates puts the picture next to the television instead of on it.
     */
    public static void place(final int x, final int y, final int w, final int h,
                             final int srcW, final int srcH) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (!ensure(a)) return;
                float sx = 1f, sy = 1f;
                int refW = 0, refH = 0;
                if (sWindowMode) {
                    DisplayMetrics dm = new DisplayMetrics();
                    try {
                        a.getWindowManager().getDefaultDisplay().getRealMetrics(dm);
                        refW = dm.widthPixels;
                        refH = dm.heightPixels;
                    } catch (Throwable t) {
                        refW = refH = 0;
                    }
                } else {
                    View content = a.findViewById(android.R.id.content);
                    if (content != null) {
                        refW = content.getWidth();
                        refH = content.getHeight();
                    }
                }
                if (srcW > 0 && srcH > 0 && refW > 0 && refH > 0) {
                    sx = (float) refW / (float) srcW;
                    sy = (float) refH / (float) srcH;
                }
                int px = Math.round(x * sx), py = Math.round(y * sy);
                int pw = Math.max(4, Math.round(w * sx)), ph = Math.max(4, Math.round(h * sy));
                apply(a, px, py, pw, ph);
            }
        });
    }

    /**
     * Take the picture off the screen WITHOUT stopping playback: shrink it to a single
     * pixel in the corner. See the class comment for why this is not setVisibility(GONE).
     */
    public static void hide() {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (sWeb == null) return;
                apply(a, 0, 0, 1, 1);
            }
        });
    }

    /** Load a YouTube video by its 11 character id. mode: see the class comment. */
    public static void playYouTube(final String videoId, final boolean muted, final int mode) {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null || videoId == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                if (!ensure(a)) return;
                sReady = false;
                sMode = mode == 1 ? 1 : 0;
                if (sMode == 0) {
                    sPageHtml = PAGE.replace("__ID__", videoId)
                                    .replace("__MUTE__", muted ? "true" : "false")
                                    .replace("__ORIGIN__", ORIGIN);
                    sWeb.loadUrl(PAGE_URL);
                    return;
                }
                // loop=1 needs playlist=<same id> - YouTube's own rule for looping a
                // single video. controls=0 because touches never reach the page anyway.
                String url = "https://www.youtube.com/embed/" + videoId
                    + "?autoplay=1&playsinline=1&rel=0&modestbranding=1&controls=0"
                    + "&iv_load_policy=3&loop=1&playlist=" + videoId
                    + (muted ? "&mute=1" : "");
                Map<String, String> headers = new HashMap<String, String>();
                headers.put("Referer", "https://www.youtube.com/");
                sWeb.loadUrl(url, headers);
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
                sMode = 1;
                sWeb.loadUrl(url);
            }
        });
    }

    public static void play() {
        post(sMode == 0 ? "arPlay()" : "var v=" + VIDEO + ";if(v)v.play();");
    }

    public static void pause() {
        post(sMode == 0 ? "arPause()" : "var v=" + VIDEO + ";if(v)v.pause();");
    }

    public static void mute(final boolean m) {
        post(sMode == 0 ? ("arMute(" + (m ? "true" : "false") + ")")
                        : ("var v=" + VIDEO + ";if(v)v.muted=" + (m ? "true" : "false") + ";"));
    }

    /**
     * Ask the page whether anything is actually playing and report back to Unity via
     * UnitySendMessage(callbackObject, "OnWebPlayback", "1" / "0" / "0:<errorCode>").
     *
     * Some videos forbid embedding, some refuse the origin; the page then shows
     * YouTube's own error card and nothing plays. Without this the game cannot tell
     * that apart from "still buffering" and the player is left staring at a box.
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
                    String q = sMode == 0
                        ? "arState()"
                        : "(function(){var v=" + VIDEO
                          + ";return (v&&v.readyState>0&&!v.paused)?'1':'0';})()";
                    sWeb.evaluateJavascript(q, new ValueCallback<String>() {
                        public void onReceiveValue(String value) {
                            send(callbackObject, value == null ? "0" : value.replace("\"", ""));
                        }
                    });
                } catch (Throwable t) {
                    send(callbackObject, "0");
                }
            }
        });
    }

    /** Keep the page running while the app is in the background / in a floating window. */
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

    /** Tear the picture down completely (leaving home, shutting the set off). */
    public static void close() {
        final Activity a = UnityPlayer.currentActivity;
        if (a == null) return;
        a.runOnUiThread(new Runnable() {
            public void run() {
                try {
                    if (sWeb != null) {
                        sWeb.loadUrl("about:blank");
                        sWeb.stopLoading();
                        if (sWindowMode && sWm != null) {
                            sWm.removeViewImmediate(sWeb);
                        } else {
                            ViewGroup parent = sFallbackHost != null
                                ? (ViewGroup) sFallbackHost.getParent() : null;
                            if (parent != null) parent.removeView(sFallbackHost);
                        }
                        sWeb.destroy();
                    }
                } catch (Throwable t) {
                    // already detached
                }
                sWeb = null;
                sFallbackHost = null;
                sWm = null;
                sLp = null;
                sWindowMode = false;
                sReady = false;
                sPageHtml = "";
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

    private static void apply(Activity a, int x, int y, int w, int h) {
        try {
            if (sWindowMode && sWm != null && sLp != null) {
                sLp.x = x;
                sLp.y = y;
                sLp.width = w;
                sLp.height = h;
                sWm.updateViewLayout(sWeb, sLp);
                return;
            }
            if (sFallbackHost != null) {
                FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(w, h);
                lp.leftMargin = x;
                lp.topMargin = y;
                sFallbackHost.setLayoutParams(lp);
                sFallbackHost.setVisibility(View.VISIBLE);
                sFallbackHost.requestLayout();
            }
        } catch (Throwable t) {
            // the window went away under us; the next place() rebuilds it
        }
    }

    private static boolean ensure(Activity a) {
        if (sWeb != null) return true;
        try {
            sWeb = new WebView(a);
            WebSettings s = sWeb.getSettings();
            s.setJavaScriptEnabled(true);
            s.setDomStorageEnabled(true);
            // Without this the page needs a tap before it may play - and taps never
            // reach the page, so the TV would stay black forever.
            s.setMediaPlaybackRequiresUserGesture(false);
            s.setUseWideViewPort(true);
            s.setLoadWithOverviewMode(true);
            String ua = s.getUserAgentString();
            if (ua != null && ua.contains("; wv")) s.setUserAgentString(ua.replace("; wv", ""));

            sWeb.setBackgroundColor(Color.BLACK);
            sWeb.setLayerType(View.LAYER_TYPE_HARDWARE, null);
            sWeb.setWebChromeClient(new WebChromeClient());
            sWeb.setWebViewClient(new WebViewClient() {
                // Serve our own player page for PAGE_URL without any network request.
                // This is what gives the document a real (and stable) origin.
                @Override
                public WebResourceResponse shouldInterceptRequest(WebView v, WebResourceRequest req) {
                    try {
                        if (req != null && req.getUrl() != null
                                && PAGE_URL.equals(req.getUrl().toString())
                                && sPageHtml.length() > 0) {
                            return new WebResourceResponse("text/html", "utf-8",
                                new ByteArrayInputStream(sPageHtml.getBytes("UTF-8")));
                        }
                    } catch (Throwable t) {
                        // fall through and let the WebView try the network
                    }
                    return null;
                }

                @Override public void onPageFinished(WebView v, String url) {
                    sReady = true;
                }
            });

            if (attachWindow(a)) return true;
            return attachFallback(a);
        } catch (Throwable t) {
            sWeb = null;
            return false;
        }
    }

    /** The good path: a hardware accelerated, untouchable panel window. */
    private static boolean attachWindow(Activity a) {
        try {
            View decor = a.getWindow() != null ? a.getWindow().getDecorView() : null;
            if (decor == null || decor.getWindowToken() == null) return false;

            sWm = a.getWindowManager();
            sLp = new WindowManager.LayoutParams(
                1, 1,
                WindowManager.LayoutParams.TYPE_APPLICATION_PANEL,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                    | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
                    | WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED
                    | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT);
            sLp.gravity = Gravity.TOP | Gravity.LEFT;
            sLp.x = 0;
            sLp.y = 0;
            sLp.token = decor.getWindowToken();
            sWm.addView(sWeb, sLp);
            sWindowMode = true;
            return true;
        } catch (Throwable t) {
            sWm = null;
            sLp = null;
            sWindowMode = false;
            return false;
        }
    }

    /** The old path: a view inside the activity's content view (video will be black). */
    private static boolean attachFallback(Activity a) {
        try {
            sWeb.setLayoutParams(new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            sFallbackHost = new PassThrough(a);
            sFallbackHost.setBackgroundColor(Color.BLACK);
            sFallbackHost.addView(sWeb);
            a.addContentView(sFallbackHost, new FrameLayout.LayoutParams(1, 1));
            return true;
        } catch (Throwable t) {
            sWeb = null;
            sFallbackHost = null;
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

    private static void send(String obj, String value) {
        try {
            UnityPlayer.UnitySendMessage(obj, "OnWebPlayback", value);
        } catch (Throwable t) {
            // the bridge object is gone; nothing to report to
        }
    }
}
