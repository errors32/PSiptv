using PSiptv.Core;

internal static class WearInstallTests
{
    internal static void Run(Action<bool, string> check)
    {
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (ArgumentException) { check(true, name); return; }
            check(false, name);
        }
        check(WearInstallPolicy.AssetName("v1.7.12") == "PSiptv.Remote.Wear.v1.7.12.apk" &&
            WearInstallPolicy.AssetName("1.7.12") != "PSiptv.v1.7.12.apk", "Instalador seleciona apenas o nome do APK Wear da release");
        Reject(() => WearInstallPolicy.AssetName("1.7.12+../../other"), "Instalador rejeita tags com caminhos ou metadados inesperados");
        Reject(() => WearInstallPolicy.AssetName("1.7.12-preview"), "Instalador exige uma versão estável do relógio");
        check(new[] { "192.168.1.8", "10.0.0.5", "172.16.1.7", "fd00::1" }.All(host => WearInstallPolicy.Host(host) == host),
            "Instalador aceita endereços do relógio em redes locais IPv4 e IPv6");
        foreach (var value in new[] { "127.0.0.1", "8.8.8.8", "192.168.1.5:5555", "https://192.168.1.5", "192.168.1.5;pm clear" })
            Reject(() => WearInstallPolicy.Host(value), "Instalador rejeita destino impróprio: " + value);
        check(WearInstallPolicy.Port("37123") == 37123 && WearInstallPolicy.Port("65535") == 65535,
            "Instalador aceita as portas dinâmicas apresentadas pelo relógio");
        foreach (var value in new[] { "0", "65536", "-1", "5555;reboot" })
            Reject(() => WearInstallPolicy.Port(value), "Instalador rejeita porta inválida: " + value);
        check(WearInstallPolicy.PairingCode("001234") == "001234", "Instalador preserva zeros iniciais do código de emparelhamento");
        Reject(() => WearInstallPolicy.PairingCode("12345"), "Instalador rejeita códigos incompletos");
        Reject(() => WearInstallPolicy.PairingCode("１２３４５６"), "Instalador exige os seis algarismos ASCII do código Android");
        check(WearInstallPolicy.ValidDigest("sha256:" + new string('a', 64)) &&
            !WearInstallPolicy.ValidDigest(null) && !WearInstallPolicy.ValidDigest("sha256:" + new string('g', 64)),
            "Instalador exige um SHA-256 completo e válido para o APK");
        check(WearInstallPolicy.IsWatch("feature:android.hardware.bluetooth\r\nfeature:android.hardware.type.watch\r\n") &&
            !WearInstallPolicy.IsWatch("feature:android.hardware.type.watch.fake\n") && !WearInstallPolicy.IsWatch("feature:android.hardware.type.television\n"),
            "Instalador confirma a funcionalidade Wear OS e recusa telemóveis ou televisores");
        check(WearInstallPolicy.InstalledVersion("Packages:\n versionCode=298849612 minSdk=26\n versionName=1.7.11\n") is
            { Code: 298849612, Name: "1.7.11" }, "Instalador lê a versão e o build do pacote remoto");
        check(WearInstallPolicy.InstalledVersion("Unable to find package: com.PS.PSiptv") is null,
            "Instalador distingue primeira instalação de uma atualização");
        check(WearInstallPolicy.InstallSucceeded("Success\n") &&
            !WearInstallPolicy.InstallSucceeded("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]\n") &&
            !WearInstallPolicy.InstallSucceeded("Success\nError: session failed") && !WearInstallPolicy.InstallSucceeded("APK enviado"),
            "Instalador só anuncia sucesso após confirmação explícita do gestor de pacotes");
    }
}
