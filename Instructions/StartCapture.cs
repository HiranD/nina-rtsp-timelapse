using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;

namespace NINA.RtspTimelapse.Plugin.Instructions {

    [ExportMetadata("Name", "Start Timelapse Capture")]
    [ExportMetadata("Description", "Starts RTSP timelapse capture via the app's local HTTP API. No-op if already capturing.")]
    [ExportMetadata("Icon", "RtspTimelapse_SVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StartCapture : SequenceItem {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public StartCapture(IProfileService profileService) {
            this.profileService = profileService;
        }

        private StartCapture(StartCapture copyMe) : this(copyMe.profileService) {
            CopyMetaData(copyMe);
            WaitUntilCapturing = copyMe.WaitUntilCapturing;
        }

        private bool waitUntilCapturing = true;

        /// <summary>When true, poll /status until the app reports capturing before completing.</summary>
        [JsonProperty]
        public bool WaitUntilCapturing {
            get => waitUntilCapturing;
            set { waitUntilCapturing = value; RaisePropertyChanged(); }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);

            var status = await client.GetStatusAsync(token);
            if (status.Capturing) {
                Logger.Info("RTSP Timelapse is already capturing; Start Timelapse Capture is a no-op.");
                return;
            }

            // Remember the start instant so a later Create Video step renders only this session's frames.
            RtspSession.LastStartSince = RtspSession.FormatSince(DateTime.Now);

            await client.StartCaptureAsync(token);

            if (WaitUntilCapturing) {
                await WaitUntilCapturingAsync(client, token);
            }
        }

        // /capture/start returns 202 (accepted) immediately, so confirm it actually started.
        private static async Task WaitUntilCapturingAsync(RtspApiClient client, CancellationToken token) {
            for (var attempt = 0; attempt < 20; attempt++) {
                token.ThrowIfCancellationRequested();
                var status = await client.GetStatusAsync(token);
                if (status.Capturing) {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }
            throw new Exception("RTSP capture did not report 'capturing' within ~10 seconds of starting.");
        }

        public override object Clone() => new StartCapture(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(StartCapture)}";
    }
}
