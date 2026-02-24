package com.camerac.sample;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.os.Bundle;
import android.util.Log;
import android.widget.TextView;

public class MainActivity extends Activity {

    private static final String TAG            = "CameracSample";
    private static final int    REQ_CAMERA     = 1001;

    // Native library – libcamerac.so must be loaded first (it's a dependency).
    static {
        System.loadLibrary("camerac");
        System.loadLibrary("native_sample");
    }

    private TextView mStatus;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        mStatus = new TextView(this);
        mStatus.setTextSize(16f);
        mStatus.setPadding(32, 32, 32, 32);
        setContentView(mStatus);

        if (checkSelfPermission(Manifest.permission.CAMERA)
                == PackageManager.PERMISSION_GRANTED) {
            startTest();
        } else {
            mStatus.setText("Requesting camera permission…");
            requestPermissions(
                    new String[]{ Manifest.permission.CAMERA },
                    REQ_CAMERA);
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode,
                                           String[] permissions,
                                           int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQ_CAMERA) {
            if (grantResults.length > 0
                    && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                startTest();
            } else {
                Log.e(TAG, "Camera permission denied.");
                mStatus.setText("Camera permission denied – check Logcat.");
            }
        }
    }

    /** Runs the native camerac test on a background thread. */
    private void startTest() {
        mStatus.setText("Running camerac test – see Logcat…");
        Log.i(TAG, "Permission granted – starting camerac test.");
        new Thread(() -> {
            runCameracTest();
            runOnUiThread(() -> mStatus.setText("Done – see Logcat for results."));
        }, "camerac-test").start();
    }

    /** Implemented in native_sample.cpp */
    private native void runCameracTest();
}
