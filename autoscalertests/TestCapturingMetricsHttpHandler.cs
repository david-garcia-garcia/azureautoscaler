using System.Net;
using System.Text;

namespace poolautoscaler.tests
{
    /// <summary>Captures Azure Monitor custom-metric POSTs for pusher tests.</summary>
    internal sealed class TestCapturingMetricsHttpHandler : HttpMessageHandler
    {
        /// <summary>Bodies of POST requests in order.</summary>
        public List<string> Bodies { get; } = new();

        /// <summary>Request URIs in order.</summary>
        public List<string> Uris { get; } = new();

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Uris.Add(request.RequestUri?.ToString() ?? string.Empty);
            this.Bodies.Add(request.Content == null ? string.Empty : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
