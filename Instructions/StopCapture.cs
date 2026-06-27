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
    [ExportMetadata("Description", "Stops RTSP timelapse capture via the app's local HTTP API, then optionally renders the video for this session. Stop is a no-op if not capturing.")]
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
            CreateVideoAfterStop = copyMe.CreateVideoAfterStop;
        }

        private bool createVideoAfterStop = true;

        /// <summary>
        /// When true, render the timelapse video after stopping - only this session's frames, using
        /// the session start the app reports on /status. The app uploads to Discord too, if configured.
        /// </summary>
        [JsonProperty]
        public bool CreateVideoAfterStop {
            get => createVideoAfterStop;
            set { createVideoAfterStop = value; RaisePropertyChanged(); }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);

            // Read the app-owned session start BEFORE stopping (it survives the stop), so the render
            // covers exactly this session's frames - even when the plugin's Start was a no-op because
            // the app button or scheduler started the capture. Null (e.g. after an app restart) falls
            // back to rendering the whole newest folder.
            var status = await client.GetStatusAsync(token);
            var since = RtspSession.MarginedSince(status.SessionStart);
            if (status.Capturing) {
                await client.StopCaptureAsync(token);
            } else {
                Logger.Info("RTSP Timelapse is not capturing; Stop is a no-op.");
            }

            if (CreateVideoAfterStop) {
                await client.CreateVideoAsync(since, token);
            }
        }

        public override object Clone() => new StopCapture(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(StopCapture)}";
    }
}
