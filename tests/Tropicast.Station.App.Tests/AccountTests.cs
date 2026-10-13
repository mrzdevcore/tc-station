using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Station.App.ViewModels;
using Tropicast.Station.App.Views;
using Tropicast.Station.Audio;
using Tropicast.Station.Core.Account;
using Tropicast.Station.Core.Broadcasting;
using Tropicast.Station.Core.Profiles;
using Tropicast.Station.Encoding;
using Tropicast.Station.Tests;

namespace Tropicast.Station.App.Tests;

/// <summary>From a fresh install: sign in, pick a station, Go Live, without typing a host, mount or password.</summary>
public sealed class AccountTests
{
    private sealed class Browser : IExternalBrowser
    {
        internal List<Uri> Opened { get; } = [];

        public void Open(Uri url) => Opened.Add(url);
    }

    private sealed class RecordingEncoder : IBroadcastEncoder
    {
        internal List<BroadcastTarget> Targets { get; } = [];

        public Task<IEncoderSession> StartAsync(BroadcastTarget target, EncoderOptions? options = null, CancellationToken cancellationToken = default)
        {
            Targets.Add(target);
            return Task.FromResult<IEncoderSession>(new IdleSession());
        }
    }

    private sealed class IdleSession : IEncoderSession
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public EncoderSnapshot Snapshot { get; private set; } = new(EncoderState.Starting, "Starting", 0);
        public Task Completion => _completion.Task;
        public void Submit(PcmFrame frame) => Snapshot = new(EncoderState.Streaming, "Live", frame.Data.Length);
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _completion.TrySetResult();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            _completion.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Answers like the real HTTP client: on a thread-pool thread, not the caller's.</summary>
    private sealed class ThreadPoolApi(IDesktopApi inner) : IDesktopApi
    {
        public Task<DeviceSignIn> StartSignInAsync(string deviceName, CancellationToken cancellationToken = default)
            => Task.Run(() => inner.StartSignInAsync(deviceName, cancellationToken), cancellationToken);
        public Task<SignInPoll> PollSignInAsync(string deviceSecret, CancellationToken cancellationToken = default)
            => Task.Run(() => inner.PollSignInAsync(deviceSecret, cancellationToken), cancellationToken);
        public Task<TokenPair> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
            => Task.Run(() => inner.RefreshAsync(refreshToken, cancellationToken), cancellationToken);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
            => Task.Run(() => inner.RevokeAsync(refreshToken, cancellationToken), cancellationToken);
        public Task<IReadOnlyList<AccountStation>> GetStationsAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.Run(() => inner.GetStationsAsync(accessToken, cancellationToken), cancellationToken);
        public Task<IssuedStationTarget> GetBroadcastTargetAsync(string accessToken, Guid tenantId, Guid stationId,
            CancellationToken cancellationToken = default)
            => Task.Run(() => inner.GetBroadcastTargetAsync(accessToken, tenantId, stationId, cancellationToken), cancellationToken);
    }

    [AvaloniaFact]
    public async Task Sign_in_shows_the_code_on_the_UI_thread_when_the_API_answers_from_another_thread()
    {
        var secrets = new MemorySecrets();
        var browser = new Browser();
        using var host = AppHost.Create([], services =>
        {
            services.AddSingleton<IProfileStore, MemoryProfiles>();
            services.AddSingleton<ISecretStore>(secrets);
            services.AddSingleton<IAudioCaptureProvider, ToneAudioCaptureProvider>();
            services.AddSingleton<IExternalBrowser>(browser);
            services.AddSingleton(new AccountService(new ThreadPoolApi(new FakeDesktopApi()), new MemoryAccountStore(), secrets,
                new FastTime()));
        });
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        await UntilAsync(() => !model.Account.IsBusy);
        List<string> offThread = [];
        model.Account.PropertyChanged += (_, e) =>
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                offThread.Add(e.PropertyName ?? "");
            }
        };

        await model.Account.SignInCommand.ExecuteAsync(null);

        Assert.Empty(offThread);
        Assert.Equal("Signed in.", model.Account.Status);
        await UntilAsync(() => model.Account.IsSignedIn);
        Assert.Equal([new Uri("https://app.test/device?code=BCDF-GHJK")], browser.Opened);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Sign_in_pick_a_station_and_go_live_without_typing_any_connection_setting()
    {
        var api = new FakeDesktopApi();
        var secrets = new MemorySecrets();
        var browser = new Browser();
        var encoder = new RecordingEncoder();
        using var host = AppHost.Create([], services =>
        {
            services.AddSingleton<IProfileStore, MemoryProfiles>();
            services.AddSingleton<ISecretStore>(secrets);
            services.AddSingleton<IAudioCaptureProvider, ToneAudioCaptureProvider>();
            services.AddSingleton<IBroadcastEncoder>(encoder);
            services.AddSingleton<IExternalBrowser>(browser);
            services.AddSingleton(new AccountService(api, new MemoryAccountStore(), secrets, new FastTime()));
        });
        var window = host.Services.GetRequiredService<MainWindow>();
        window.Show();
        var model = host.Services.GetRequiredService<MainViewModel>();
        await UntilAsync(() => !model.Account.IsBusy && model.Audio.Inputs.Count > 0);
        Assert.True(window.Find<Button>("SignInButton")!.IsEffectivelyVisible);
        Assert.False(model.Broadcast.GoLiveCommand.CanExecute(null));

        await model.Account.SignInCommand.ExecuteAsync(null);
        await UntilAsync(() => model.Account.SelectedStation is not null);

        Assert.Equal([new Uri("https://app.test/device?code=BCDF-GHJK")], browser.Opened);
        Assert.True(model.Broadcast.UseStation);
        Assert.Equal("Radio Mada (My radios)", model.Account.SelectedStation!.DisplayName);
        Assert.True(window.Find<ComboBox>("StationPicker")!.IsEffectivelyVisible);
        Assert.Contains("Radio Mada", model.Broadcast.StreamSettings, StringComparison.Ordinal);

        model.Audio.SelectedInput = model.Audio.Inputs[0];
        await model.Broadcast.GoLiveCommand.ExecuteAsync(null);
        await UntilAsync(() => model.Broadcast.State == BroadcastState.Live);

        Assert.Equal(["/stations/k3m9x2p7qa/live.mp3", "/stations/k3m9x2p7qa/live.opus"], encoder.Targets.Select(t => t.Profile.Mount));
        Assert.All(encoder.Targets, t => Assert.Equal(("ingest.tropicastradio.com", 443, true, "station-password-1"),
            (t.Profile.Host, t.Profile.Port, t.Profile.UseTls, t.Password)));
        Assert.False(model.Account.CanChooseStation);
        Assert.False(model.Account.SignOutCommand.CanExecute(null));

        // Every account control is locked while live, including sign-in for a signed-out panel.
        model.Account.IsSignedIn = false;
        Assert.False(model.Account.SignInCommand.CanExecute(null));
        model.Account.IsSignedIn = true;

        await host.Services.GetRequiredService<BroadcastController>().StopAsync(TestContext.Current.CancellationToken);
        await UntilAsync(() => model.Broadcast.State == BroadcastState.Idle);
        await model.Account.SignOutCommand.ExecuteAsync(null);
        Assert.False(model.Account.IsSignedIn);
        Assert.False(model.Broadcast.UseStation);
        Assert.Empty(secrets.Items);
        window.Close();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, deadline.Token);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
