using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;

namespace NINA.RtspTimelapse.Plugin.Dockable {

    /// <summary>
    /// Imaging-tab dock panel showing live RTSP Timelapse status, with manual
    /// Start / Stop / Create-Video buttons. Polls the app's /status while visible.
    /// </summary>
    [Export(typeof(IDockableVM))]
    public class RtspStatusDockable : DockableVM {
        // Note: profileService is inherited (protected) from BaseVM.
        private readonly DispatcherTimer timer;

        [ImportingConstructor]
        public RtspStatusDockable(IProfileService profileService) : base(profileService) {
            Title = "RTSP Timelapse";

            if (Application.Current != null &&
                Application.Current.TryFindResource("RtspTimelapse_SVG") is GeometryGroup geometry) {
                ImageGeometry = geometry;
            }

            RefreshCommand = new AsyncCommand<bool>(async () => { await RefreshAsync(CancellationToken.None); return true; });
            StartCommand = new AsyncCommand<bool>(() => RunAsync((c, t) => c.StartCaptureAsync(t)));
            StopCommand = new AsyncCommand<bool>(() => RunAsync((c, t) => c.StopCaptureAsync(t)));
            CreateVideoCommand = new AsyncCommand<bool>(() => RunAsync((c, t) => c.CreateVideoAsync(null, t)));

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += OnTick;
            timer.Start();
        }

        public ICommand RefreshCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand CreateVideoCommand { get; }

        private bool connected;
        public bool Connected { get => connected; private set { connected = value; RaisePropertyChanged(); } }

        private string appVersion;
        public string AppVersion { get => appVersion; private set { appVersion = value; RaisePropertyChanged(); } }

        // Computed for the view (avoids needing value converters in XAML).
        private string connectionText = "Not connected";
        public string ConnectionText { get => connectionText; private set { connectionText = value; RaisePropertyChanged(); } }

        private Brush connectionBrush = Brushes.Tomato;
        public Brush ConnectionBrush { get => connectionBrush; private set { connectionBrush = value; RaisePropertyChanged(); } }

        private bool capturing;
        public bool Capturing { get => capturing; private set { capturing = value; RaisePropertyChanged(); } }

        private string state;
        public string State { get => state; private set { state = value; RaisePropertyChanged(); } }

        private int frameCount;
        public int FrameCount { get => frameCount; private set { frameCount = value; RaisePropertyChanged(); } }

        private int failedFrameCount;
        public int FailedFrameCount { get => failedFrameCount; private set { failedFrameCount = value; RaisePropertyChanged(); } }

        private string uptime;
        public string Uptime { get => uptime; private set { uptime = value; RaisePropertyChanged(); } }

        private string lastError;
        public string LastError {
            get => lastError;
            private set { lastError = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ErrorText)); }
        }

        private string error;
        public string Error {
            get => error;
            private set { error = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ErrorText)); }
        }

        /// <summary>Combined error line for the view (empty when there is nothing to show).</summary>
        public string ErrorText {
            get {
                if (!string.IsNullOrWhiteSpace(Error)) { return Error; }
                if (!string.IsNullOrWhiteSpace(LastError)) { return $"Last error: {LastError}"; }
                return string.Empty;
            }
        }

        // Guards against overlapping polls: a slow/stalled app must not let the 3s timer pile up
        // concurrent requests. All access is on the UI thread (DispatcherTimer), so a plain bool is safe.
        private bool isRefreshing;

        private async void OnTick(object sender, EventArgs e) {
            if (!IsVisible || isRefreshing) {
                return;
            }
            isRefreshing = true;
            try {
                // Bound each poll so a stalled connection can't hang for the full 30s HttpClient
                // timeout (and block the next ticks) - mirrors the timeout used by the manual buttons.
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10))) {
                    await RefreshAsync(cts.Token);
                }
            } finally {
                isRefreshing = false;
            }
        }

        private async Task<bool> RunAsync(Func<RtspApiClient, CancellationToken, Task> action) {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20))) {
                try {
                    var client = RtspApiClient.FromProfile(profileService);
                    await action(client, cts.Token);
                    await RefreshAsync(cts.Token);
                    return true;
                } catch (Exception ex) {
                    Error = ex.Message;
                    Logger.Error(ex);
                    return false;
                }
            }
        }

        private async Task RefreshAsync(CancellationToken token) {
            try {
                var client = RtspApiClient.FromProfile(profileService);
                var health = await client.GetHealthAsync(token);
                var status = await client.GetStatusAsync(token);

                Connected = health != null && health.Ok;
                AppVersion = health?.Version;
                ConnectionText = Connected ? $"Connected (app v{AppVersion})" : "Reachable, but not OK";
                ConnectionBrush = Connected ? Brushes.LimeGreen : Brushes.Orange;
                Capturing = status.Capturing;
                State = status.State;
                FrameCount = status.FrameCount;
                FailedFrameCount = status.FailedFrameCount;
                Uptime = TimeSpan.FromSeconds(status.UptimeSeconds).ToString(@"hh\:mm\:ss");
                LastError = status.LastError;
                Error = null;
            } catch (Exception ex) {
                Connected = false;
                AppVersion = null;
                ConnectionText = "Not connected";
                ConnectionBrush = Brushes.Tomato;
                Error = ex.Message;
            }
        }
    }
}
