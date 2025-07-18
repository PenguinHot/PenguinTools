﻿using PenguinTools.Common;

namespace PenguinTools.Core.Media;

public class JacketConverter : IConverter<JacketConverter.Context>
{
    public async Task ConvertAsync(Context context, IDiagnostic diag, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!await CanConvertAsync(context, diag)) return;
        progress?.Report(Strings.Status_converting_jacket);
        ct.ThrowIfCancellationRequested();
        MuaInterop.ConvertJk(context.InputPath, context.OutputPath);
        ct.ThrowIfCancellationRequested();
    }

    public Task<bool> CanConvertAsync(Context context, IDiagnostic diag)
    {
        if (!File.Exists(context.InputPath)) diag.Report(Severity.Error, Strings.Error_file_not_found, context.InputPath);
        return Task.FromResult(!diag.HasError);
    }

    public record Context(string InputPath, string OutputPath);
}