using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile;
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
    /// was sent, with no way to tell why. WHICH events are sent is chosen on the plugin's options
    /// page (SessionEventCatalog, one toggle per event type, saved per profile) rather than on this
    /// block: the selection is read live at every decision point, so options changes apply
    /// immediately - even mid-sequence - and every sequence shares one selection instead of each
    /// saved sequence carrying its own stale copy.
    ///
    /// Nothing here may fail a sequence: every send is swallowed by RtspApiClient.SendEventAsync,
    /// and Execute catches whatever is left. Events are recorded only while the app is capturing;
    /// when it isn't, the app answers 409 and the send is silently skipped.
    /// </summary>
    [ExportMetadata("Name", "Report Timelapse Events")]
    [ExportMetadata("Description", "Sends session events (autofocus, filter changes, target, guiding) to the RTSP Timelapse app so they can be shown on the timelapse video. Choose which event types on the plugin's options page.")]
    [ExportMetadata("Icon", "RtspTimelapse_SVG")]
    [ExportMetadata("Category", "RTSP Timelapse")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class ReportTimelapseEvents : SequenceTrigger, IValidatable {
        private readonly IProfileService profileService;
        private readonly IGuiderMediator guiderMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IPluginOptionsAccessor settings;

        /// <summary>
        /// Trigger names/types treated as a meridian flip. Narrow on purpose: subscribing to every
        /// sibling trigger would pick up things like DitherAfterExposures, which fires continuously.
        /// </summary>
        private static readonly string[] FlipTriggerMarkers = { "MeridianFlip", "Flip" };

        /// <summary>How long a single event POST may take before we give up on it.</summary>
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

        // Last values sent, so target/guiding report on CHANGE rather than on every instruction
        // boundary. Reset per sequence run in SequenceBlockInitialize.
        private string lastTarget;
        private string lastFilter;
        private bool? lastGuidingConnected;

        /// <summary>
        /// One event queued for the boundary just crossed. Commit is applied only after the app
        /// confirms it recorded the event - see Execute. State changes (target, guiding) must not
        /// be marked as reported while the app is still stopped.
        /// </summary>
        private sealed class PendingEvent {
            public string Title;
            public string Detail;
            public string Category;
            public Action Commit;
        }

        // What Execute should send, decided in ShouldTriggerAfter. Holding it here keeps the
        // decision and the send in one place per boundary; NINA calls the two back to back.
        private readonly List<PendingEvent> pending = new List<PendingEvent>();

        [ImportingConstructor]
        public ReportTimelapseEvents(IProfileService profileService,
                                     IGuiderMediator guiderMediator,
                                     IFilterWheelMediator filterWheelMediator) {
            this.profileService = profileService;
            this.guiderMediator = guiderMediator;
            this.filterWheelMediator = filterWheelMediator;
            settings = new PluginOptionsAccessor(profileService, PluginConstants.PluginId);
        }

        private ReportTimelapseEvents(ReportTimelapseEvents copyMe)
            : this(copyMe.profileService, copyMe.guiderMediator, copyMe.filterWheelMediator) {
            CopyMetaData(copyMe);
        }

        /// <summary>The per-profile event selection from the plugin's options page.</summary>
        private bool IsEnabled(SessionEventToggle toggle) =>
            SessionEventCatalog.IsEnabled(settings, toggle);

        public override void SequenceBlockInitialize() {
            // Fresh run: forget what the previous one reported, so the first target of the night
            // is announced again rather than being suppressed as "unchanged".
            lastTarget = null;
            lastFilter = null;
            lastGuidingConnected = null;
            SubscribeToFlipTriggers();
            base.SequenceBlockInitialize();
        }

        public override void SequenceBlockTeardown() {
            UnsubscribeFromFlipTriggers();
            base.SequenceBlockTeardown();
        }

        /// <summary>
        /// Decide which events the boundary just crossed is worth, and remember what to send.
        /// Every source is evaluated independently - a boundary can queue a target change AND the
        /// instruction that revealed it. State events (target, guiding) would merely be delayed by
        /// losing a boundary, since they re-compare live state next time; an instruction event
        /// describes the specific item that just completed, so a boundary it loses is lost for
        /// good. The order here only decides caption order: "Target: M31" reads better before
        /// "Center" than after it.
        /// </summary>
        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            pending.Clear();

            if (previousItem == null) {
                return false;  // nothing has completed yet
            }

            // Each source is guarded on its own: a guider driver faulting in GetInfo(), or a
            // misbehaving third-party target container, must not stop the OTHER sources from
            // reporting - an instruction event skipped that way would be lost for good, while
            // the faulting source itself only loses a boundary it would re-detect anyway.
            Guarded(() => TryTargetChange(previousItem), "target");
            Guarded(TryGuidingChange, "guiding");
            Guarded(() => TryInstruction(previousItem), "instruction");

            return pending.Count > 0;
        }

        /// <summary>Reading sequence/equipment state must never break the sequence.</summary>
        private static void Guarded(Action detect, string source) {
            try {
                detect();
            } catch (Exception ex) {
                Logger.Debug($"Report Timelapse Events: could not inspect {source} state ({ex.Message})");
            }
        }

        // We report what has happened, so everything is decided after an item completes.
        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) => false;

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            if (pending.Count == 0) {
                return;
            }

            var events = pending.ToList();
            pending.Clear();

            try {
                var client = RtspApiClient.FromProfile(profileService);
                foreach (var evt in events) {
                    if (ct.IsCancellationRequested) {
                        return;  // sequence is stopping; the rest can stay unsent
                    }

                    // Bounded and linked to the sequence's token: a hung localhost POST must not
                    // stall the sequence, and stopping the sequence should abandon the send
                    // immediately. The timeout is per event so an earlier slow send can't starve
                    // the ones behind it.
                    using (var timeout = new CancellationTokenSource(SendTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token)) {
                        var sent = await client.SendEventAsync(evt.Title, evt.Detail, evt.Category, DateTime.Now, linked.Token)
                                               .ConfigureAwait(false);

                        // Only remember a state change once the app has actually taken it. Capture
                        // usually starts a few instructions into a sequence, so the target is often
                        // resolved while the app is still stopped and the send 409s. Committing
                        // anyway would mark the target as "already reported" and it would never be
                        // captioned, even though it applies to the whole video. Leaving it
                        // uncommitted means the next boundary re-detects it and it lands as soon
                        // as capture is running.
                        if (sent) {
                            evt.Commit?.Invoke();
                        }
                    }
                }
            } catch (Exception ex) {
                // SendEventAsync already swallows everything; this is a backstop so a surprise here
                // (e.g. building the client) still can't fail the user's sequence.
                Logger.Debug($"Report Timelapse Events: send failed ({ex.Message})");
            }
        }

        // ------------------------------------------------------------------ event sources

        /// <summary>Queue the target changing since the last report. Resolved from the completed item.</summary>
        private void TryTargetChange(ISequenceItem previousItem) {
            if (!IsEnabled(SessionEventCatalog.TargetChanges)) {
                return;
            }
            var target = FindTargetName(previousItem);
            if (string.IsNullOrWhiteSpace(target) || target == lastTarget) {
                return;
            }
            pending.Add(new PendingEvent {
                Title = $"Target: {target}",
                Category = "target",
                Commit = () => lastTarget = target,
            });
        }

        /// <summary>Queue guiding connecting/disconnecting since the last report.</summary>
        private void TryGuidingChange() {
            if (!IsEnabled(SessionEventCatalog.Guiding)) {
                return;
            }
            var info = guiderMediator?.GetInfo();
            if (info == null) {
                return;
            }

            var connected = info.Connected;
            if (lastGuidingConnected == connected) {
                return;
            }

            // First observation establishes the baseline rather than announcing "guiding resumed"
            // for a guider that was simply already running when the sequence started. This one is
            // committed immediately - there's nothing to send, so there's nothing to confirm.
            if (lastGuidingConnected == null) {
                lastGuidingConnected = connected;
                return;
            }

            pending.Add(new PendingEvent {
                Title = connected ? "Guiding resumed" : "Guiding lost",
                Detail = connected ? FormatRms(info) : null,
                Category = "guiding",
                Commit = () => lastGuidingConnected = connected,
            });
        }

        /// <summary>Queue the instruction that just finished, if it's one worth captioning per the options page.</summary>
        private void TryInstruction(ISequenceItem previousItem) {
            var typeName = previousItem.GetType().Name;
            var toggle = SessionEventCatalog.FindForInstruction(typeName);
            if (toggle == null || !IsEnabled(toggle)) {
                return;
            }

            // A filter switch is worth a caption only for *which* filter it selected. "Switch
            // Filter" on its own says nothing, and it is by far the most frequent reportable
            // instruction - 38 of 63 events on a test night, one every few minutes as the
            // rotation cycles. Naming the filter earns the space; repeats are dropped.
            if (ReferenceEquals(toggle, SessionEventCatalog.SwitchFilter)) {
                var filter = CurrentFilterName();
                if (!string.IsNullOrWhiteSpace(filter)) {
                    if (filter == lastFilter) {
                        return;  // same filter re-selected - nothing changed to show
                    }
                    pending.Add(new PendingEvent {
                        Title = $"Filter: {filter}",
                        Category = "filter",
                        Commit = () => lastFilter = filter,
                    });
                    return;
                }
                // No wheel connected or no selection - fall through and report the instruction
                // itself rather than losing the event entirely.
            }

            // The display name is what the user sees in their sequence, so it reads better on the
            // video than the type name; fall back to the type when a plugin leaves Name unset.
            var name = string.IsNullOrWhiteSpace(previousItem.Name) ? typeName : previousItem.Name;
            pending.Add(new PendingEvent {
                Title = name.Trim(),
                Category = "sequence",
            });
        }

        /// <summary>Currently selected filter's name, or null if it can't be read.</summary>
        private string CurrentFilterName() {
            try {
                var name = filterWheelMediator?.GetInfo()?.SelectedFilter?.Name;
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            } catch (Exception) {
                return null;  // wheel disconnected mid-read; the caller falls back
            }
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

        // -------------------------------------------------------------- meridian flip

        // Sibling flip triggers we're watching, with the status each was last seen in. Guarded by
        // flipTriggersLock: PropertyChanged can arrive on whatever thread changed the trigger's
        // Status, racing the sequencer thread's subscribe/teardown.
        private readonly Dictionary<ISequenceTrigger, bool> watchedFlipTriggers =
            new Dictionary<ISequenceTrigger, bool>();
        private readonly object flipTriggersLock = new object();

        /// <summary>
        /// Watch every meridian-flip trigger in the sequence for its status changing.
        ///
        /// Subscribing rather than polling is the point: a flip takes minutes during which no
        /// sequence items execute, so ShouldTriggerAfter is never called and a poll would only
        /// notice the flip once it was already over. Status changes arrive as they happen.
        ///
        /// This is also why the flip can't be captioned as an instruction - NINA runs the flip's
        /// items under a TriggerRunner, so they never appear as `previousItem`.
        /// </summary>
        private void SubscribeToFlipTriggers() {
            UnsubscribeFromFlipTriggers();

            // Always watch, even when the Meridian flip toggle is off: the toggle is checked at
            // fire time instead, so flipping it on the options page takes effect mid-run - in
            // both directions - and the running/not-running edge detection never goes stale.
            try {
                var root = FindRootContainer();
                foreach (var trigger in FindTriggers(root)) {
                    if (ReferenceEquals(trigger, this) || !LooksLikeFlipTrigger(trigger)) {
                        continue;
                    }
                    if (trigger is INotifyPropertyChanged observable) {
                        lock (flipTriggersLock) {
                            watchedFlipTriggers[trigger] = IsRunning(trigger);
                        }
                        observable.PropertyChanged += FlipTrigger_PropertyChanged;
                        Logger.Debug($"Report Timelapse Events: watching flip trigger '{trigger.Name}'");
                    }
                }
            } catch (Exception ex) {
                Logger.Debug($"Report Timelapse Events: could not watch flip triggers ({ex.Message})");
            }
        }

        /// <summary>
        /// Drop every handler. A sequence tree outlives a run, so a leaked handler would keep
        /// firing on later runs and against a stale trigger instance.
        /// </summary>
        private void UnsubscribeFromFlipTriggers() {
            // Empty the map before detaching: a Status change arriving in between finds nothing
            // and returns, which during teardown is exactly what should happen.
            List<ISequenceTrigger> watched;
            lock (flipTriggersLock) {
                watched = watchedFlipTriggers.Keys.ToList();
                watchedFlipTriggers.Clear();
            }
            foreach (var trigger in watched) {
                if (trigger is INotifyPropertyChanged observable) {
                    observable.PropertyChanged -= FlipTrigger_PropertyChanged;
                }
            }
        }

        private void FlipTrigger_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            try {
                if (e?.PropertyName != nameof(ISequenceTrigger.Status)) {
                    return;
                }
                if (!(sender is ISequenceTrigger trigger)) {
                    return;
                }

                var running = IsRunning(trigger);
                lock (flipTriggersLock) {
                    if (!watchedFlipTriggers.TryGetValue(trigger, out var wasRunning) || running == wasRunning) {
                        return;
                    }
                    watchedFlipTriggers[trigger] = running;
                }

                // State bookkeeping above stays unconditional; only the send is gated, so the edge
                // detection is still correct if the toggle is enabled mid-flip.
                if (!IsEnabled(SessionEventCatalog.MeridianFlip)) {
                    return;
                }

                // Fire-and-forget: this is a UI/sequencer notification thread, and blocking it on
                // an HTTP call would stall the sequencer. SendEventAsync swallows everything.
                var title = running ? "Meridian Flip started" : "Meridian Flip complete";
                var detail = trigger.Name;
                _ = SendFireAndForgetAsync(title, detail, "mount");
            } catch (Exception ex) {
                // Never throw back into whoever raised the notification - that could be the
                // sequencer mid-flip.
                Logger.Debug($"Report Timelapse Events: flip status change not handled ({ex.Message})");
            }
        }

        private async Task SendFireAndForgetAsync(string title, string detail, string category) {
            try {
                using (var cts = new CancellationTokenSource(SendTimeout)) {
                    var client = RtspApiClient.FromProfile(profileService);
                    await client.SendEventAsync(title, detail, category, DateTime.Now, cts.Token)
                                .ConfigureAwait(false);
                }
            } catch (Exception ex) {
                Logger.Debug($"Report Timelapse Events: flip event not sent ({ex.Message})");
            }
        }

        private static bool IsRunning(ISequenceTrigger trigger) =>
            trigger.Status == SequenceEntityStatus.RUNNING;

        private static bool LooksLikeFlipTrigger(ISequenceTrigger trigger) {
            var typeName = trigger.GetType().Name ?? string.Empty;
            var name = trigger.Name ?? string.Empty;
            return FlipTriggerMarkers.Any(m =>
                typeName.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Every trigger in the sequence, from the root container down.</summary>
        private static IEnumerable<ISequenceTrigger> FindTriggers(ISequenceContainer container) {
            if (container == null) {
                yield break;
            }
            if (container is ITriggerable triggerable && triggerable.Triggers != null) {
                foreach (var trigger in triggerable.Triggers.ToList()) {
                    yield return trigger;
                }
            }
            foreach (var item in container.Items.ToList()) {
                if (item is ISequenceContainer child) {
                    foreach (var trigger in FindTriggers(child)) {
                        yield return trigger;
                    }
                }
            }
        }

        private ISequenceContainer FindRootContainer() {
            ISequenceContainer container = Parent;
            while (container?.Parent != null) {
                container = container.Parent;
            }
            return container;
        }

        /// <summary>
        /// Target enclosing the instruction that just finished, walking up its container chain.
        ///
        /// Resolved from the *item*, not from this trigger. The trigger belongs on the sequence
        /// root, so walking up from its own Parent starts at the root and never descends into the
        /// DeepSkyObjectContainers below it - it would return null for every sequence. The item
        /// that just ran is inside the target container, so its ancestry does reach it. (Ground
        /// Station's Utilities.FindDsoInfo resolves from item context for the same reason.)
        ///
        /// Returns null for items outside any target container, e.g. a startup sequence.
        /// </summary>
        private static string FindTargetName(ISequenceItem item) {
            ISequenceContainer container = item?.Parent;
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
            if (!SessionEventCatalog.All.Any(IsEnabled)) {
                issues.Add("Nothing selected to report - enable events in Options > Plugins > RTSP Timelapse Control.");
            }
            Issues = issues;
            RaisePropertyChanged(nameof(Issues));
            return issues.Count == 0;
        }

        public override object Clone() => new ReportTimelapseEvents(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(ReportTimelapseEvents)}";
    }
}
