using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using poolautoscaler.resources;
using System.Net;
using System.Text;

namespace poolautoscaler.tests
{
    public class AzureDevOpsClientTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public AzureDevOpsClientTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public async Task GetBillingTokenAsync_WithValidResponse_ReturnsToken()
        {
            // Arrange
            var responseJson = """{"token": "test-bearer-token-12345"}""";
            var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act
            var token = await client.GetBillingTokenAsync("myorg", "test-pat", CancellationToken.None);

            // Assert
            Assert.Equal("test-bearer-token-12345", token);
        }

        [Fact]
        public async Task GetBillingTokenAsync_WithEmptyToken_ThrowsException()
        {
            // Arrange
            var responseJson = """{"token": ""}""";
            var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() =>
                client.GetBillingTokenAsync("myorg", "test-pat", CancellationToken.None));
        }

        [Fact]
        public async Task GetParallelJobsAsync_WithValidResponse_ReturnsCorrectCounts()
        {
            // Arrange
            var responseJson = """
            {
                "value": [
                    {
                        "meterId": "4bad9897-8d87-43bb-80be-5e6e8fefa3de",
                        "purchaseQuantity": 5,
                        "includedQuantity": 0
                    },
                    {
                        "meterId": "f44a67f2-53ae-4044-bd58-1c8aca386b98",
                        "purchaseQuantity": 3,
                        "includedQuantity": 1
                    }
                ]
            }
            """;
            var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act
            var result = await client.GetParallelJobsAsync("org-guid", "test-token", CancellationToken.None);

            // Assert
            Assert.Equal(5, result.HostedParallelJobs);
            Assert.Equal(3, result.PrivateParallelJobs);
        }

        [Fact]
        public async Task GetParallelJobsAsync_WithNoMeters_ReturnsZeros()
        {
            // Arrange
            var responseJson = """{"value": []}""";
            var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act
            var result = await client.GetParallelJobsAsync("org-guid", "test-token", CancellationToken.None);

            // Assert
            Assert.Equal(0, result.HostedParallelJobs);
            Assert.Equal(0, result.PrivateParallelJobs);
        }

        [Fact]
        public async Task GetParallelJobsAsync_WithIncludedQuantityOnly_ReturnsIncludedQuantity()
        {
            // Arrange
            var responseJson = """
            {
                "value": [
                    {
                        "meterId": "4bad9897-8d87-43bb-80be-5e6e8fefa3de",
                        "includedQuantity": 1
                    }
                ]
            }
            """;
            var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act
            var result = await client.GetParallelJobsAsync("org-guid", "test-token", CancellationToken.None);

            // Assert
            Assert.Equal(1, result.HostedParallelJobs);
        }

        [Fact]
        public async Task GetQueuedJobsAsync_WithQueuedAndRunningJobs_ReturnsCorrectCounts()
        {
            // Arrange
            var responseJson = """
            {
                "value": [
                    { "assignTime": null, "result": null },
                    { "assignTime": null, "result": null },
                    { "assignTime": "2024-01-01T10:00:00Z", "result": null },
                    { "assignTime": "2024-01-01T09:00:00Z", "result": "succeeded" }
                ]
            }
            """;
            var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act
            var result = await client.GetQueuedJobsAsync("myorg", "test-pat", 1, CancellationToken.None);

            // Assert
            Assert.Equal(2, result.QueuedJobs);  // Jobs without assignTime and without result
            Assert.Equal(1, result.RunningJobs); // Jobs with assignTime but no result
        }

        [Fact]
        public async Task GetQueuedJobsAsync_WithNoJobs_ReturnsZeros()
        {
            // Arrange
            var responseJson = """{"value": []}""";
            var httpClient = CreateMockHttpClient(responseJson, HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act
            var result = await client.GetQueuedJobsAsync("myorg", "test-pat", 1, CancellationToken.None);

            // Assert
            Assert.Equal(0, result.QueuedJobs);
            Assert.Equal(0, result.RunningJobs);
        }

        [Fact]
        public async Task SetParallelJobsAsync_WithValidRequest_CompletesSuccessfully()
        {
            // Arrange
            var httpClient = CreateMockHttpClient("{}", HttpStatusCode.OK);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act & Assert - Should not throw
            await client.SetParallelJobsAsync(
                "org-guid",
                "test-token",
                AzureDevOpsClient.MeterIdHostedPipeline,
                5,
                CancellationToken.None);
        }

        [Fact]
        public async Task SetParallelJobsAsync_WithErrorResponse_ThrowsHttpRequestException()
        {
            // Arrange
            var httpClient = CreateMockHttpClient("{\"error\": \"unauthorized\"}", HttpStatusCode.Unauthorized);
            var client = new AzureDevOpsClient(httpClient, _loggerMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.SetParallelJobsAsync(
                    "org-guid",
                    "test-token",
                    AzureDevOpsClient.MeterIdHostedPipeline,
                    5,
                    CancellationToken.None));
        }

        [Fact]
        public void MeterIdConstants_HaveCorrectValues()
        {
            // Assert - Verify the meter IDs match the documented values
            Assert.Equal("4bad9897-8d87-43bb-80be-5e6e8fefa3de", AzureDevOpsClient.MeterIdHostedPipeline);
            Assert.Equal("f44a67f2-53ae-4044-bd58-1c8aca386b98", AzureDevOpsClient.MeterIdPrivatePipeline);
        }

        private HttpClient CreateMockHttpClient(string responseContent, HttpStatusCode statusCode)
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
                });

            return new HttpClient(handlerMock.Object);
        }
    }
}
