package com.ps.psiptv.wearinstaller;

import io.github.muntashirakon.adb.AdbConnection;
import io.github.muntashirakon.adb.WatchPairingConnectionCtx;
import io.github.muntashirakon.adb.AdbStream;
import java.io.*;
import java.security.*;
import java.security.cert.Certificate;
import java.security.cert.CertificateFactory;
import java.security.spec.PKCS8EncodedKeySpec;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;

/** Narrow bridge: pairing, fixed package inspection and streamed APK installation. */
public final class WearAdbBridge implements Closeable {
    private final PrivateKey key;
    private final Certificate certificate;
    private final AtomicLong sent = new AtomicLong();
    private volatile AdbConnection connection;
    private volatile WatchPairingConnectionCtx pairing;
    private volatile boolean cancelled;

    public WearAdbBridge(byte[] pkcs8, byte[] der) throws Exception {
        key = KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(pkcs8));
        certificate = CertificateFactory.getInstance("X.509").generateCertificate(new ByteArrayInputStream(der));
    }
    public long getBytesSent() { return sent.get(); }

    public boolean pairWatch(String host, int port, String code) throws Exception {
        WatchPairingConnectionCtx pending = new WatchPairingConnectionCtx(host, port, code.getBytes("UTF-8"), key, certificate, "PSiptv phone");
        try (WatchPairingConnectionCtx cleanup = pending) {
            pairing = pending;
            if (cancelled) throw new IOException("Cancelled");
            pending.start();
            if (cancelled) throw new IOException("Cancelled");
            return true;
        } finally { pairing = null; pending.destroyContext(); }
    }
    public boolean connectWatch(String host, int port) throws Exception {
        AdbConnection pending = new AdbConnection.Builder(host, port).setApi(30)
            .setPrivateKey(key).setCertificate(certificate).setDeviceName("PSiptv phone").build();
        connection = pending;
        if (cancelled) { pending.close(); throw new IOException("Cancelled"); }
        return pending.connect(20, TimeUnit.SECONDS, true);
    }

    public String features() throws Exception { return command("pm list features", 128 * 1024); }
    public String installedPackage() throws Exception { return command("dumpsys package com.PS.PSiptv", 512 * 1024); }

    public String installApk(String path) throws Exception {
        File apk = new File(path);
        long length = apk.length();
        if (length <= 0 || length > 128L * 1024 * 1024) throw new IOException("Invalid APK size");
        sent.set(0);
        try (AdbStream stream = connection.open("shell,v2,raw:cmd package install -r -S " + length);
             InputStream file = new FileInputStream(apk)) {
            OutputStream output = stream.openOutputStream();
            // shell-v2 stdin packets: stream id, little-endian size, then bytes.
            byte[] buffer = new byte[64 * 1024 + 5];
            int count;
            while ((count = file.read(buffer, 5, buffer.length - 5)) != -1) {
                buffer[0] = 0;
                for (int i = 0; i < 4; i++) buffer[i + 1] = (byte) (count >>> (i * 8));
                output.write(buffer, 0, count + 5);
                sent.addAndGet(count);
            }
            output.flush();
            return readShell(stream.openInputStream(), 64 * 1024, true);
        }
    }
    private String command(String command, int limit) throws Exception {
        try (AdbStream stream = connection.open("shell,v2,raw:" + command)) { return readShell(stream.openInputStream(), limit, false); }
    }
    private String readShell(InputStream input, int limit, boolean installation) throws IOException {
        ByteArrayOutputStream result = new ByteArrayOutputStream();
        byte[] header = new byte[5];
        for (;;) {
            readFully(input, header);
            int kind = header[0] & 255;
            long length = 0;
            for (int i = 0; i < 4; i++) length |= (long) (header[i + 1] & 255) << (i * 8);
            if (length > limit || result.size() + length > limit) throw new IOException("ADB response too large");
            byte[] payload = new byte[(int) length];
            readFully(input, payload);
            if (kind == 3) { // Explicit shell-v2 exit packet: do not wait for stream EOF.
                if (payload.length == 0) throw new IOException("ADB command returned no exit code");
                if (payload[0] != 0) {
                    if (installation) return "Failure: " + result.toString("UTF-8");
                    throw new IOException("ADB command failed: " + result.toString("UTF-8"));
                }
                return result.toString("UTF-8");
            }
            if (kind != 1 && kind != 2) throw new IOException("Unexpected ADB shell packet");
            result.write(payload);
        }
    }
    private void readFully(InputStream input, byte[] bytes) throws IOException {
        int offset = 0;
        while (offset < bytes.length) {
            int count = input.read(bytes, offset, bytes.length - offset);
            if (count <= 0) throw new EOFException("ADB closed before command completion");
            offset += count;
        }
    }
    // Avoid the manager lock held during a blocking connect/open. Closing the
    // underlying connection interrupts reads and writes from cancellation threads.
    public void cancelOperation() {
        cancelled = true;
        try { if (connection != null) connection.close(); } catch (Exception ignored) { }
        try { if (pairing != null) pairing.close(); } catch (Exception ignored) { }
    }
    @Override public void close() { cancelOperation(); }
}
