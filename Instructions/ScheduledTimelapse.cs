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
    [ExportMetadata("Description", "Starts RTSP timelapse capture and runs until a chosen time (e.g. Nautical Dawn + offset), then stops and optionally renders the video. No Stop block needed. It blocks the sequence until that time - put it in a Parallel set to capture alongside imaging.")]
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

        private TimeOnly rolloverTime = new TimeOnly(12, 0, 0);
        public TimeOnly RolloverTime {
            get => rolloverTime;
            private set { rolloverTime = value; RaisePropertyChanged(); }
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
                RolloverTime = SelectedProvider.GetRolloverTime(this);
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

        // Mirrors Wait for Time: target clock time -> TimeSpan from now, with day-rollover handling.
        public override TimeSpan GetEstimatedDuration() {
            if (SelectedProvider == null) { return TimeSpan.Zero; }
            try {
                var now = DateTime.Now;
                var then = new DateTime(now.Year, now.Month, now.Day, Hours, Minutes, Seconds);
                RolloverTime = SelectedProvider.GetRolloverTime(this);
                var timeOnlyNow = TimeOnly.FromDateTime(now);
                var timeOnlyThen = TimeOnly.FromDateTime(then);
                if (timeOnlyNow < RolloverTime && timeOnlyThen >= RolloverTime) { then = then.AddDays(-1); }
                if (timeOnlyNow >= RolloverTime && timeOnlyThen < RolloverTime) { then = then.AddDays(1); }
                var diff = then - DateTime.Now;
                return diff < TimeSpan.Zero ? TimeSpan.Zero : diff;
            } catch (Exception ex) {
                Logger.Error(ex);
                return TimeSpan.Zero;
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var client = RtspApiClient.FromProfile(profileService);

            string since;
            var status = await client.GetStatusAsync(token);
            if (!status.Capturing) {
                since = RtspSession.FormatSince(DateTime.Now);
                RtspSession.LastStartSince = since;
                await client.StartCaptureAsync(token);
                await client.WaitForFirstFrameAsync(token);
            } else {
                since = RtspSession.LastStartSince;
                Logger.Info("RTSP Timelapse already capturing; Scheduled Timelapse will run until its target time, then stop.");
            }

            var completed = false;
            try {
                await CoreUtil.Wait(GetEstimatedDuration(), true, token, progress, "");
                completed = true;
            } finally {
                try {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
                        var s = await client.GetStatusAsync(cts.Token);
                        if (s.Capturing) {
                            await client.StopCaptureAsync(cts.Token);
                        }
                        // Only render on a normal finish - not when the sequence was stopped/aborted.
                        if (completed && CreateVideoWhenDone) {
                            await client.CreateVideoAsync(since, cts.Token);
                        }
                    }
                } catch (Exception ex) {
                    Logger.Error($"Scheduled Timelapse cleanup failed: {ex.Message}");
                }
            }
        }

        public override object Clone() => new ScheduledTimelapse(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(ScheduledTimelapse)}";
    }
}
