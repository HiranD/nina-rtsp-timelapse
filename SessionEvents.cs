using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NINA.Profile;
using NINA.Profile.Interfaces;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>
    /// One reportable session-event type: its persisted per-profile key, its options-page
    /// text, and (for instruction toggles) the type-name markers it matches.
    /// </summary>
    public sealed class SessionEventToggle {

        public SessionEventToggle(string key, string label, string toolTip, bool defaultEnabled,
                                  params string[] typeNameMarkers) {
            Key = key;
            Label = label;
            ToolTip = toolTip;
            DefaultEnabled = defaultEnabled;
            TypeNameMarkers = typeNameMarkers ?? Array.Empty<string>();
        }

        /// <summary>Per-profile settings key. Load-bearing: never rename once shipped.</summary>
        public string Key { get; }

        public string Label { get; }

        public string ToolTip { get; }

        public bool DefaultEnabled { get; }

        /// <summary>Sequence-item type-name substrings this toggle covers; empty for pure state toggles.</summary>
        public IReadOnlyList<string> TypeNameMarkers { get; }
    }

    /// <summary>
    /// Every event type the Report Timelapse Events trigger can send, in options-page display
    /// order. The catalog drives three things from one list: the trigger's gating, the options
    /// page checkboxes, and the persisted per-profile keys.
    ///
    /// Instruction toggles are an allowlist rather than a denylist: a night contains hundreds of
    /// Take Exposure items, and a caption for each would bury the video. Better to show a few
    /// meaningful moments and extend this list than to filter noise out forever. Matching is on a
    /// contains basis so NINA's concrete type names (e.g. "MeridianFlip", "SmartMeridianFlip")
    /// both hit.
    ///
    /// Note the trigger only ever sees instructions in the normal item flow. Anything NINA runs
    /// from a trigger (auto-dither, autofocus-after-HFR-increase, a DIY meridian flip) executes
    /// under a TriggerRunner and never arrives as `previousItem`, so it cannot be captioned from
    /// here - the meridian flip is picked up by watching the flip trigger instead. Dither is
    /// deliberately absent: it runs every few exposures, which is far too frequent to caption.
    /// </summary>
    public static class SessionEventCatalog {

        // State events - things the trigger detects by change, not by instruction type.
        public static readonly SessionEventToggle TargetChanges = new SessionEventToggle(
            "ReportTargetChanges", "Target changes",
            "Caption the imaging target whenever it changes, e.g. \"Target: M31\". Sent once per change, not once per instruction.",
            true);

        public static readonly SessionEventToggle Guiding = new SessionEventToggle(
            "ReportGuiding", "Guiding lost / resumed",
            "Caption guiding being lost and resumed, with the current RMS error. Off by default - useful for diagnosing a night, noisy if your guider reconnects often.",
            false);

        public static readonly SessionEventToggle MeridianFlip = new SessionEventToggle(
            "ReportMeridianFlip", "Meridian flip",
            "Caption the meridian flip starting and finishing. Watches the flip trigger itself, so it works with NINA's built-in flip and with DIY flip triggers alike; also covers the built-in flip instruction.",
            true, "MeridianFlip");

        // Instruction events - gated by the type name of the instruction that just finished.
        // Only centering is on by default: it marks each target acquisition. The rest are
        // opt-in so a fresh install captions the essentials (target, flip, centering) without
        // burying the video in housekeeping.
        public static readonly SessionEventToggle Autofocus = new SessionEventToggle(
            "ReportAutofocus", "Autofocus",
            "Caption autofocus runs as they finish.",
            false, "Autofocus");

        public static readonly SessionEventToggle SwitchFilter = new SessionEventToggle(
            "ReportSwitchFilter", "Filter changes",
            "Caption the filter by name as it changes - \"Filter: Ha\" rather than \"Switch Filter\". Re-selecting the same filter is skipped.",
            false, "SwitchFilter");

        public static readonly SessionEventToggle Center = new SessionEventToggle(
            "ReportCenter", "Center / rotate",
            "Caption centering - Center and Center & Rotate (plate solve + slew).",
            true, "Center");

        public static readonly SessionEventToggle SolveAndSync = new SessionEventToggle(
            "ReportSolveAndSync", "Solve and sync",
            "Caption Solve & Sync plate solves.",
            false, "SolveAndSync");

        public static readonly SessionEventToggle OpenDomeShutter = new SessionEventToggle(
            "ReportOpenDomeShutter", "Open dome shutter",
            "Caption the dome shutter opening.",
            false, "OpenDomeShutter");

        public static readonly SessionEventToggle ParkDome = new SessionEventToggle(
            "ReportParkDome", "Park dome",
            "Caption the dome parking.",
            false, "ParkDome");

        public static readonly SessionEventToggle FindHome = new SessionEventToggle(
            "ReportFindHome", "Find home",
            "Caption the mount finding home.",
            false, "FindHome");

        public static readonly SessionEventToggle ParkScope = new SessionEventToggle(
            "ReportParkScope", "Park scope",
            "Caption the mount parking.",
            false, "ParkScope");

        public static readonly SessionEventToggle UnparkScope = new SessionEventToggle(
            "ReportUnparkScope", "Unpark scope",
            "Caption the mount unparking.",
            false, "UnparkScope");

        public static readonly SessionEventToggle CoolCamera = new SessionEventToggle(
            "ReportCoolCamera", "Cool camera",
            "Caption camera cooldown finishing.",
            false, "CoolCamera");

        public static readonly SessionEventToggle WarmCamera = new SessionEventToggle(
            "ReportWarmCamera", "Warm camera",
            "Caption camera warm-up finishing.",
            false, "WarmCamera");

        public static readonly IReadOnlyList<SessionEventToggle> StateToggles = new[] {
            TargetChanges, Guiding, MeridianFlip,
        };

        public static readonly IReadOnlyList<SessionEventToggle> InstructionToggles = new[] {
            Autofocus, SwitchFilter, Center, SolveAndSync, OpenDomeShutter, ParkDome,
            FindHome, ParkScope, UnparkScope, CoolCamera, WarmCamera,
        };

        public static readonly IReadOnlyList<SessionEventToggle> All = new[] {
            TargetChanges, Guiding, MeridianFlip,
            Autofocus, SwitchFilter, Center, SolveAndSync, OpenDomeShutter, ParkDome,
            FindHome, ParkScope, UnparkScope, CoolCamera, WarmCamera,
        };

        /// <summary>
        /// Catalog entry for a completed instruction's type name, or null when no toggle covers
        /// it. Longest matching marker wins, not the first: "UnparkScope" contains "ParkScope",
        /// so first-match would gate every Unpark Scope instruction behind the Park scope toggle.
        /// </summary>
        public static SessionEventToggle FindForInstruction(string typeName) {
            if (string.IsNullOrEmpty(typeName)) {
                return null;
            }
            SessionEventToggle best = null;
            var bestLength = -1;
            foreach (var toggle in All) {
                foreach (var marker in toggle.TypeNameMarkers) {
                    if (marker.Length > bestLength &&
                        typeName.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) {
                        best = toggle;
                        bestLength = marker.Length;
                    }
                }
            }
            return best;
        }

        public static bool IsEnabled(IPluginOptionsAccessor settings, SessionEventToggle toggle) {
            return settings.GetValueBoolean(toggle.Key, toggle.DefaultEnabled);
        }
    }

    /// <summary>
    /// Checkbox item for the options page. Reads and writes straight through the per-profile
    /// accessor, so there is no second copy of the value to fall out of sync.
    /// </summary>
    public sealed class SessionEventToggleViewModel : INotifyPropertyChanged {
        private readonly SessionEventToggle descriptor;
        private readonly IPluginOptionsAccessor settings;

        public SessionEventToggleViewModel(SessionEventToggle descriptor, IPluginOptionsAccessor settings) {
            this.descriptor = descriptor;
            this.settings = settings;
        }

        public string Label => descriptor.Label;

        public string ToolTip => descriptor.ToolTip;

        public bool IsChecked {
            get => SessionEventCatalog.IsEnabled(settings, descriptor);
            set {
                settings.SetValueBoolean(descriptor.Key, value);
                RaisePropertyChanged();
            }
        }

        /// <summary>Re-read from the (possibly different) active profile after a profile switch.</summary>
        public void RefreshFromProfile() {
            RaisePropertyChanged(nameof(IsChecked));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
