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

    [ExportMetadata("Name", "Stop Timelapse Capture")]
    [ExportMetadata("Description", "Stops RTSP timelapse capture via the app's local HTTP API. No-op if not capturing.")]
    [ExportMetadata("Icon", "RtspTimelapse_SVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StopCapture : SequenceItem {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public StopCapture(IProfileService profileService) {
            this.profileService = profileService;
        }

        private StopCapture(StopCapture copyMe) : this(copyMe.profileService) {
            CopyMetaData(copyMe);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);

            var status = await client.GetStatusAsync(token);
            if (!status.Capturing) {
                Logger.Info("RTSP Timelapse is not capturing; Stop Timelapse Capture is a no-op.");
                return;
            }

            await client.StopCaptureAsync(token);
        }

        public override object Clone() => new StopCapture(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(StopCapture)}";
    }
}
