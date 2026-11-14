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

        public string BadgeBackground { get; set; } = "dark";

        public bool HideFullLikingBirthday { get; set; }

        public bool ShowBirthdayHeart { get; set; } = true;

        public bool DisablePikeSpecial { get; set; }

        public bool DisablePortMerchant { get; set; }

        public bool DisableLicca { get; set; }

        public bool DisableMerchantCanMoveIn { get; set; }

        public bool RestoreResinCollectorPriority { get; set; }

        public bool ShowLayoutEditor { get; set; }

        public bool DemoBadges { get; set; }

        public string EditorKey { get; set; }

        public string SaveKey { get; set; }

        public string DemoKey { get; set; }

        public string DumpKey { get; set; }

        /// <summary>
        /// Debug only: 1 opens the calendar preview, 2 the clock-row preview, right after the editor
        /// appears. It has no menu entry; it exists so the preview pages can be inspected without a
        /// keyboard.
        /// </summary>
        public int PreviewOnStart { get; set; }
    }
}
