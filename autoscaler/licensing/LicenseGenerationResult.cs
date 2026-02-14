namespace poolautoscaler.licensing
{
    /// <summary>
    /// Result of a license generation attempt.
    /// </summary>
    public sealed class LicenseGenerationResult
    {
        /// <summary>True if generation succeeded.</summary>
        public bool Success { get; init; }

        /// <summary>Generated JWT when successful.</summary>
        public string? Jwt { get; init; }

        /// <summary>Error message when failed.</summary>
        public string? ErrorMessage { get; init; }
    }
}
