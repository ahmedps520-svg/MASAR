namespace Masar.Game.Diagnostics
{
    /// <summary>
    /// Where the "Send diagnostics" button uploads to: a dedicated ForgeBase project
    /// (Supabase-compatible REST, https://forgebase.art/docs).
    ///
    /// The anon key is a PUBLIC client key by design (ForgeBase docs: "safe in browsers and apps").
    /// What it can do is decided by row-level security: the diagnostics table only allows INSERT
    /// for anon, so the key cannot read anyone's reports. Never put the service_role key here.
    ///
    /// Empty values mean "not configured yet". The button then says so, instead of pretending to send.
    /// </summary>
    public static class DiagnosticsEndpoint
    {
        /// <summary>e.g. https://api.forgebase.art/p/&lt;project-ref&gt;</summary>
        public const string ProjectUrl = "";
        public const string AnonKey = "";
        public const string Table = "diagnostics";

        public static bool Configured => !string.IsNullOrEmpty(ProjectUrl) && !string.IsNullOrEmpty(AnonKey);
        public static string InsertUrl => ProjectUrl.TrimEnd('/') + "/rest/v1/" + Table;
    }
}
