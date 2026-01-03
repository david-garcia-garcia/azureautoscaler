namespace poolautoscaler.licensing
{
    /// <summary>
    /// Provides license information and status
    /// </summary>
    public class LicenseInfo
    {
        public License License { get; set; }
        public bool IsValid { get; set; }
        public bool IsExpired { get; set; }

        /// <summary>
        /// Returns true if license is expired or invalid (both have the same restrictions)
        /// </summary>
        public bool IsRestricted => !IsValid || IsExpired;

        /// <summary>
        /// Returns the reason for license restriction: "invalid" or "expired"
        /// </summary>
        public string Reason => !IsValid ? "invalid" : "expired";

        public LicenseInfo(License license, bool isValid, bool isExpired)
        {
            License = license;
            IsValid = isValid;
            IsExpired = isExpired;
        }
    }
}
