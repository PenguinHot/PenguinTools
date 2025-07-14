using PenguinTools.Common;
using PenguinTools.Core.Metadata;
using System.Diagnostics;
using System.Text.Json;

namespace PenguinTools.Core.Media;

public static class MediaHandler
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public async static Task<string> ConvertAudio(Meta meta, IProgress<string>? p, CancellationToken ct = default)
    {
       
    }

    public async static Task<AudioStreamInfo> AnalyzeAudioAsync(string inPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "PenguinMedia",
            Arguments = $"audio probe \"{inPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var stdOut = await process.StandardOutput.ReadToEndAsync();

        if (process.ExitCode != 0)
        {
            var formatted = $"{process.ExitCode} - {stdOut.Trim()}";
            throw new DiagnosticException(string.Format(Strings.Error_ffmpeg_failed, formatted));
        }

        AudioFileInfo info;
        try
        {
            info = AudioFileInfo.Parse(stdOut) ?? throw new DiagnosticException(string.Format(Strings.Error_ffprobe_failed, await process.StandardError.ReadToEndAsync()));
            if (info.Count == 0) throw new DiagnosticException(Strings.Error_no_audio_stream);
        }
        catch (JsonException ex)
        {
            throw new DiagnosticException(string.Format(Strings.Error_ffprobe_failed_parse, ex.Message, stdOut));
        }

        return info[0];
    }
}