using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;

namespace NINA.RtspTimelapse.Plugin.Instructions {

    [ExportMetadata("Name", "Create Timelapse Video")]
    [ExportMetadata("Description", "Renders the timelapse video for the newest capture session via the app's local HTTP API. Honours the app's Discord upload settings. Fire-and-forget: completes once accepted, not when rendering finishes.")]
    [ExportMetadata("Icon", "RtspTimelapse_SVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CreateVideo : SequenceItem {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public CreateVideo(IProfileService profileService) {
            this.profileService = profileService;
        }

        private CreateVideo(CreateVideo copyMe) : this(copyMe.profileService) {
            CopyMetaData(copyMe);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);
            await client.CreateVideoAsync(null, token);
        }

        public override object Clone() => new CreateVideo(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(CreateVideo)}";
    }
}
