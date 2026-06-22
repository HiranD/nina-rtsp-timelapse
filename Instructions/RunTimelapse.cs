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

    [ExportMetadata("Name", "Auto Timelapse")]
    [ExportMetadata("Description", "Starts RTSP timelapse capture, then automatically stops it (and optionally renders the video) when the sequence ends. Put one of these at the start of your sequence. The rendered video contains only this session's frames.")]
    [ExportMetadata("Icon", "RtspTimelapseRun_SVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class RunTimelapse : SequenceItem {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public RunTimelapse(IProfileService profileService) {
            this.profileService = profileService;
        }

        private RunTimelapse(RunTimelapse copyMe) : this(copyMe.profileService) {
            CopyMetaData(copyMe);
            WaitUntilCapturing = copyMe.WaitUntilCapturing;
            CreateVideoWhenDone = copyMe.CreateVideoWhenDone;
        }

        private bool waitUntilCapturing = true;

        [JsonProperty]
        public bool WaitUntilCapturing {
            get => waitUntilCapturing;
            set { waitUntilCapturing = value; RaisePropertyChanged(); }
        }

        private bool createVideoWhenDone = true;

        [JsonProperty]
        public bool CreateVideoWhenDone {
            get => createVideoWhenDone;
            set { createVideoWhenDone = value; RaisePropertyChanged(); }
        }

        // Runtime state (not serialized): set in Execute, consumed in Teardown.
        private bool started;
        private string since;

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);

            var status = await client.GetStatusAsync(token);
            if (status.Capturing) {
                Logger.Info("RTSP Timelapse already capturing; Auto Timelapse will not manage this session.");
                return;
            }

            // Record the start instant so the end-of-sequence video renders only this session's frames.
            since = RtspSession.FormatSince(DateTime.Now);
            RtspSession.LastStartSince = since;

            await client.StartCaptureAsync(token);
            started = true;

            if (WaitUntilCapturing) {
                await client.WaitForFirstFrameAsync(token);
            }
        }

        // N.I.N.A. calls Teardown() once at the very end of the sequence run - including
        // after a user abort. We stop the capture this block started and optionally render
        // the session's video. Must be idempotent and never throw.
        public override async void Teardown() {
            base.Teardown();
            if (!started) {
                return;
            }
            started = false;
            try {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
                    var client = RtspApiClient.FromProfile(profileService);
                    var status = await client.GetStatusAsync(cts.Token);
                    if (status.Capturing) {
                        await client.StopCaptureAsync(cts.Token);
                    }
                    if (CreateVideoWhenDone) {
                        await client.CreateVideoAsync(since, cts.Token);
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"Auto Timelapse teardown failed: {ex.Message}");
            }
        }

        public override object Clone() => new RunTimelapse(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(RunTimelapse)}";
    }
}
