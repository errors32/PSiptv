#if ANDROID
using Android.Media.Audiofx;
using Microsoft.Maui.Graphics;

namespace PSiptv.Views;

// Reads Android's low-resolution playback mix only while a radio is playing.
// No samples are stored or transmitted.
internal sealed class RadioSpectrumView : Grid
{
    private readonly SpectrumDrawing drawing = new();
    private readonly GraphicsView graph;
    private readonly Label status;
    private readonly IDispatcherTimer timer;
    private Visualizer? capture;
    private byte[]? waveform;
    private byte[]? fft;
    private int generation;
    private bool starting;

    public RadioSpectrumView()
    {
        IsVisible = false;
        InputTransparent = true;
        BackgroundColor = Color.FromArgb("#081321");
        graph = new GraphicsView { Drawable = drawing, InputTransparent = true };
        Add(graph);
        status = new Label
        {
            Text = "A aguardar áudio…",
            TextColor = Colors.White,
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        Add(status);
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(40);
        timer.Tick += (_, _) => Sample();
    }

    public async Task StartAsync()
    {
        if (capture is not null || starting) return;
        starting = true;
        var request = generation;
        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.Microphone>();
            if (request != generation) return;
            if (permission != PermissionStatus.Granted)
            {
                status.Text = "Autorize a permissão de áudio para mostrar o espectro.";
                return;
            }

            var visualizer = new Visualizer(0);
            try
            {
                var range = Visualizer.GetCaptureSizeRange() ?? [512, 512];
                visualizer.SetCaptureSize(Math.Clamp(512, range[0], range[1]));
                visualizer.SetScalingMode(VisualizerScalingMode.Normalized);
                visualizer.SetEnabled(true);
                capture = visualizer;
                waveform = new byte[visualizer.CaptureSize];
                fft = new byte[visualizer.CaptureSize];
                status.IsVisible = false;
                timer.Start();
            }
            catch
            {
                try { visualizer.Release(); }
                finally { visualizer.Dispose(); }
                throw;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Radio spectrum unavailable: {ex}");
            if (request == generation)
            {
                status.Text = "Espectro indisponível neste dispositivo.";
                status.IsVisible = true;
            }
        }
        finally { starting = false; }
    }

    public void Stop()
    {
        ++generation;
        timer.Stop();
        if (capture is { } visualizer)
        {
            capture = null;
            try { visualizer.SetEnabled(false); }
            catch (Exception) { /* The audio system can release an effect first. */ }
            try { visualizer.Release(); }
            catch (Exception) { /* Playback must continue if the effect was already released. */ }
            finally { visualizer.Dispose(); }
        }
        waveform = fft = null;
        drawing.Reset();
        graph.Invalidate();
        status.Text = "A aguardar áudio…";
        status.IsVisible = true;
    }

    private void Sample()
    {
        if (capture is not { } visualizer || waveform is null || fft is null) return;
        try
        {
            if (visualizer.GetWaveForm(waveform) != VisualizerStatus.Success ||
                visualizer.GetFft(fft) != VisualizerStatus.Success) return;
            drawing.Update(waveform, fft);
            graph.Invalidate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Radio spectrum capture stopped: {ex}");
            Stop();
            status.Text = "Espectro indisponível neste dispositivo.";
        }
    }

    private sealed class SpectrumDrawing : IDrawable
    {
        private readonly float[] bands = new float[48];
        private readonly float[] wave = new float[96];

        public void Reset()
        {
            Array.Clear(bands);
            Array.Clear(wave);
        }

        public void Update(byte[] samples, byte[] frequencies)
        {
            for (var i = 0; i < wave.Length; i++)
                wave[i] = (samples[i * samples.Length / wave.Length] - 128) / 128f;

            var bins = frequencies.Length / 2;
            for (var i = 0; i < bands.Length; i++)
            {
                var first = Math.Clamp((int)Math.Pow(bins - 1, (double)i / bands.Length), 1, bins - 1);
                var last = Math.Clamp((int)Math.Pow(bins - 1, (double)(i + 1) / bands.Length), first, bins - 1);
                var peak = 0d;
                for (var bin = first; bin <= last; bin++)
                {
                    var real = unchecked((sbyte)frequencies[bin * 2]);
                    var imaginary = unchecked((sbyte)frequencies[bin * 2 + 1]);
                    peak = Math.Max(peak, Math.Sqrt(real * real + imaginary * imaginary));
                }
                var level = (float)Math.Clamp(Math.Log10(1 + peak) / 2.3, 0, 1);
                bands[i] = Math.Max(level, bands[i] * 0.84f);
            }
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var width = dirtyRect.Width;
            var height = dirtyRect.Height;
            canvas.FillColor = Color.FromArgb("#081321");
            canvas.FillRectangle(dirtyRect);
            if (width <= 0 || height <= 0) return;

            var waveCenter = height * 0.27f;
            var waveHeight = height * 0.18f;
            canvas.StrokeColor = Color.FromArgb("#3BE8DE");
            canvas.StrokeSize = 2.5f;
            for (var i = 1; i < wave.Length; i++)
                canvas.DrawLine(width * (i - 1) / (wave.Length - 1), waveCenter + wave[i - 1] * waveHeight,
                    width * i / (wave.Length - 1), waveCenter + wave[i] * waveHeight);

            var bottom = height * 0.91f;
            var maxHeight = height * 0.46f;
            var step = width / bands.Length;
            for (var i = 0; i < bands.Length; i++)
            {
                var barHeight = Math.Max(2f, bands[i] * maxHeight);
                canvas.FillColor = i < bands.Length / 3 ? Color.FromArgb("#35D9F1")
                    : i < bands.Length * 2 / 3 ? Color.FromArgb("#7C7BFF")
                    : Color.FromArgb("#D067F1");
                canvas.FillRoundedRectangle(i * step + 1, bottom - barHeight,
                    Math.Max(2, step - 2), barHeight, Math.Min(4, step / 3));
            }
        }
    }
}
#endif
