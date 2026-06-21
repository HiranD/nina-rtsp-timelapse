using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>
    /// Plugin entry point + options page view model. The options DataTemplate
    /// (key "RTSP Timelapse Control_Options") binds to this instance.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class RtspTimelapsePlugin : PluginBase, INotifyPropertyChanged {
        private readonly IPluginOptionsAccessor pluginSettings;

        [ImportingConstructor]
        public RtspTimelapsePlugin(IProfileService profileService, IOptionsVM options) {
            pluginSettings = new PluginOptionsAccessor(profileService, PluginConstants.PluginId);
            TestConnectionCommand = new AsyncCommand<bool>(TestConnectionAsync);
        }

        /// <summary>API port; must match the RTSP Timelapse app's Integrations tab.</summary>
        public int Port {
            get => pluginSettings.GetValueInt32(PluginConstants.PortKey, PluginConstants.DefaultPort);
            set {
                var clamped = Math.Min(65535, Math.Max(1024, value));
                pluginSettings.SetValueInt32(PluginConstants.PortKey, clamped);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(BaseUrl));
            }
        }

        public string BaseUrl => $"http://127.0.0.1:{Port}";

        private string testResult;
        public string TestResult {
            get => testResult;
            private set { testResult = value; RaisePropertyChanged(); }
        }

        public ICommand TestConnectionCommand { get; }

        private async Task<bool> TestConnectionAsync() {
            try {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10))) {
                    var health = await new RtspApiClient(Port).GetHealthAsync(cts.Token);
                    TestResult = health != null && health.Ok
                        ? $"Connected — RTSP Timelapse v{health.Version}"
                        : "Reached the server, but it did not report OK.";
                }
            } catch (Exception ex) {
                TestResult = ex.Message;
            }
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
