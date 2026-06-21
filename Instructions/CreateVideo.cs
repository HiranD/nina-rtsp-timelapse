using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;

namespace NINA.RtspTimelapse.Plugin.Instructions {

    [ExportMetadata("Name", "Create Timelapse Video")]
    [ExportMetadata("Description", "Renders the timelapse video via the app's local HTTP API. Leave Date blank for the newest session. Fire-and-forget: completes once accepted, not when rendering finishes.")]
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
            Date = copyMe.Date;
        }

        private string date = string.Empty;

        /// <summary>Session date as YYYYMMDD. Blank = newest session.</summary>
        [JsonProperty]
        public string Date {
            get => date;
            set { date = value; RaisePropertyChanged(); }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!string.IsNullOrWhiteSpace(Date) &&
                !DateTime.TryParseExact(Date.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out _)) {
                throw new Exception($"Invalid date '{Date}'. Expected YYYYMMDD (e.g. 20250620) or blank for the newest session.");
            }

            var client = RtspApiClient.FromProfile(profileService);
            await client.CreateVideoAsync(Date, token);
        }

        public override object Clone() => new CreateVideo(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(CreateVideo)}, Date: {Date}";
    }
}
