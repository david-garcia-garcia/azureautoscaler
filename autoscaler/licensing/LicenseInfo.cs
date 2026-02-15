namespace poolautoscaler.licensing
{
    /// <summary>
    /// Provides license information and status.
    /// </summary>
    public class LicenseInfo
    {
        /// <summary>Decoded license payload.</summary>
        public License License { get; set; }

        /// <summary>True if signature and format are valid.</summary>
        public bool IsValid { get; set; }

        /// <summary>True if past expiration date.</summary>
        public bool IsExpired { get; set; }

        /// <summary>
        /// Returns true if license is expired or invalid (both have the same restrictions).
        /// </summary>
        public bool IsRestricted => !this.IsValid || this.IsExpired;

        /// <summary>
        /// Returns the reason for license restriction: "invalid" or "expired".
        /// </summary>
        public string Reason => !this.IsValid ? "invalid" : "expired";

        /// <summary>Initializes a new instance of the <see cref="LicenseInfo"/> class with the given state.</summary>
        /// <param name="license">The license payload.</param>
        /// <param name="isValid">True if signature and format are valid.</param>
        /// <param name="isExpired">True if past expiration date.</param>
        public LicenseInfo(License license, bool isValid, bool isExpired)
        {
            this.License = license;
            this.IsValid = isValid;
            this.IsExpired = isExpired;
        }
    }
}
