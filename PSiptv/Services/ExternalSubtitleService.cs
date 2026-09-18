using System.Text;
using PSiptv.Core;

namespace PSiptv.Services;

public static class ExternalSubtitleService
{
    public static async Task<IReadOnlyList<SubtitleCue>> PickAsync(CancellationToken token)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = LanguageService.Text("Escolher legendas SRT ou WebVTT")
        });
        if (file is null) return [];
        if (!ExternalSubtitleParser.SupportsFileName(file.FileName))
            throw new InvalidOperationException("Escolha um ficheiro de legendas .srt ou .vtt.");
        await using var stream = await file.OpenReadAsync();
        return ExternalSubtitleParser.Parse(await ReadAsync(stream, token));
    }

    public static async Task<IReadOnlyList<SubtitleCue>> LoadUrlAsync(string url, CancellationToken token)
    {
        var address = WebAddress.Require(url).AbsoluteUri;
        var text = await AppServices.Client.DownloadAsync(address, token, ExternalSubtitleParser.MaximumBytes);
        return ExternalSubtitleParser.Parse(text);
    }

    private static async Task<string> ReadAsync(Stream stream, CancellationToken token)
    {
        if (stream.CanSeek && stream.Length > ExternalSubtitleParser.MaximumBytes)
            throw new InvalidOperationException("O ficheiro de legendas excede o limite de 5 MB.");
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > ExternalSubtitleParser.MaximumBytes)
                throw new InvalidOperationException("O ficheiro de legendas excede o limite de 5 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        var bytes = output.ToArray();
        return bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe
            ? Encoding.Unicode.GetString(bytes)
            : bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff
                ? Encoding.BigEndianUnicode.GetString(bytes)
                : Encoding.UTF8.GetString(bytes);
    }
}
