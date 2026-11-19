// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

package com.newvision.ble;

import android.Manifest;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.SystemClock;
import android.util.Log;

import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * Bluetooth LE link to the M5StickC Plus2 ("New Vision Stick"), Nordic-UART style:
 * text lines written to RX, replies arrive as notifications on TX (chunks, '\n' ends a line).
 * No pairing: the app connects straight to the GATT server; the stick is a keyboard for the laptop at the same time.
 * Unity calls tick() a couple of times per second (scan / connect / reconnect), readLine() every frame and writeLine().
 * Needs the runtime permissions BLUETOOTH_SCAN and BLUETOOTH_CONNECT (Unity asks for them).
 */
public class NewVisionBleLink {
    private static final String TAG = "NewVisionBle";
    private static final String DEVICE_NAME = "New Vision Stick";
    private static final UUID SERVICE = UUID.fromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID RX = UUID.fromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID TX = UUID.fromString("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");
    private static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");
    private static final long SCAN_MS = 10000;
    private static final long CONNECT_TIMEOUT_MS = 12000;

    private final Context context;
    private final BluetoothAdapter adapter;
    private final ConcurrentLinkedQueue<String> lines = new ConcurrentLinkedQueue<>();
    private final ConcurrentLinkedQueue<byte[]> outgoing = new ConcurrentLinkedQueue<>();
    private final StringBuilder partial = new StringBuilder();

    private volatile String state = "idle";
    private BluetoothLeScanner scanner;
    private boolean scanning;
    private long scanStarted;
    private BluetoothDevice device;
    private String lastAddress;
    private BluetoothGatt gatt;
    private long connectStarted;
    private BluetoothGattCharacteristic rx;
    private volatile boolean writing;
    private volatile int mtu = 23;

    public NewVisionBleLink(Context context) {
        this.context = context.getApplicationContext();
        BluetoothManager manager = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
        this.adapter = manager != null ? manager.getAdapter() : null;
    }

    /** "no permission", "bluetooth off", "scanning", "connecting", "open", "error: ..." */
    public String getState() {
        return state;
    }

    public String getDeviceName() {
        BluetoothDevice d = device;
        return d == null ? "" : d.getAddress();
    }

    /** Remembered stick address from an earlier session: connect straight away, no scan. */
    public void setLastAddress(String address) {
        lastAddress = address == null || address.isEmpty() ? null : address;
    }

    public String readLine() {
        return lines.poll();
    }

    /** Queues a line; GATT allows one write at a time, the next goes out when the previous one is acknowledged. */
    public boolean writeLine(String line) {
        if (!"open".equals(state)) {
            return false;
        }
        byte[] data = (line + "\n").getBytes(StandardCharsets.UTF_8);
        int chunk = Math.max(20, mtu - 3);
        for (int i = 0; i < data.length; i += chunk) {
            byte[] part = new byte[Math.min(chunk, data.length - i)];
            System.arraycopy(data, i, part, 0, part.length);
            outgoing.add(part);
        }
        if (outgoing.size() > 64) {
            outgoing.clear(); // link stuck: drop instead of growing forever
        }
        pump();
        return true;
    }

    public synchronized void tick() {
        try {
            tickInner();
        } catch (SecurityException e) {
            state = "no permission";
        } catch (Exception e) {
            Log.w(TAG, "tick", e);
            state = "error: " + e.getMessage();
        }
    }

    private void tickInner() {
        if (adapter == null) {
            state = "error: no Bluetooth";
            return;
        }
        if (!hasPermissions()) {
            state = "no permission";
            return;
        }
        if (!adapter.isEnabled()) {
            stopScan();
            closeGatt();
            state = "bluetooth off";
            return;
        }
        if (gatt != null) {
            if ("connecting".equals(state) && SystemClock.elapsedRealtime() - connectStarted > CONNECT_TIMEOUT_MS) {
                Log.i(TAG, "connect timeout, scanning again");
                closeGatt();
                lastAddress = null;
            } else {
                return;
            }
        }
        if (lastAddress != null) {
            connect(adapter.getRemoteDevice(lastAddress));
            return;
        }
        if (scanning && SystemClock.elapsedRealtime() - scanStarted > SCAN_MS) {
            stopScan(); // restart below: some stacks stop reporting during long scans
        }
        if (!scanning) {
            startScan();
        }
    }

    private boolean hasPermissions() {
        return context.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED &&
               context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
    }

    private void startScan() {
        scanner = adapter.getBluetoothLeScanner();
        if (scanner == null) {
            state = "bluetooth off";
            return;
        }
        ScanSettings settings = new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build();
        scanner.startScan(null, settings, scanCallback);
        scanning = true;
        scanStarted = SystemClock.elapsedRealtime();
        state = "scanning";
        Log.i(TAG, "scanning for " + DEVICE_NAME);
    }

    private void stopScan() {
        if (scanning && scanner != null) {
            try {
                scanner.stopScan(scanCallback);
            } catch (Exception ignored) {
            }
        }
        scanning = false;
    }

    private final ScanCallback scanCallback = new ScanCallback() {
        @Override
        public void onScanResult(int callbackType, ScanResult result) {
            String name = result.getScanRecord() != null ? result.getScanRecord().getDeviceName() : null;
            if (!DEVICE_NAME.equals(name)) {
                return;
            }
            synchronized (NewVisionBleLink.this) {
                if (gatt != null) {
                    return;
                }
                Log.i(TAG, "found " + result.getDevice().getAddress() + " rssi " + result.getRssi());
                stopScan();
                connect(result.getDevice());
            }
        }

        @Override
        public void onScanFailed(int errorCode) {
            Log.w(TAG, "scan failed " + errorCode);
            scanning = false;
            state = "error: scan failed " + errorCode;
        }
    };

    private void connect(BluetoothDevice d) {
        device = d;
        state = "connecting";
        connectStarted = SystemClock.elapsedRealtime();
        Log.i(TAG, "connecting to " + d.getAddress());
        gatt = d.connectGatt(context, false, gattCallback, BluetoothDevice.TRANSPORT_LE);
    }

    private synchronized void closeGatt() {
        if (gatt != null) {
            try {
                gatt.disconnect();
                gatt.close();
            } catch (SecurityException ignored) {
            }
            gatt = null;
        }
        rx = null;
        writing = false;
        outgoing.clear();
        synchronized (partial) {
            partial.setLength(0);
        }
    }

    public synchronized void close() {
        stopScan();
        closeGatt();
        state = "idle";
    }

    private void pump() {
        BluetoothGatt g = gatt;
        BluetoothGattCharacteristic c = rx;
        if (g == null || c == null || writing) {
            return;
        }
        byte[] next = outgoing.poll();
        if (next == null) {
            return;
        }
        writing = true;
        try {
            c.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT);
            c.setValue(next);
            if (!g.writeCharacteristic(c)) {
                writing = false;
                Log.w(TAG, "write refused");
            }
        } catch (SecurityException e) {
            writing = false;
        }
    }

    private void onBytes(byte[] value) {
        if (value == null) {
            return;
        }
        synchronized (partial) {
            partial.append(new String(value, StandardCharsets.UTF_8));
            int newline;
            while ((newline = partial.indexOf("\n")) >= 0) {
                String line = partial.substring(0, newline).trim();
                partial.delete(0, newline + 1);
                if (!line.isEmpty()) {
                    lines.add(line);
                }
            }
            if (partial.length() > 1024) {
                partial.setLength(0);
            }
        }
        while (lines.size() > 200) {
            lines.poll();
        }
    }

    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override
        public void onConnectionStateChange(BluetoothGatt g, int status, int newState) {
            try {
                if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                    Log.i(TAG, "connected, asking MTU");
                    g.requestConnectionPriority(BluetoothGatt.CONNECTION_PRIORITY_HIGH);
                    if (!g.requestMtu(185)) {
                        g.discoverServices();
                    }
                } else {
                    Log.i(TAG, "disconnected, status " + status);
                    closeGatt();
                    state = "scanning";
                }
            } catch (SecurityException e) {
                state = "no permission";
            }
        }

        @Override
        public void onMtuChanged(BluetoothGatt g, int newMtu, int status) {
            mtu = status == BluetoothGatt.GATT_SUCCESS ? newMtu : 23;
            Log.i(TAG, "MTU " + mtu);
            try {
                g.discoverServices();
            } catch (SecurityException e) {
                state = "no permission";
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt g, int status) {
            BluetoothGattService service = g.getService(SERVICE);
            if (status != BluetoothGatt.GATT_SUCCESS || service == null) {
                Log.w(TAG, "UART service not found, status " + status);
                state = "error: stick has old firmware?";
                closeGatt();
                return;
            }
            rx = service.getCharacteristic(RX);
            BluetoothGattCharacteristic tx = service.getCharacteristic(TX);
            if (rx == null || tx == null) {
                state = "error: characteristics missing";
                closeGatt();
                return;
            }
            try {
                g.setCharacteristicNotification(tx, true);
                BluetoothGattDescriptor cccd = tx.getDescriptor(CCCD);
                if (cccd != null) {
                    cccd.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
                    g.writeDescriptor(cccd);
                } else {
                    opened(g);
                }
            } catch (SecurityException e) {
                state = "no permission";
            }
        }

        @Override
        public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor descriptor, int status) {
            opened(g);
        }

        private void opened(BluetoothGatt g) {
            lastAddress = g.getDevice().getAddress();
            state = "open";
            Log.i(TAG, "link open to " + lastAddress);
            pump();
        }

        @Override
        public void onCharacteristicWrite(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
            writing = false;
            pump();
        }

        // Android 13+: the value comes as a parameter
        @Override
        public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value) {
            onBytes(value);
        }

        // Android 12 and older
        @Override
        @SuppressWarnings("deprecation")
        public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
            onBytes(c.getValue());
        }
    };
}
