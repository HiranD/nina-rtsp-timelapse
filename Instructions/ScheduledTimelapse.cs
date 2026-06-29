using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility.DateTimeProvider;

namespace NINA.RtspTimelapse.Plugin.Instructions {

    [ExportMetadata("Name", "Scheduled Timelapse")]
    [ExportMetadata("Description", "Starts RTSP timelapse capture and tells the app to auto-stop at a chosen time (e.g. Nautical Dawn + offset) and optionally render. No Stop block needed - the app owns the timer, so it stops at the time even if the sequence is stopped. Non-blocking: the sequence continues immediately.")]
    [ExportMetadata("Icon", "ClockSVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class ScheduledTimelapse : SequenceItem {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public ScheduledTimelapse(IProfileService profileService, IList<IDateTimeProvider> dateTimeProviders) {
            this.profileService = profileService;
            DateTimeProviders = dateTimeProviders;
            SelectedProvider = dateTimeProviders?.FirstOrDefault();
        }

        private ScheduledTimelapse(ScheduledTimelapse copyMe) : this(copyMe.profileService, copyMe.DateTimeProviders) {
            CopyMetaData(copyMe);
            Hours = copyMe.Hours;
            Minutes = copyMe.Minutes;
            Seconds = copyMe.Seconds;
            MinutesOffset = copyMe.MinutesOffset;
            CreateVideoWhenDone = copyMe.CreateVideoWhenDone;
            WaitUntilCapturing = copyMe.WaitUntilCapturing;
            SelectedProvider = copyMe.SelectedProvider;
        }

        // The provider list is injected by NINA (same one Wait for Time uses); not serialized.
        public IList<IDateTimeProvider> DateTimeProviders { get; }

        private IDateTimeProvider selectedProvider;
        public IDateTimeProvider SelectedProvider {
            get => selectedProvider;
            set {
                selectedProvider = value;
                selectedProviderName = value?.Name;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasFixedTimeProvider));
                RaisePropertyChanged(nameof(IsManualTime));
                RaisePropertyChanged(nameof(OffsetVisibility));
                UpdateTime();
            }
        }

        // The selected provider round-trips by Name (resolved back from the injected list on load).
        private string selectedProviderName;
        [JsonProperty]
        public string SelectedProviderName {
            get => selectedProviderName;
            set => selectedProviderName = value;
        }

        private int hours;
        [JsonProperty]
        public int Hours {
            get => hours;
            set { hours = Math.Max(0, Math.Min(23, value)); RaisePropertyChanged(); }
        }

        private int minutes;
        [JsonProperty]
        public int Minutes {
            get => minutes;
            set { minutes = Math.Max(0, Math.Min(59, value)); RaisePropertyChanged(); }
        }

        private int seconds;
        [JsonProperty]
        public int Seconds {
            get => seconds;
            set { seconds = Math.Max(0, Math.Min(59, value)); RaisePropertyChanged(); }
        }

        private int minutesOffset;
        [JsonProperty]
        public int MinutesOffset {
            get => minutesOffset;
            set { minutesOffset = value; RaisePropertyChanged(); UpdateTime(); }
        }

        private bool createVideoWhenDone = true;
        [JsonProperty]
        public bool CreateVideoWhenDone {
            get => createVideoWhenDone;
            set { createVideoWhenDone = value; RaisePropertyChanged(); }
        }

        private bool waitUntilCapturing = true;
        [JsonProperty]
        public bool WaitUntilCapturing {
            get => waitUntilCapturing;
            set { waitUntilCapturing = value; RaisePropertyChanged(); }
        }

        // The manual "Time" provider lets the user type the clock time; all others compute it.
        public bool HasFixedTimeProvider => selectedProvider != null
            && !(selectedProvider is NINA.Sequencer.Utility.DateTimeProvider.TimeProvider);
        public bool IsManualTime => !HasFixedTimeProvider;
        public Visibility OffsetVisibility => HasFixedTimeProvider ? Visibility.Visible : Visibility.Collapsed;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (DateTimeProviders != null && !string.IsNullOrEmpty(selectedProviderName)) {
                var match = DateTimeProviders.FirstOrDefault(p => p.Name == selectedProviderName);
                if (match != null) {
                    SelectedProvider = match;
                }
            }
        }

        public override void AfterParentChanged() {
            UpdateTime();
        }

        // Mirrors Wait for Time: for a fixed source, collapse (provider time + offset) into HH:MM:SS.
        private void UpdateTime() {
            try {
                if (SelectedProvider == null) { return; }
                if (HasFixedTimeProvider) {
                    var t = SelectedProvider.GetDateTime(this) + TimeSpan.FromMinutes(MinutesOffset);
                    Hours = t.Hour;
                    Minutes = t.Minute;
                    Seconds = t.Second;
                }
            } catch (Exception ex) {
                Logger.Error(ex);
            }
        }

        // The block returns quickly (it just hands the schedule to the app), so it doesn't add to the
        // sequence's estimated duration.
        public override TimeSpan GetEstimatedDuration() => TimeSpan.Zero;

        // The absolute next-future-occurrence of the target, SIDE-EFFECT-FREE (no property writes) so
        // it's safe to call from Execute on a background sequence thread. A scheduled STOP is always
        // the NEXT occurrence - you can't stop in the past - so unlike "Wait for Time" we don't apply
        // a day-rollover heuristic (which yields a past instant when armed in the evening for a morning
        // event). NINA anchors sun events to the astro "day", so in the evening GetDateTime() returns
        // today's already-passed event; we roll forward to the next occurrence. (Astro events drift
        // only minutes/day - negligible for a stop time.) For a fixed source it recomputes from the
        // provider (avoids a stale cached value); manual uses the entered H:M:S. Provider exceptions
        // (e.g. no twilight solution at high latitude) propagate so Execute can fail with a clear message.
        private DateTime NextTargetTime() {
            var now = DateTime.Now;
            DateTime target = HasFixedTimeProvider
                ? SelectedProvider.GetDateTime(this) + TimeSpan.FromMinutes(MinutesOffset)
                : now.Date + new TimeSpan(Hours, Minutes, Seconds);   // manual "Time"
            while (target <= now) { target = target.AddDays(1); }
            return target;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);

            if (SelectedProvider == null) {
                throw new SequenceEntityFailedException("Scheduled Timelapse: choose an 'Until' time source.");
            }
            DateTime target;
            try {
                target = NextTargetTime();
            } catch (TimeProviderException ex) {
                // e.g. high-latitude summer: the chosen sun event has no solution.
                throw new SequenceEntityFailedException(
                    $"Scheduled Timelapse: {ex.Message} - choose a different 'Until' time source.");
            }

            // Hand the schedule to the app, which owns the stop timer - so it stops (and optionally
            // renders) at the target time regardless of the NINA sequence. Non-blocking: we return
            // after starting (capture is started by the app if it wasn't already).
            var stopAt = target.ToString("yyyyMMdd-HHmmss");
            await client.ScheduleCaptureAsync(stopAt, CreateVideoWhenDone, token);

            if (WaitUntilCapturing) {
                await client.WaitForFirstFrameAsync(token);
            }
        }

        public override object Clone() => new ScheduledTimelapse(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(ScheduledTimelapse)}";
    }
}
