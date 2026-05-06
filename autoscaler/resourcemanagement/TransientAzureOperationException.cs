namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Thrown by a resource's <see cref="ResourceState.ApplyChanges"/> when the Azure API rejects
    /// the operation with a known transient error code. The processor catches this and applies a
    /// short disable window instead of the generic 1-hour unhandled-exception disable.
    /// </summary>
    public class TransientAzureOperationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TransientAzureOperationException"/> class.
        /// </summary>
        /// <param name="errorCode">The Azure REST API error code.</param>
        /// <param name="disableMinutes">How long the resource should be disabled.</param>
        /// <param name="innerException">The original <see cref="Azure.RequestFailedException"/>.</param>
        public TransientAzureOperationException(string errorCode, int disableMinutes, Exception innerException)
            : base($"Transient Azure error: {errorCode}", innerException)
        {
            this.ErrorCode = errorCode;
            this.DisableMinutes = disableMinutes;
        }

        /// <summary>Gets the Azure REST API error code.</summary>
        public string ErrorCode { get; }

        /// <summary>Gets the number of minutes the resource should be disabled.</summary>
        public int DisableMinutes { get; }
    }
}
