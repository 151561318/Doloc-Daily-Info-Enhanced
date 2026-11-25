namespace SomeOptimizations
{
    /// <summary>
    /// Persisted user settings. Public settable properties on purpose: DTMAPI stores this with
    /// DataContractJsonSerializer, which only round-trips public read/write properties and fields.
    /// </summary>
    public sealed class Settings
    {
        public bool ShowExactTime { get; set; }

        public int SameBatchMinutes { get; set; } = 60;

        public bool DisableMatureHighlight { get; set; }

        /// <summary>Skips the tree stand (cocoa tree) when scanning for maturing crops.</summary>
        public bool DisableTreeCrops { get; set; }

        public string BadgeBackground { get; set; } = "dark";

        public bool ShowFullLikingBirthday { get; set; }

        public bool HideBirthdayHeart { get; set; }

        /// <summary>Suppresses the player's own birthday reminder.</summary>
        public bool DisablePlayerBirthday { get; set; }

        public bool DisablePikeSpecial { get; set; }

        public bool DisablePortMerchant { get; set; }

        public bool DisableLicca { get; set; }

        /// <summary>Silences Licca's reminder for the whole long-night festival instead of its last three days.</summary>
        public bool DisableLiccaEverNight { get; set; }

        /// <summary>Stops the festival badge from pulsing during a festival's story/participation window.</summary>
        public bool DisableFestivalWindow { get; set; }

        public bool DisableMerchantCanMoveIn { get; set; }

        /// <summary>Suppresses the "plastic film is about to wear out" reminder.</summary>
        public bool DisableFilmWarning { get; set; }

        public bool RestoreResinCollectorPriority { get; set; }

        public bool ShowLayoutEditor { get; set; }

        public bool DemoBadges { get; set; }

        public string EditorKey { get; set; }

        /// <summary>Opens the player-portrait editor (defaults to F8); see <see cref="EditorUi"/>.</summary>
        public string PlayerEditorKey { get; set; }

        public string SaveKey { get; set; }

        public string DemoKey { get; set; }

        public string DumpKey { get; set; }

        /// <summary>Turns on the verbose "diag" log lines (troubleshooting only).</summary>
        public bool DiagnosticsLog { get; set; }

        /// <summary>
        /// Debug only: 1 opens the calendar preview, 2 the clock-row preview, right after the editor
        /// appears. It has no menu entry; it exists so the preview pages can be inspected without a
        /// keyboard.
        /// </summary>
        public int PreviewOnStart { get; set; }
    }
}
