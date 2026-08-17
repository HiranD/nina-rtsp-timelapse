using System;
using System.Collections.Generic;
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

            StateEventToggles = BuildToggles(SessionEventCatalog.StateToggles);
            InstructionEventToggles = BuildToggles(SessionEventCatalog.InstructionToggles);

            // The options page stays open across profile switches. The accessor always writes to
            // the ACTIVE profile, so without this refresh the controls would display the old
            // profile's values while clicks silently write to the new one.
            profileService.ProfileChanged += (s, e) => {
                RaisePropertyChanged(nameof(Port));
                RaisePropertyChanged(nameof(BaseUrl));
                foreach (var toggle in StateEventToggles) { toggle.RefreshFromProfile(); }
                foreach (var toggle in InstructionEventToggles) { toggle.RefreshFromProfile(); }
            };
        }

        /// <summary>Session-event checkboxes for the options page - see SessionEventCatalog.</summary>
        public IReadOnlyList<SessionEventToggleViewModel> StateEventToggles { get; }

        public IReadOnlyList<SessionEventToggleViewModel> InstructionEventToggles { get; }

        private IReadOnlyList<SessionEventToggleViewModel> BuildToggles(
                IReadOnlyList<SessionEventToggle> descriptors) {
            var items = new List<SessionEventToggleViewModel>(descriptors.Count);
            foreach (var descriptor in descriptors) {
                items.Add(new SessionEventToggleViewModel(descriptor, pluginSettings));
            }
            return items;
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
