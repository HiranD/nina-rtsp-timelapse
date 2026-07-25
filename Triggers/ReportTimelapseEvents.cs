using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;

namespace NINA.RtspTimelapse.Plugin.Triggers {

    /// <summary>
    /// Reports what happens during the imaging session to the RTSP Timelapse app, so the events can
    /// be burned onto the finished timelapse as captions ("Autofocus Complete", "Target: M31").
    ///
    /// Adding this trigger to a sequence IS the opt-in - there is no separate enable switch. A
    /// sequence item cannot register a trigger on the running sequence without mutating (and then
    /// serialising into) the user's saved sequence, so a toggle elsewhere could be on while nothing
    /// was sent, with no way to tell why.
    ///
    /// Nothing here may fail a sequence: every send is swallowed by RtspApiClient.SendEventAsync,
    /// and Execute catches whatever is left. Events are recorded only while the app is capturing;
    /// when it isn't, the app answers 409 and the send is silently skipped.
    /// </summary>
    [ExportMetadata("Name", "Report Timelapse Events")]
    [ExportMetadata("Description", "Sends session events (autofocus, filter changes, target, guiding) to the RTSP Timelapse app so they can be shown on the timelapse video.")]
    [ExportMetadata("Icon", "RtspTimelapse_SVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class ReportTimelapseEvents : SequenceTrigger, IValidatable {
        private readonly IProfileService profileService;
        private readonly IGuiderMediator guiderMediator;

        /// <summary>
        /// Instruction types worth a caption, matched against the sequence item's type name.
        ///
        /// An allowlist rather than a denylist: a night contains hundreds of Take Exposure items,
        /// and a caption for each would bury the video. Better to show a few meaningful moments and
        /// extend this list than to filter noise out forever. Matching is on a contains basis so
        /// NINA's concrete type names (e.g. "MeridianFlip", "SmartMeridianFlip") both hit.
        /// </summary>
        private static readonly string[] ReportableTypes = {
            "Autofocus",
            "MeridianFlip",
            "SwitchFilter",
            "Center",          // Center / CenterAndRotate - plate solve + slew
            "SolveAndSync",
            "Dither",
            "ParkScope",
            "UnparkScope",
            "ParkDome",
            "CoolCamera",
            "WarmCamera",
        };

        /// <summary>How long a single event POST may take before we give up on it.</summary>
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

        // Last values sent, so target/guiding report on CHANGE rather than on every instruction
        // boundary. Reset per sequence run in SequenceBlockInitialize.
        private string lastTarget;
        private bool? lastGuidingConnected;

        // What Execute should send, decided in ShouldTriggerAfter. Holding it here keeps the
        // decision and the send in one place per boundary; NINA calls the two back to back.
        private string pendingTitle;
        private string pendingDetail;
        private string pendingCategory;

        [ImportingConstructor]
        public ReportTimelapseEvents(IProfileService profileService, IGuiderMediator guiderMediator) {
            this.profileService = profileService;
            this.guiderMediator = guiderMediator;
        }

        private ReportTimelapseEvents(ReportTimelapseEvents copyMe)
            : this(copyMe.profileService, copyMe.guiderMediator) {
            CopyMetaData(copyMe);
            ReportInstructions = copyMe.ReportInstructions;
            ReportTargetChanges = copyMe.ReportTargetChanges;
            ReportGuiding = copyMe.ReportGuiding;
        }

        private bool reportInstructions = true;

        /// <summary>Report notable instructions as they finish (autofocus, filter change, flip...).</summary>
        [JsonProperty]
        public bool ReportInstructions {
            get => reportInstructions;
            set { reportInstructions = value; RaisePropertyChanged(); }
        }

        private bool reportTargetChanges = true;

        /// <summary>Report the imaging target when it changes.</summary>
        [JsonProperty]
        public bool ReportTargetChanges {
            get => reportTargetChanges;
            set { reportTargetChanges = value; RaisePropertyChanged(); }
        }

        private bool reportGuiding = false;

        /// <summary>Report guiding being lost and resumed.</summary>
        [JsonProperty]
        public bool ReportGuiding {
            get => reportGuiding;
            set { reportGuiding = value; RaisePropertyChanged(); }
        }

        public override void SequenceBlockInitialize() {
            // Fresh run: forget what the previous one reported, so the first target of the night
            // is announced again rather than being suppressed as "unchanged".
            lastTarget = null;
            lastGuidingConnected = null;
            base.SequenceBlockInitialize();
        }

        /// <summary>
        /// Decide whether the boundary just crossed is worth an event, and remember what to send.
        /// Checked in priority order so one boundary produces at most one caption - a target change
        /// matters more than the instruction that revealed it.
        /// </summary>
        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            pendingTitle = pendingDetail = pendingCategory = null;

            if (previousItem == null) {
                return false;  // nothing has completed yet
            }

            try {
                if (ReportTargetChanges && TryTargetChange()) { return true; }
                if (ReportGuiding && TryGuidingChange()) { return true; }
                if (ReportInstructions && TryInstruction(previousItem)) { return true; }
            } catch (Exception ex) {
                // Reading sequence/equipment state must never break the sequence.
                Logger.Debug($"Report Timelapse Events: could not inspect state ({ex.Message})");
            }

            return false;
        }

        // We report what has happened, so everything is decided after an item completes.
        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) => false;

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            if (string.IsNullOrWhiteSpace(pendingTitle)) {
                return;
            }

            var title = pendingTitle;
            var detail = pendingDetail;
            var category = pendingCategory;
            pendingTitle = pendingDetail = pendingCategory = null;

            try {
                // Bounded and linked to the sequence's token: a hung localhost POST must not stall
                // the sequence, and stopping the sequence should abandon the send immediately.
                using (var timeout = new CancellationTokenSource(SendTimeout))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token)) {
                    var client = RtspApiClient.FromProfile(profileService);
                    await client.SendEventAsync(title, detail, category, DateTime.Now, linked.Token)
                                .ConfigureAwait(false);
                }
            } catch (Exception ex) {
                // SendEventAsync already swallows everything; this is a backstop so a surprise here
                // (e.g. building the client) still can't fail the user's sequence.
                Logger.Debug($"Report Timelapse Events: send failed ({ex.Message})");
            }
        }

        // ------------------------------------------------------------------ event sources

        /// <summary>Target changed since the last report? Walks up for the enclosing DSO container.</summary>
        private bool TryTargetChange() {
            var target = FindTargetName();
            if (string.IsNullOrWhiteSpace(target) || target == lastTarget) {
                return false;
            }
            lastTarget = target;
            pendingTitle = $"Target: {target}";
            pendingCategory = "target";
            return true;
        }

        /// <summary>Guiding connected/disconnected since the last report?</summary>
        private bool TryGuidingChange() {
            var info = guiderMediator?.GetInfo();
            if (info == null) {
                return false;
            }

            var connected = info.Connected;
            if (lastGuidingConnected == connected) {
                return false;
            }

            // First observation establishes the baseline rather than announcing "guiding resumed"
            // for a guider that was simply already running when the sequence started.
            var isFirstObservation = lastGuidingConnected == null;
            lastGuidingConnected = connected;
            if (isFirstObservation) {
                return false;
            }

            pendingTitle = connected ? "Guiding resumed" : "Guiding lost";
            pendingCategory = "guiding";
            pendingDetail = connected ? FormatRms(info) : null;
            return true;
        }

        /// <summary>Was the instruction that just finished one worth captioning?</summary>
        private bool TryInstruction(ISequenceItem previousItem) {
            var typeName = previousItem.GetType().Name;
            if (!ReportableTypes.Any(t => typeName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) {
                return false;
            }

            // The display name is what the user sees in their sequence, so it reads better on the
            // video than the type name; fall back to the type when a plugin leaves Name unset.
            var name = string.IsNullOrWhiteSpace(previousItem.Name) ? typeName : previousItem.Name;
            pendingTitle = name.Trim();
            pendingCategory = "sequence";
            return true;
        }

        /// <summary>Current guiding error as a caption detail line, or null if unavailable.</summary>
        private string FormatRms(GuiderInfo info) {
            try {
                var rms = info.RMSError;
                if (rms?.Total == null) {
                    return null;
                }
                return $"RMS {rms.Total.Arcseconds:F2}\"";
            } catch (Exception) {
                // GuiderInfo's shape varies with the guider in use; the caption is still useful
                // without the numbers.
                return null;
            }
        }

        /// <summary>
        /// Nearest enclosing target name, walking up the container chain (the same approach Ground
        /// Station uses). Returns null outside a target container - e.g. in a startup sequence.
        /// </summary>
        private string FindTargetName() {
            ISequenceContainer container = Parent;
            while (container != null) {
                if (container is IDeepSkyObjectContainer dso && dso.Target?.DeepSkyObject != null) {
                    var name = dso.Target.DeepSkyObject.Name;
                    return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
                }
                container = container.Parent;
            }
            return null;
        }

        // ------------------------------------------------------------------ boilerplate

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var issues = new ObservableCollection<string>();
            if (!ReportInstructions && !ReportTargetChanges && !ReportGuiding) {
                issues.Add("Nothing selected to report - tick at least one option.");
            }
            Issues = issues;
            RaisePropertyChanged(nameof(Issues));
            return issues.Count == 0;
        }

        public override object Clone() => new ReportTimelapseEvents(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(ReportTimelapseEvents)}";
    }
}
