using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Com.PS.Psiptv.Wearinstaller;
using PSiptv.Core;

namespace PSiptv.Services;

public sealed record WatchInstallation(long VersionCode, string VersionName);

public static class WearInstallService
{
    private const string IdentityKey = "wear-installer.adb-identity.v1";
    private static readonly SemaphoreSlim gate = new(1, 1);

    public static Task<bool> PairAsync(string host, int port, string code, CancellationToken token) =>
        RunAsync(bridge => bridge.PairWatch(WearInstallPolicy.Host(host), port, WearInstallPolicy.PairingCode(code)),
            TimeSpan.FromSeconds(45), token);

    public static Task<WatchInstallation> InspectAsync(string host, int port, CancellationToken token) =>
        RunAsync(bridge => Inspect(bridge, host, port), TimeSpan.FromSeconds(40), token);

    public static Task<WatchInstallation> InstallAsync(string host, int port, string apkPath,
        IProgress<long> progress, CancellationToken token)
    {
        var apk = AndroidAppUpdateService.ValidateWearApk(apkPath);
        return RunAsync(bridge =>
        {
            var current = Inspect(bridge, host, port);
            if (current.VersionCode >= apk.Code) return current;
            var response = bridge.InstallApk(apkPath) ?? "";
            if (!WearInstallPolicy.InstallSucceeded(response))
                throw new IOException("O relógio recusou a instalação. Confirme o espaço livre e a assinatura da versão já instalada.\n" + response[..Math.Min(response.Length, 700)]);
            var installed = ReadVersion(bridge);
            if (installed.VersionCode != apk.Code) throw new IOException("O relógio ainda não confirmou a versão esperada. Volte a verificar a ligação antes de tentar novamente.");
            return installed;
        }, TimeSpan.FromMinutes(10), token, progress);
    }

    private static WatchInstallation Inspect(WearAdbBridge bridge, string host, int port)
    {
        if (!bridge.ConnectWatch(WearInstallPolicy.Host(host), port)) throw new IOException("Não foi possível ligar ao relógio. Verifique a porta de ligação e o emparelhamento.");
        if (!WearInstallPolicy.IsWatch(bridge.Features() ?? "")) throw new IOException("O endereço indicado não pertence a um dispositivo Wear OS. Nenhum APK foi enviado.");
        return ReadVersion(bridge);
    }

    private static WatchInstallation ReadVersion(WearAdbBridge bridge) =>
        WearInstallPolicy.InstalledVersion(bridge.InstalledPackage() ?? "") is { } version
            ? new(version.Code, version.Name) : new(0, "Não instalada");

    private static async Task<T> RunAsync<T>(Func<WearAdbBridge, T> operation, TimeSpan maximum,
        CancellationToken token, IProgress<long>? progress = null)
    {
        await gate.WaitAsync(token);
        WearAdbBridge bridge;
        try { bridge = await CreateBridgeAsync(); }
        catch { gate.Release(); throw; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(maximum);
        Task? closing = null;
        using var cancel = deadline.Token.Register(() => closing = Task.Run(() => bridge.CancelOperation()));
        var worker = Task.Run(() => { deadline.Token.ThrowIfCancellationRequested(); return operation(bridge); });
        using var monitoring = new CancellationTokenSource();
        var monitor = MonitorAsync();
        try { return await worker.WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("A ligação ao relógio excedeu o tempo de espera. Confirme o Wi-Fi, o código e as portas apresentados no relógio."); }
        catch (Java.Lang.Exception) when (token.IsCancellationRequested)
        { throw new OperationCanceledException(token); }
        catch (Java.Lang.Exception)
        { throw new IOException("Não foi possível comunicar com o relógio. Confirme o Wi-Fi e a depuração sem fios. Se a autorização mudou, volte a emparelhar com um código novo."); }
        finally
        {
            monitoring.Cancel();
            await monitor;
            cancel.Dispose();
            _ = CleanupAsync();
        }

        async Task MonitorAsync()
        {
            try
            {
                while (!worker.IsCompleted)
                {
                    if (progress is not null) progress.Report(bridge.BytesSent);
                    await Task.Delay(300, monitoring.Token);
                }
                if (progress is not null) progress.Report(bridge.BytesSent);
            }
            catch (OperationCanceledException) { }
        }
        async Task CleanupAsync()
        {
            try { await worker; } catch { /* Observe late cancellation/connection errors. */ }
            if (closing is not null) try { await closing; } catch { }
            try { await Task.Run(bridge.Close); } catch { }
            finally { bridge.Dispose(); gate.Release(); }
        }
    }

    private static async Task<WearAdbBridge> CreateBridgeAsync()
    {
        var encoded = await SecureStorage.Default.GetAsync(IdentityKey);
        byte[] key;
        byte[] certificate;
        if (string.IsNullOrEmpty(encoded))
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=PSiptv watch installer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            key = rsa.ExportPkcs8PrivateKey();
            certificate = cert.Export(X509ContentType.Cert);
            await SecureStorage.Default.SetAsync(IdentityKey, Convert.ToBase64String(key) + ":" + Convert.ToBase64String(certificate));
        }
        else
        {
            var parts = encoded.Split(':');
            if (parts.Length != 2) throw new IOException("Não foi possível ler a identidade de emparelhamento do telemóvel.");
            key = Convert.FromBase64String(parts[0]);
            certificate = Convert.FromBase64String(parts[1]);
        }
        try { return await Task.Run(() => new WearAdbBridge(key, certificate)); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
