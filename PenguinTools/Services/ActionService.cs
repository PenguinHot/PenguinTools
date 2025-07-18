﻿using CommunityToolkit.Mvvm.ComponentModel;
using PenguinTools.Common;
using PenguinTools.Controls;
using PenguinTools.Core;
using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace PenguinTools.Services;

public partial class ActionService : ObservableObject
{
    protected static Dispatcher Dispatcher => Application.Current.Dispatcher;

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = Strings.Status_idle;
    [ObservableProperty] public partial DateTime StatusTime { get; set; } = DateTime.Now;

    public bool CanRun()
    {
        return !IsBusy;
    }

    public async Task RunAsync(Func<IDiagnostic, IProgress<string>, CancellationToken, Task> action, CancellationToken? externalToken = null)
    {
        if (!CanRun()) return;
        var diagnostics = new DiagnosticReporter();
        IsBusy = true;

        var progress = new Progress<string>(s =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                Status = s;
                StatusTime = DateTime.Now;
            });
        });
        IProgress<string> ip = progress;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken ?? CancellationToken.None);

            ip.Report(Strings.Status_starting);
            await Task.Run(() => action(diagnostics, progress, cts.Token), cts.Token);
            ip.Report(Strings.Status_done);

            SystemSounds.Exclamation.Play();
        }
        catch (Exception ex)
        {
            diagnostics.Report(ex);
        }
        finally
        {
            IsBusy = false;
        }

        var model = new DiagnosticsWindowViewModel
        {
            Diagnostics = [..diagnostics.Diagnostics]
        };

        if (!diagnostics.HasProblem) return;
        if (diagnostics.HasError) ip.Report(Strings.Status_error);

        var window = new DiagnosticsWindow
        {
            DataContext = model,
            Owner = App.MainWindow
        };
        window.ShowDialog();
    }
}