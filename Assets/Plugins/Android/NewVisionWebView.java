// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

package com.newvision.webview;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.hardware.HardwareBuffer;
import android.media.Image;
import android.media.ImageReader;
import android.net.Uri;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.Log;
import android.view.Choreographer;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

import org.json.JSONArray;
import org.json.JSONObject;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/**
 * Off-screen browser for Unity: a real Android WebView that is attached to the activity's window (invisible in the
 * headset) and drawn into an ImageReader surface. Each new page image is copied into one of two direct ByteBuffers,
 * which Unity uploads into a texture straight from native memory (see WebViewBridge.cs).
 * Input comes back from Unity as touch, scroll and key events. All WebView calls run on the UI thread.
 */
public class NewVisionWebView {
    private static final String TAG = "NewVisionWebView";
    private static final long FRAME_INTERVAL_NS = 12_000_000L; // up to the display rate (72-90 Hz)
    private static final long FOCUS_POLL_MS = 400;
    private static final long REFRESH_INTERVAL_NS = 500_000_000L; // redraw now and then even without invalidation

    private final Activity activity;
    private final int width;
    private final int height;

    private WebView webView;
    private FrameLayout container;
    private ImageReader reader;
    private Surface surface;
    private HandlerThread readerThread;
    private boolean softwareDraw;

    // Double buffer shared with Unity: the writer fills the back buffer, then swaps it to the front when Unity isn't reading
    private final ByteBuffer[] buffers = new ByteBuffer[2];
    private final Object lock = new Object();
    private volatile int front;
    private volatile int frameNumber;
    private boolean reading;
    private boolean writing;
    private boolean pendingSwap;

    private volatile boolean capturing = true;
    private volatile boolean dirty = true;
    private long lastCaptureNs;
    private long lastDrawNs;
    private long downTime;
    private long lastMoveTime;
    private boolean destroyed;

    private volatile String url = "";
    private volatile String title = "";
    private volatile int progress = 100;
    private volatile boolean canGoBack;
    private volatile boolean canGoForward;
    private volatile boolean inputFocused;
    private volatile String inputType = "";
    private volatile String inputValue = "";
    private volatile int focusVersion;
    private volatile String message = "";

    private final Choreographer.FrameCallback frameCallback = new Choreographer.FrameCallback() {
        @Override
        public void doFrame(long frameTimeNanos) {
            if (destroyed) {
                return;
            }
            drawFrame(frameTimeNanos);
            Choreographer.getInstance().postFrameCallback(this);
        }
    };

    public NewVisionWebView(Activity activity, int width, int height) {
        this.activity = activity;
        this.width = width;
        this.height = height;
        for (int i = 0; i < 2; i++) {
            buffers[i] = ByteBuffer.allocateDirect(width * height * 4).order(ByteOrder.nativeOrder());
        }
        activity.runOnUiThread(this::create);
    }

    private void create() {
        webView = new WebView(activity);
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setUseWideViewPort(true);
        settings.setLoadWithOverviewMode(true);
        settings.setSupportMultipleWindows(false); // target=_blank opens in the same view
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setBuiltInZoomControls(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        // The window is never visible in the headset, so by default Chromium rasters only what it must and fast
        // scrolling shows white holes. Pre-raster the viewport (and around it) even though the view is "offscreen".
        settings.setOffscreenPreRaster(true);
        webView.setBackgroundColor(Color.WHITE);
        webView.setFocusable(true);
        webView.setFocusableInTouchMode(true);

        webView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                String scheme = request.getUrl().getScheme();
                if ("http".equals(scheme) || "https".equals(scheme)) {
                    return false;
                }
                // intent:, market:, file: ... can't be shown here
                message = "Can't open " + scheme + ": links";
                return true;
            }

            @Override
            public void onPageStarted(WebView view, String pageUrl, Bitmap favicon) {
                url = pageUrl;
                message = "";
                updateHistory();
            }

            @Override
            public void onPageFinished(WebView view, String pageUrl) {
                url = pageUrl;
                updateHistory();
                dirty = true;
                view.evaluateJavascript("window.devicePixelRatio + ' ' + window.innerWidth",
                        v -> Log.i(TAG, "page dpr/width " + v));
            }

            @Override
            public void doUpdateVisitedHistory(WebView view, String pageUrl, boolean isReload) {
                url = pageUrl;
                updateHistory();
            }

            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (request.isForMainFrame()) {
                    message = String.valueOf(error.getDescription());
                }
            }
        });
        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onProgressChanged(WebView view, int newProgress) {
                progress = newProgress;
                dirty = true;
            }

            @Override
            public void onReceivedTitle(WebView view, String pageTitle) {
                title = pageTitle != null ? pageTitle : "";
            }
        });
        webView.setDownloadListener((downloadUrl, userAgent, contentDisposition, mimetype, contentLength) ->
                message = "Downloads are not supported here");

        // Any redraw request of the page marks the image dirty
        container = new FrameLayout(activity) {
            @Override
            public void onDescendantInvalidated(View child, View target) {
                super.onDescendantInvalidated(child, target);
                dirty = true;
            }
        };
        container.addView(webView, new FrameLayout.LayoutParams(width, height));
        ViewGroup root = (ViewGroup) activity.getWindow().getDecorView();
        root.addView(container, 0, new FrameLayout.LayoutParams(width, height));

        readerThread = new HandlerThread("NewVisionWebViewReader");
        readerThread.start();
        reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2,
                HardwareBuffer.USAGE_CPU_READ_OFTEN | HardwareBuffer.USAGE_GPU_COLOR_OUTPUT);
        reader.setOnImageAvailableListener(this::copyImage, new Handler(readerThread.getLooper()));
        surface = reader.getSurface();

        webView.requestFocus();
        Choreographer.getInstance().postFrameCallback(frameCallback);
        webView.postDelayed(focusPoll, FOCUS_POLL_MS);
        Log.i(TAG, "created " + width + "x" + height + ", density " + activity.getResources().getDisplayMetrics().density);
    }

    private void updateHistory() {
        canGoBack = webView.canGoBack();
        canGoForward = webView.canGoForward();
    }

    /** UI thread: draws the page into the ImageReader surface when it changed. */
    private void drawFrame(long now) {
        if (!capturing || surface == null || now - lastCaptureNs < FRAME_INTERVAL_NS) {
            return;
        }
        if (!dirty && now - lastDrawNs < REFRESH_INTERVAL_NS) {
            return;
        }
        dirty = false;
        lastCaptureNs = now;
        lastDrawNs = now;
        try {
            Canvas canvas = softwareDraw ? surface.lockCanvas(null) : surface.lockHardwareCanvas();
            try {
                canvas.drawColor(Color.WHITE);
                webView.draw(canvas);
            } finally {
                surface.unlockCanvasAndPost(canvas);
            }
        } catch (Exception e) {
            if (!softwareDraw) {
                Log.w(TAG, "hardware drawing failed, switching to software: " + e);
                softwareDraw = true;
                webView.setLayerType(View.LAYER_TYPE_SOFTWARE, null);
            } else {
                Log.w(TAG, "drawing failed: " + e);
            }
        }
    }

    /** Reader thread: copies the new image (rows may be padded) tightly into the back buffer and publishes it. */
    private void copyImage(ImageReader imageReader) {
        Image image = imageReader.acquireLatestImage();
        if (image == null) {
            return;
        }
        try {
            Image.Plane plane = image.getPlanes()[0];
            ByteBuffer src = plane.getBuffer();
            int rowStride = plane.getRowStride();
            int rowBytes = width * 4;
            int back;
            synchronized (lock) {
                back = 1 - front;
                writing = true;
            }
            ByteBuffer dst = buffers[back];
            dst.clear();
            if (rowStride == rowBytes) {
                src.limit(Math.min(src.capacity(), rowBytes * height));
                dst.put(src);
            } else {
                for (int y = 0; y < height; y++) {
                    src.limit(y * rowStride + rowBytes);
                    src.position(y * rowStride);
                    dst.put(src);
                }
            }
            synchronized (lock) {
                writing = false;
                if (reading) {
                    pendingSwap = true;
                } else {
                    front = back;
                    frameNumber++;
                    pendingSwap = false;
                }
            }
        } catch (Exception e) {
            synchronized (lock) {
                writing = false;
            }
            Log.w(TAG, "copy failed: " + e);
        } finally {
            image.close();
        }
    }

    // ---- Frame access for Unity ----

    public int getWidth() { return width; }
    public int getHeight() { return height; }
    public ByteBuffer getBuffer(int index) { return buffers[index]; }
    public int getFrameNumber() { return frameNumber; }

    /** Locks the front buffer for reading and returns its index; call releaseFrame() right after copying it. */
    public int acquireFrame() {
        synchronized (lock) {
            reading = true;
            return front;
        }
    }

    public void releaseFrame() {
        synchronized (lock) {
            reading = false;
            if (pendingSwap && !writing) {
                front = 1 - front;
                frameNumber++;
                pendingSwap = false;
            }
        }
    }

    // ---- State for the toolbar ----

    public String getUrl() { return url; }
    public String getTitle() { return title; }
    public int getProgress() { return progress; }
    public boolean canGoBack() { return canGoBack; }
    public boolean canGoForward() { return canGoForward; }
    public boolean isInputFocused() { return inputFocused; }
    /** Changes each time a text field gets the focus. */
    public int getFocusVersion() { return focusVersion; }
    public String getInputType() { return inputType; }
    public String getInputValue() { return inputValue; }

    /** Last error or notice for the user, cleared by reading it. */
    public String takeMessage() {
        String m = message;
        message = "";
        return m;
    }

    // ---- Commands from Unity ----

    public void loadUrl(final String target) {
        activity.runOnUiThread(() -> {
            Uri uri = Uri.parse(target);
            String scheme = uri.getScheme();
            if ("http".equals(scheme) || "https".equals(scheme)) {
                webView.loadUrl(target);
            } else {
                message = "Only web addresses can be opened";
            }
        });
    }

    public void goBack() { activity.runOnUiThread(() -> { if (webView.canGoBack()) webView.goBack(); }); }
    public void goForward() { activity.runOnUiThread(() -> { if (webView.canGoForward()) webView.goForward(); }); }
    public void reload() { activity.runOnUiThread(() -> webView.reload()); }
    public void stopLoading() { activity.runOnUiThread(() -> webView.stopLoading()); }

    /** Pauses drawing and JavaScript timers while the panel is hidden. */
    public void setActive(final boolean active) {
        capturing = active;
        dirty = true;
        activity.runOnUiThread(() -> {
            if (active) {
                webView.onResume();
                webView.resumeTimers();
            } else {
                webView.onPause();
            }
        });
    }

    /** action: 0 down, 1 up, 2 move (MotionEvent constants), x / y in page pixels from the top-left. */
    public void touch(final int action, final float x, final float y) {
        touchAt(action, x, y, false);
    }

    /**
     * Like touch(), but an ACTION_UP can be stamped right after the last move: when the hand's release jerk was
     * filtered out on the Unity side, the fling must still see the speed of the real gesture.
     */
    public void touchAt(final int action, final float x, final float y, final boolean atLastMove) {
        final long now = SystemClock.uptimeMillis();
        activity.runOnUiThread(() -> {
            long time = now;
            if (action == MotionEvent.ACTION_DOWN) {
                downTime = now;
            } else if (action == MotionEvent.ACTION_MOVE) {
                lastMoveTime = now;
            } else if (action == MotionEvent.ACTION_UP && atLastMove && lastMoveTime > downTime) {
                time = Math.min(now, lastMoveTime + 8);
            }
            MotionEvent event = MotionEvent.obtain(downTime, time, action, x, y, 0);
            event.setSource(InputDevice.SOURCE_TOUCHSCREEN);
            webView.dispatchTouchEvent(event);
            event.recycle();
            if (action == MotionEvent.ACTION_UP) {
                // A tap may have focused a text field: check a moment later
                webView.postDelayed(this::checkInputFocus, 250);
            }
        });
    }

    /** Mouse-wheel style scrolling at a point; positive amount scrolls the page down. */
    public void scroll(final float x, final float y, final float amount) {
        final long now = SystemClock.uptimeMillis();
        activity.runOnUiThread(() -> {
            MotionEvent.PointerProperties[] properties = { new MotionEvent.PointerProperties() };
            properties[0].id = 0;
            properties[0].toolType = MotionEvent.TOOL_TYPE_MOUSE;
            MotionEvent.PointerCoords[] coords = { new MotionEvent.PointerCoords() };
            coords[0].x = x;
            coords[0].y = y;
            coords[0].setAxisValue(MotionEvent.AXIS_VSCROLL, -amount);
            MotionEvent event = MotionEvent.obtain(now, now, MotionEvent.ACTION_SCROLL, 1, properties, coords,
                    0, 0, 1f, 1f, 0, 0, InputDevice.SOURCE_MOUSE, 0);
            webView.dispatchGenericMotionEvent(event);
            event.recycle();
        });
    }

    /**
     * Scrolls whatever is scrollable under the point (an inner box or the page) by dx / dy page pixels, directly in
     * JavaScript: smooth, exact and without fling, for thumbstick scrolling.
     */
    public void scrollAt(final float x, final float y, final float dx, final float dy) {
        activity.runOnUiThread(() -> {
            float density = activity.getResources().getDisplayMetrics().density;
            webView.evaluateJavascript("(function(x,y,dx,dy){var e=document.elementFromPoint(x,y);" +
                    "while(e&&e!==document.body&&e!==document.documentElement){var s=getComputedStyle(e);" +
                    "if((dy&&e.scrollHeight>e.clientHeight&&/(auto|scroll|overlay)/.test(s.overflowY))||" +
                    "(dx&&e.scrollWidth>e.clientWidth&&/(auto|scroll|overlay)/.test(s.overflowX))){" +
                    "var t=e.scrollTop,l=e.scrollLeft;e.scrollBy(dx,dy);if(e.scrollTop!==t||e.scrollLeft!==l)return;}" +
                    "e=e.parentElement;}" +
                    "(document.scrollingElement||document.documentElement).scrollBy(dx,dy);})(" +
                    (x / density) + "," + (y / density) + "," + (dx / density) + "," + (dy / density) + ")", null);
        });
    }

    /** Mouse hover (links light up, cursors change); exit = true when the pointer left the page. */
    public void hover(final float x, final float y, final boolean exit) {
        final long now = SystemClock.uptimeMillis();
        activity.runOnUiThread(() -> {
            MotionEvent.PointerProperties[] properties = { new MotionEvent.PointerProperties() };
            properties[0].id = 0;
            properties[0].toolType = MotionEvent.TOOL_TYPE_MOUSE;
            MotionEvent.PointerCoords[] coords = { new MotionEvent.PointerCoords() };
            coords[0].x = x;
            coords[0].y = y;
            MotionEvent event = MotionEvent.obtain(now, now, exit ? MotionEvent.ACTION_HOVER_EXIT : MotionEvent.ACTION_HOVER_MOVE,
                    1, properties, coords, 0, 0, 1f, 1f, 0, 0, InputDevice.SOURCE_MOUSE, 0);
            webView.dispatchGenericMotionEvent(event);
            event.recycle();
        });
    }

    /** Replaces the whole text of the focused field (the system keyboard edits its own copy of it). */
    public void setInputValue(final String text) {
        activity.runOnUiThread(() -> webView.evaluateJavascript(
                "(function(v){var e=document.activeElement;if(!e)return;" +
                "if(e.isContentEditable){e.textContent=v;}else{" +
                "var d=Object.getOwnPropertyDescriptor(Object.getPrototypeOf(e),'value');" +
                "if(d&&d.set){d.set.call(e,v);}else{e.value=v;}}" +
                "e.dispatchEvent(new Event('input',{bubbles:true}));})(" + JSONObject.quote(text) + ")", null));
    }

    /** Types text into the focused field of the page. */
    public void insertText(final String text) {
        activity.runOnUiThread(() -> webView.evaluateJavascript(
                "document.execCommand('insertText', false, " + JSONObject.quote(text) + ")", null));
    }

    /** Sends a key press, e.g. KeyEvent.KEYCODE_DEL (67) or KEYCODE_ENTER (66). */
    public void key(final int keyCode) {
        activity.runOnUiThread(() -> {
            long now = SystemClock.uptimeMillis();
            webView.dispatchKeyEvent(new KeyEvent(now, now, KeyEvent.ACTION_DOWN, keyCode, 0));
            webView.dispatchKeyEvent(new KeyEvent(now, now, KeyEvent.ACTION_UP, keyCode, 0));
            if (keyCode == KeyEvent.KEYCODE_ENTER) {
                webView.postDelayed(this::checkInputFocus, 250);
            }
        });
    }

    /** Takes the focus away from the text field (keyboard closed). */
    public void blurInput() {
        activity.runOnUiThread(() -> {
            webView.evaluateJavascript("document.activeElement && document.activeElement.blur()", null);
            inputFocused = false;
        });
    }

    /** Asks the page whether a text field has the focus; result: [focused, type, current text]. */
    private void checkInputFocus() {
        if (destroyed) {
            return;
        }
        webView.evaluateJavascript(
                "(function(){var e=document.activeElement;if(!e)return [0,'',''];var t=e.tagName;" +
                "if(t=='TEXTAREA')return [1,'textarea',e.value];" +
                "if(e.isContentEditable)return [1,'text',e.textContent];" +
                "if(t=='INPUT'&&!/^(button|submit|checkbox|radio|range|color|file|image|reset|hidden)$/i.test(e.type))" +
                "return [1,(e.type||'text').toLowerCase(),e.value];" +
                "return [0,'',''];})()",
                value -> {
                    try {
                        JSONArray result = new JSONArray(value);
                        boolean focused = result.getInt(0) == 1;
                        if (focused && !inputFocused) {
                            inputType = result.getString(1);
                            inputValue = result.getString(2);
                            focusVersion++;
                        }
                        inputFocused = focused;
                    } catch (Exception e) {
                        inputFocused = false;
                    }
                });
    }

    private final Runnable focusPoll = new Runnable() {
        @Override
        public void run() {
            if (destroyed) {
                return;
            }
            if (capturing) {
                checkInputFocus();
            }
            webView.postDelayed(this, FOCUS_POLL_MS);
        }
    };

    public void destroy() {
        activity.runOnUiThread(() -> {
            destroyed = true;
            Choreographer.getInstance().removeFrameCallback(frameCallback);
            if (container != null && container.getParent() instanceof ViewGroup) {
                ((ViewGroup) container.getParent()).removeView(container);
            }
            if (webView != null) {
                webView.stopLoading();
                webView.destroy();
            }
            if (reader != null) {
                reader.close();
            }
            if (readerThread != null) {
                readerThread.quitSafely();
            }
        });
    }
}
