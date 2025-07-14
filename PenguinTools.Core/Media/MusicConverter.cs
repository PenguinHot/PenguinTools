using PenguinTools.Common;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;

namespace PenguinTools.Core.Media;

public class MusicConverter : IConverter<MusicConverter.Context>
{
    public Task<bool> CanConvertAsync(Context context, IDiagnostic diag)
    {
        if (context.Meta.Id is null) diag.Report(Severity.Error, Strings.Error_song_id_is_not_set);
        if (context.Meta.BgmPreviewStop < context.Meta.BgmPreviewStart) diag.Report(Severity.Error, Strings.Error_preview_stop_greater_than_start);
        var path = context.Meta.FullBgmFilePath;
        if (!File.Exists(path)) diag.Report(Severity.Error, Strings.Error_file_not_found, path);
        return Task.FromResult(!diag.HasError);
    }

    public async Task ConvertAsync(Context context, IDiagnostic diag, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!await CanConvertAsync(context, diag)) return;
        var songId = context.Meta.Id ?? throw new DiagnosticException(Strings.Error_song_id_is_not_set);

        progress?.Report(Strings.Status_converting_audio);
        if (context.Meta.BgmPreviewStart > 120) diag.Report(Severity.Warning, Strings.Diag_pv_laterthan_120);

        var xml = new CueFileXml(songId);
        var outputDir = await xml.SaveDirectoryAsync(context.DestinationFolder);

        var pvStart = (double)context.Meta.BgmPreviewStart;
        var pvStop = (double)context.Meta.BgmPreviewStop;
        var acbPath = Path.Combine(outputDir, xml.AcbFile);
        var awbPath = Path.Combine(outputDir, xml.AwbFile);
        // await CriwareConverter.CreateAsync(realPath, xml.DataName, acbPath, awbPath, pvStart, pvStop, ct);
        // TODO
        ct.ThrowIfCancellationRequested();
    }

    public record Context(Meta Meta, string DestinationFolder);
}