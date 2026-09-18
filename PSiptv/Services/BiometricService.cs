namespace PSiptv.Services;

/// <summary>Native biometric verification. PIN remains available if unsupported or cancelled.</summary>
public static class BiometricService
{
    public static bool IsAvailable
    {
        get
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(28)) return false;
            var context = Android.App.Application.Context;
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var manager = context.GetSystemService(Android.Content.Context.BiometricService) as Android.Hardware.Biometrics.BiometricManager;
#pragma warning disable CA1422
                return manager?.CanAuthenticate() == Android.Hardware.Biometrics.BiometricCode.Success;
#pragma warning restore CA1422
            }
#pragma warning disable CS0618, CA1422
            var fingerprint = context.GetSystemService(Android.Content.Context.FingerprintService) as Android.Hardware.Fingerprints.FingerprintManager;
            return fingerprint?.IsHardwareDetected == true && fingerprint.HasEnrolledFingerprints;
#pragma warning restore CS0618, CA1422
#elif IOS || MACCATALYST
            using var context = new LocalAuthentication.LAContext();
            return context.CanEvaluatePolicy(LocalAuthentication.LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _);
#else
            return false;
#endif
        }
    }

    public static async Task<bool> AuthenticateAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested || !IsAvailable) return false;
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) return false;
        var activity = Platform.CurrentActivity;
        if (activity is null) return false;
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = new Android.OS.CancellationSignal();
        using var registration = token.Register(() =>
        {
            signal.Cancel();
            result.TrySetResult(false);
        });
        using var callback = new AuthenticationCallback(result);
        using var negative = new NegativeButton(result);
        using var builder = new Android.Hardware.Biometrics.BiometricPrompt.Builder(activity);
        using var prompt = builder.SetTitle(LanguageService.Text("Desbloquear lista"))
            .SetSubtitle(LanguageService.Text("Confirme a sua identidade no dispositivo"))
            .SetNegativeButton(LanguageService.Text("Usar PIN"), activity.MainExecutor!, negative)
            .Build();
        prompt.Authenticate(signal, activity.MainExecutor!, callback);
        return await result.Task && !token.IsCancellationRequested;
#elif IOS || MACCATALYST
        using var context = new LocalAuthentication.LAContext { LocalizedFallbackTitle = LanguageService.Text("Usar PIN") };
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => { context.Invalidate(); result.TrySetResult(false); });
        context.EvaluatePolicy(LocalAuthentication.LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
            LanguageService.Text("Desbloquear a sua lista PSiptv"), (success, _) => result.TrySetResult(success));
        return await result.Task && !token.IsCancellationRequested;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

#if ANDROID
    [System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
    private sealed class AuthenticationCallback(TaskCompletionSource<bool> result)
        : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult? authenticationResult)
            => result.TrySetResult(true);
        public override void OnAuthenticationError(Android.Hardware.Biometrics.BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
            => result.TrySetResult(false);
    }

    private sealed class NegativeButton(TaskCompletionSource<bool> result)
        : Java.Lang.Object, Android.Content.IDialogInterfaceOnClickListener
    {
        public void OnClick(Android.Content.IDialogInterface? dialog, int which) => result.TrySetResult(false);
    }
#endif
}


