package com.ps.psiptv.tests;

import android.app.Instrumentation;
import android.os.Bundle;
import com.ps.psiptv.wearinstaller.WearAdbBridge;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.net.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Opt-in Debug instrumentation, excluded from production APKs. */
public final class WearInstallerSmoke extends Instrumentation {
    private Bundle arguments;
    @Override public void onCreate(Bundle values) { arguments = values; start(); }
    @Override public void onStart() {
        Bundle result = new Bundle();
        try (WearAdbBridge bridge = new WearAdbBridge(
                Files.readAllBytes(Paths.get(arguments.getString("key"))),
                Files.readAllBytes(Paths.get(arguments.getString("certificate"))))) {
            String mode = arguments.getString("mode", "cancel");
            if (mode.equals("cancel")) {
                try (ServerSocket listener = new ServerSocket(0, 1, InetAddress.getByName("127.0.0.1"))) {
                    AtomicBoolean accepted = new AtomicBoolean();
                    Thread silent = new Thread(() -> { try (Socket client = listener.accept()) { accepted.set(true); Thread.sleep(5000); } catch (Exception ignored) { } });
                    silent.setDaemon(true);
                    silent.start();
                    AtomicBoolean failed = new AtomicBoolean();
                    Thread pairing = new Thread(() -> { try { bridge.pairWatch("127.0.0.1", listener.getLocalPort(), "123456"); } catch (Throwable expected) { failed.set(true); } });
                    pairing.start();
                    Thread.sleep(700);
                    if (!accepted.get() || !pairing.isAlive()) throw new AssertionError("TLS pairing did not reach the test socket");
                    bridge.cancelOperation();
                    pairing.join(3000);
                    if (pairing.isAlive() || !failed.get()) throw new AssertionError("Pairing cancellation did not interrupt TLS I/O");
                    result.putString("pairingCancellation", "PASS");
                }
            } else if (mode.equals("pair")) {
                if (!bridge.pairWatch("127.0.0.1", Integer.parseInt(arguments.getString("port")), arguments.getString("code"))) throw new AssertionError("Pairing rejected");
                result.putString("wirelessPairing", "PASS");
            } else {
                if (!bridge.connectWatch("127.0.0.1", Integer.parseInt(arguments.getString("port", "5555")))) throw new AssertionError("Connection failed");
                if (!bridge.features().contains("feature:")) throw new AssertionError("No feature list received");
                result.putString("adbFeatures", "PASS");
                if (mode.equals("install")) {
                    String path = arguments.getString("apk");
                    String output = bridge.installApk(path);
                    if (!output.trim().equals("Success")) throw new AssertionError("Install response: " + output);
                    if (bridge.getBytesSent() != Files.size(Paths.get(path))) throw new AssertionError("Transfer byte count mismatch");
                    result.putString("streamedInstall", "PASS");
                }
            }
            finish(-1, result);
        } catch (Throwable error) {
            result.putString("error", error.toString());
            java.io.StringWriter trace = new java.io.StringWriter();
            error.printStackTrace(new java.io.PrintWriter(trace));
            result.putString("trace", trace.toString());
            finish(1, result);
        }
    }
}
