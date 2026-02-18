using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.licensing;
using poolautoscaler.resourcemanagement;
using InteractiveBrowserCredential = Azure.Identity.InteractiveBrowserCredential;

namespace AzureSqlElasticPoolAutoscaler
{
    /// <summary>Entry point and CLI (run service or generate license).</summary>
    class Program
    {
        /// <summary>Creates the host builder for the autoscaler service.</summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>The configured host builder.</returns>
        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);

            return builder
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    string file = Environment.GetEnvironmentVariable("CONFIG_FILE", EnvironmentVariableTarget.Process);

                    if (!File.Exists(file))
                    {
                        file = "/app/config.yml";
                    }

                    if (!File.Exists(file))
                    {
                        // Find the configuration file recursively upwards from the current bin directory
                        var currentDirectory = Directory.GetCurrentDirectory();
                        var parentDirectory = Directory.GetParent(currentDirectory);

                        while (parentDirectory != null)
                        {
                            var configFile = Path.Combine(parentDirectory.FullName, "config.yml");

                            if (File.Exists(configFile))
                            {
                                file = configFile;
                                break;
                            }

                            parentDirectory = parentDirectory.Parent;
                        }
                    }

                    config.AddYamlFile(file, optional: false, reloadOnChange: true);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();
                    services.AddMemoryCache();
                    services.AddSingleton<poolautoscaler.resourcemanagement.IResourceLocationResolver>(sp =>
                        new poolautoscaler.resourcemanagement.ResourceLocationResolver(
                            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                            sp.GetRequiredService<ILoggerFactory>().CreateLogger("ResourceLocationResolver")));
                    services.AddSingleton<poolautoscaler.metrics.IVmSizeResolver>(sp =>
                        new poolautoscaler.metrics.VmSizeResolver(
                            sp.GetRequiredService<ILoggerFactory>().CreateLogger("VmSizeResolver")));
                    services.AddSingleton<poolautoscaler.resourcemanagement.IResourceStateFactory, poolautoscaler.resourcemanagement.ResourceStateFactory>();
                    services.AddSingleton<LicenseService>();
                    services.AddSingleton<ResourceManager>();
                    services.AddHostedService<AutoscalerService>();
                })
                .ConfigureLogging((hostContext, logging) =>
                {
                    // These are just defaults, actual setup can be set through config.yml
                    // https://learn.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#set-formatter-with-configuration
                    logging.ClearProviders();
                    logging.AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss ";
                    });

                    // Check if license is expired/invalid and force trace mode
                    var licenseService = new LicenseService();
                    var licenseInfo = licenseService.GetLicenseInfo();
                    if (licenseInfo.IsRestricted)
                    {
                        logging.SetMinimumLevel(LogLevel.Trace);
                    }
                    else
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                });
        }

        static async Task Main(string[] args)
        {
            // There are hardcoded numbers in both config lambda expressions and tests, we don't want locale
            // messing up with that, and we dont want to support cultureinfo in this application at all.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // Determine command (default to "runservice" if none provided)
            var command = args.Length > 0 ? args[0] : "runservice";
            var commandArgs = args.Length > 0 ? args.Skip(1).ToArray() : Array.Empty<string>();

            switch (command.ToLowerInvariant())
            {
                case "runservice":
                    await RunService(commandArgs);
                    break;
                case "generate-license":
                    GenerateLicense(commandArgs);
                    break;
                default:
                    ShowUsage();
                    Environment.Exit(1);
                    break;
            }
        }

        static void ShowUsage()
        {
            Console.WriteLine("Azure Autoscaler");
            Console.WriteLine();
            Console.WriteLine("Usage: poolautoscaler [command] [arguments]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  runservice              Run the autoscaler service (default)");
            Console.WriteLine("  generate-license        Generate a license JWT token");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  poolautoscaler                                    # Run service (default)");
            Console.WriteLine("  poolautoscaler runservice                         # Run service explicitly");
            Console.WriteLine("  poolautoscaler generate-license <key> <to> <exp> <max>");
            Console.WriteLine();
        }

        static async Task RunService(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }

        static void GenerateLicense(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: poolautoscaler generate-license <private_key.pem> <licensedTo> <expirationDate> <maxResources> [allowedSubscriptionIds]");
                Console.WriteLine();
                Console.WriteLine("Example:");
                Console.WriteLine("  poolautoscaler generate-license private_key.pem \"Acme Corp\" \"2025-12-31T23:59:59Z\" 10");
                Console.WriteLine("  poolautoscaler generate-license private_key.pem \"Acme Corp\" \"2025-12-31T23:59:59Z\" 10 \"sub-id-1,sub-id-2\"");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine("  private_key.pem       - Path to RSA private key file");
                Console.WriteLine("  licensedTo            - Name of the licensee");
                Console.WriteLine("  expirationDate        - Expiration date in ISO 8601 format (e.g., 2025-12-31T23:59:59Z)");
                Console.WriteLine("  maxResources          - Maximum number of Azure resources allowed");
                Console.WriteLine("  allowedSubscriptionIds - Optional. Comma-separated Azure subscription IDs. When omitted or empty, all subscriptions are allowed.");
                Environment.Exit(1);
                return;
            }

            IReadOnlyList<string>? allowedSubscriptionIds = null;
            if (args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4]))
            {
                allowedSubscriptionIds = args[4]
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }

            var service = new poolautoscaler.licensing.LicenseService();
            var result = service.Generate(args[0], args[1], args[2], args[3], allowedSubscriptionIds);

            if (!result.Success)
            {
                Console.Error.WriteLine($"Error: {result.ErrorMessage}");
                Environment.Exit(1);
                return;
            }

            Console.WriteLine("========================================");
            Console.WriteLine("License JWT Generated Successfully");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Set this environment variable:");
            Console.WriteLine();
            Console.WriteLine($"export AUTOSCALER_LICENSE='{result.Jwt}'");
            Console.WriteLine();
            Console.WriteLine("Or in PowerShell:");
            Console.WriteLine();
            Console.WriteLine($"$env:AUTOSCALER_LICENSE='{result.Jwt}'");
            Console.WriteLine();
            Console.WriteLine("========================================");
        }

        /// <summary>Background service that discovers resources and runs the scaling loop.</summary>
        public class AutoscalerService : BackgroundService
        {
            private readonly IConfiguration RawConfiguration;
            private readonly ILoggerFactory LogFactory;
            private readonly ILogger Logger;
            private readonly Configuration Configuration;
            private readonly LicenseInfo LicenseInfo;
            private readonly DateTime startTime;
            private readonly LicenseService licenseService;
            private readonly ResourceManager resourceManager;
            private readonly poolautoscaler.resourcemanagement.IResourceLocationResolver resourceLocationResolver;

            /// <summary>Initializes a new instance of the <see cref="AutoscalerService"/> class.</summary>
            /// <param name="configuration">Application configuration.</param>
            /// <param name="factory">Logger factory.</param>
            /// <param name="licenseService">License service.</param>
            /// <param name="resourceManager">Resource manager.</param>
            /// <param name="resourceLocationResolver">Resolves resource IDs to region for custom metrics.</param>
            public AutoscalerService(IConfiguration configuration, ILoggerFactory factory, LicenseService licenseService, ResourceManager resourceManager, poolautoscaler.resourcemanagement.IResourceLocationResolver resourceLocationResolver)
            {
                this.RawConfiguration = configuration;
                this.Configuration = configuration.Get<Configuration>();
                this.LogFactory = factory;
                this.Logger = factory.CreateLogger("autoscaler");
                this.licenseService = licenseService;
                this.resourceManager = resourceManager;
                this.resourceLocationResolver = resourceLocationResolver;

                this.LicenseInfo = this.licenseService.GetLicenseInfo();

                // Track startup time for expired license error checking
                this.startTime = DateTime.UtcNow;

                // Log version information
                this.PrintVersionInfo();

                // Log license information
                this.licenseService.LogLicenseInfo(this.Logger);

                // Apply logging restrictions for expired or invalid licenses
                if (this.LicenseInfo.IsRestricted)
                {
                    // Force trace level logging only
                    factory.CreateLogger("autoscaler").LogTrace("License expired or invalid - trace mode logging only");
                }

                this.Configuration.PrepareAndValidate(this.Logger);
            }

            /// <inheritdoc/>
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                List<IDimension> dimensions = new List<IDimension>();
                dimensions.Add(new DimensionAzureSqlElasticPoolDtu());
                dimensions.Add(new DimensionAzureSqlElasticPoolMaxDataBytes());
                dimensions.Add(new DimensionAzureSqlDatabaseDtu());
                dimensions.Add(new DimensionAzureSqlDatabaseMaxDataBytes());
                dimensions.Add(new DimensionMySqlFlexibleServerSku());
                dimensions.Add(new DimensionMySqlFlexibleServerCoreCount());
                dimensions.Add(new DimensionPostgreSqlFlexibleServerSku());
                dimensions.Add(new DimensionPostgreSqlFlexibleServerCoreCount());
                dimensions.Add(new DimensionAzureAksNodePoolMinNodeCount());
                dimensions.Add(new DimensionStorageFileShareProvisionedStorage());
                dimensions.Add(new DimensionMySqlFlexibleServerIops());
                dimensions.Add(new DimensionPostgreSqlFlexibleServerIops());
                dimensions.Add(new DimensionStorageFileShareThroughput());
                dimensions.Add(new DimensionFabricCapacitySku());
                dimensions.Add(new DimensionAzureDevOpsHostedParallelJobs());
                dimensions.Add(new DimensionAzureDevOpsPrivateParallelJobs());

                TokenCredential credential;

                var credType = this.Configuration.AzureCredentialType;
                if (string.IsNullOrEmpty(credType))
                {
                    credential = new DefaultAzureCredential();
                }
                else
                {
                    switch (credType)
                    {
                        case "DeviceCodeCredential":
                            credential = new DeviceCodeCredential();
                            break;
                        case "DefaultAzureCredential":
                            credential = new DefaultAzureCredential();
                            break;
                        default:
                            throw new Exception("Unsupported AzureCredentialType. Available options are DeviceCodeCredential, DefaultAzureCredential.");
                    }
                }

                if (Debugger.IsAttached && string.IsNullOrWhiteSpace(this.Configuration.AzureCredentialType))
                {
                    this.Logger.LogInformation("Using interactive browser credentials when debugger is attached.");
                    credential = new InteractiveBrowserCredential();
                }

                ArmClient client = new ArmClient(credential);
                var armClientWrapper = new ArmClientWrapper(client);

                var resourceProcessor = new ResourceProcessor(
                    this.LogFactory,
                    dimensions,
                    credential,
                    armClientWrapper,
                    this.LicenseInfo,
                    this.resourceLocationResolver,
                    defaultCustomMetricsNamespace: this.Configuration.CustomMetricsNamespace);

                const int MinIterationIntervalSeconds = 2;

                while (!stoppingToken.IsCancellationRequested)
                {
                    var iterationStart = DateTime.UtcNow;

                    // Expired or invalid license: throw error after 12 hours of runtime
                    if (this.LicenseInfo.IsRestricted)
                    {
                        this.CheckAndThrowErrorAfter12Hours();
                    }

                    // Discover resources when due (first call runs immediately; then every ResourceDiscoveryFrequency)
                    await this.resourceManager.DiscoverAsync(client, this.Configuration.Resources, this.Configuration.ResourceDiscoveryFrequencyParsed, stoppingToken);

                    // Process each resource (one at a time; ResourceProcessor does enabled/eval checks and RunLoop)
                    int processedCount = 0;
                    foreach (var resourceState in this.resourceManager.Resources.Values)
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            this.Logger.LogWarning("Program::ExecuteAsync IsCancellationRequested=true");
                            break;
                        }

                        if (this.LicenseInfo.IsRestricted && processedCount >= this.LicenseInfo.License.MaxResources)
                        {
                            resourceState.Logger.LogWarning(
                                "License {0}: Skipping resource (limit: {1} resources)",
                                this.LicenseInfo.Reason,
                                this.LicenseInfo.License.MaxResources);
                            continue;
                        }

                        var subscriptionId = resourceState.ResourceParts.GetValueOrDefault("subscriptionId");
                        if (!this.LicenseInfo.License.IsSubscriptionAllowed(subscriptionId))
                        {
                            resourceState.Logger.LogWarning(
                                "License: Skipping resource - subscription {SubscriptionId} is not in the license allowed list.",
                                subscriptionId ?? "(none - non-ARM resource)");
                            continue;
                        }

                        if (await resourceProcessor.ProcessOneAsync(resourceState, stoppingToken))
                        {
                            processedCount++;
                        }
                    }

                    // Ensure at least MinIterationIntervalSeconds between iteration starts
                    var elapsed = (DateTime.UtcNow - iterationStart).TotalMilliseconds;
                    var remaining = (MinIterationIntervalSeconds * 1000) - elapsed;
                    if (remaining > 0)
                    {
                        await Task.Delay((int)remaining, stoppingToken);
                    }
                }
            }

            private void CheckAndThrowErrorAfter12Hours()
            {
                var runtime = DateTime.UtcNow - this.startTime;

                // Throw error once after 12 hours of runtime
                if (runtime.TotalHours >= 12)
                {
                    throw new InvalidOperationException("License expired: Application has been running for more than 12 hours - container restart required");
                }
            }

            private void PrintVersionInfo()
            {
                var version = this.GetVersion();
                this.Logger.LogInformation("Azure Autoscaler Version: {Version}", version);
            }

            private string GetVersion()
            {
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var versionAttribute = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
                    if (versionAttribute != null && !string.IsNullOrEmpty(versionAttribute.InformationalVersion))
                    {
                        return versionAttribute.InformationalVersion;
                    }

                    var version = assembly.GetName().Version;
                    if (version != null)
                    {
                        return version.ToString();
                    }
                }
                catch
                {
                    // Fall through to default
                }

                return "Unknown";
            }
        }
    }
}
