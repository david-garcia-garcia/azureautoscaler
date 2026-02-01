using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using poolautoscaler;
using poolautoscaler.dimensions;
using poolautoscaler.licensing;
using poolautoscaler.resources;
using poolautoscaler.strategies;
using poolautoscaler.utils;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using InteractiveBrowserCredential = Azure.Identity.InteractiveBrowserCredential;

namespace AzureSqlElasticPoolAutoscaler
{
    class Program
    {
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
                Console.WriteLine("Usage: poolautoscaler generate-license <private_key.pem> <licensedTo> <expirationDate> <maxResources>");
                Console.WriteLine();
                Console.WriteLine("Example:");
                Console.WriteLine("  poolautoscaler generate-license private_key.pem \"Acme Corp\" \"2025-12-31T23:59:59Z\" 10");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine("  private_key.pem  - Path to RSA private key file");
                Console.WriteLine("  licensedTo       - Name of the licensee");
                Console.WriteLine("  expirationDate   - Expiration date in ISO 8601 format (e.g., 2025-12-31T23:59:59Z)");
                Console.WriteLine("  maxResources     - Maximum number of Azure resources allowed");
                Environment.Exit(1);
                return;
            }

            var privateKeyPath = args[0];
            var licensedTo = args[1];
            var expirationDateStr = args[2];
            var maxResourcesStr = args[3];

            if (!File.Exists(privateKeyPath))
            {
                Console.Error.WriteLine($"Error: Private key file not found: {privateKeyPath}");
                Environment.Exit(1);
                return;
            }

            DateTimeOffset expirationDateOffset;
            try
            {
                // Parse as DateTimeOffset using ParseExact to ensure correct UTC handling
                // Try ISO 8601 format with Z suffix first
                if (expirationDateStr.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                {
                    expirationDateOffset = DateTimeOffset.ParseExact(
                        expirationDateStr,
                        "yyyy-MM-ddTHH:mm:ssZ",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind | System.Globalization.DateTimeStyles.AssumeUniversal);
                }
                else
                {
                    // Fallback to general parse
                    expirationDateOffset = DateTimeOffset.Parse(expirationDateStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
                }
            }
            catch (FormatException)
            {
                Console.Error.WriteLine($"Error: Invalid expiration date format: {expirationDateStr}");
                Console.Error.WriteLine("Expected ISO 8601 format (e.g., 2025-12-31T23:59:59Z)");
                Environment.Exit(1);
                return;
            }

            if (!int.TryParse(maxResourcesStr, out var maxResources) || maxResources < 1)
            {
                Console.Error.WriteLine($"Error: maxResources must be a positive integer: {maxResourcesStr}");
                Environment.Exit(1);
                return;
            }

            try
            {
                var privateKeyPem = File.ReadAllText(privateKeyPath);
                // Pass the DateTimeOffset directly to avoid conversion issues
                var jwt = poolautoscaler.licensing.LicenseGenerator.GenerateLicense(
                    licensedTo,
                    expirationDateOffset,
                    maxResources,
                    privateKeyPem);

                Console.WriteLine("========================================");
                Console.WriteLine("License JWT Generated Successfully");
                Console.WriteLine("========================================");
                Console.WriteLine();
                Console.WriteLine("Set this environment variable:");
                Console.WriteLine();
                Console.WriteLine($"export AUTOSCALER_LICENSE='{jwt}'");
                Console.WriteLine();
                Console.WriteLine("Or in PowerShell:");
                Console.WriteLine();
                Console.WriteLine($"$env:AUTOSCALER_LICENSE='{jwt}'");
                Console.WriteLine();
                Console.WriteLine("========================================");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error generating license: {ex.Message}");
                Environment.Exit(1);
            }
        }

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
                    var licenseJwt = Environment.GetEnvironmentVariable("AUTOSCALER_LICENSE");
                    var validator = new LicenseValidator();
                    var license = validator.ValidateAndLoadLicense();
                    var isExpired = license != null && validator.IsExpired(license);
                    var isValid = validator.IsValid;

                    if (!isValid || isExpired || string.IsNullOrEmpty(licenseJwt))
                    {
                        // Expired, invalid, or no license: trace mode only
                        logging.SetMinimumLevel(LogLevel.Trace);
                    }
                    else
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                });
        }

        public class AutoscalerService : BackgroundService
        {
            private readonly IConfiguration RawConfiguration;
            private readonly ILoggerFactory LogFactory;
            private readonly ILogger Logger;
            private readonly Configuration Configuration;
            private readonly LicenseInfo LicenseInfo;
            private readonly DateTime _startTime;
            private readonly LicenseValidator _licenseValidator;

            public AutoscalerService(IConfiguration configuration, ILoggerFactory factory)
            {
                this.RawConfiguration = configuration;
                this.Configuration = configuration.Get<Configuration>();
                this.LogFactory = factory;
                this.Logger = factory.CreateLogger("autoscaler");

                // Validate and load license
                var validator = new LicenseValidator();
                var license = validator.ValidateAndLoadLicense();
                var isExpired = license != null && validator.IsExpired(license);
                this.LicenseInfo = new LicenseInfo(license ?? validator.CreateExpiredDefaultLicense(), validator.IsValid, isExpired);

                // Store validator for error reporting
                _licenseValidator = validator;

                // Track startup time for expired license error checking
                _startTime = DateTime.UtcNow;

                // Log version information
                PrintVersionInfo();

                // Log license information
                PrintLicenseInfo();

                // Apply logging restrictions for expired or invalid licenses
                if (this.LicenseInfo.IsRestricted)
                {
                    // Force trace level logging only
                    factory.CreateLogger("autoscaler").LogTrace("License expired or invalid - trace mode logging only");
                }

                this.Configuration.PrepareAndValidate(this.Logger);
            }

            private void PrintVersionInfo()
            {
                var version = GetVersion();
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

            private void PrintLicenseInfo()
            {
                var license = this.LicenseInfo.License;
                var now = DateTime.UtcNow;
                var timeUntilExpiration = license.ExpirationDate - now;
                var daysRemaining = (int)timeUntilExpiration.TotalDays;

                // Log basic license information in one line
                this.Logger.LogInformation(
                    "License: LicensedTo={LicensedTo}, DaysRemaining={DaysRemaining}, MaxResources={MaxResources}",
                    license.LicensedTo,
                    daysRemaining,
                    license.MaxResources);

                // Optionally log license issues if expired or has errors
                if (this.LicenseInfo.IsRestricted)
                {
                    if (this.LicenseInfo.IsExpired)
                    {
                        this.Logger.LogWarning(
                            "License expired. Limited functionality enabled.");
                    }
                    else if (!this.LicenseInfo.IsValid)
                    {
                        this.Logger.LogWarning(
                            "License invalid. Limited functionality enabled.");
                    }

                    // Show validation error if available
                    if (!string.IsNullOrEmpty(_licenseValidator.LastError))
                    {
                        this.Logger.LogWarning("License Validation Error: {Error}", _licenseValidator.LastError);
                    }
                }
            }

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                List<IDimension> dimensions = new List<IDimension>();
                dimensions.Add(new DimensionAzureSqlElasticPoolDtu());
                dimensions.Add(new DimensionAzureSqlElasticPoolMaxDataBytes());
                dimensions.Add(new DimensionAzureSqlDatabaseDtu());
                dimensions.Add(new DimensionAzureSqlDatabaseMaxDataBytes());
                dimensions.Add(new DimensionMySqlFlexibleServerSku());
                dimensions.Add(new DimensionMySqlFlexibleServerCoreCount());
                dimensions.Add(new DimensionAzureAksNodePoolMinNodeCount());
                dimensions.Add(new DimensionStorageFileShareProvisionedStorage());
                dimensions.Add(new DimensionMySqlFlexibleServerIops());
                dimensions.Add(new DimensionStorageFileShareThroughput());
                dimensions.Add(new DimensionFabricCapacitySku());
                dimensions.Add(new DimensionAzureDevOpsHostedParallelJobs());
                dimensions.Add(new DimensionAzureDevOpsPrivateParallelJobs());

                TokenCredential credential = null;

                switch (Configuration.AzureCredentialType)
                {
                    case "DeviceCodeCredential":
                        credential = new DeviceCodeCredential();
                        break;
                    case "DefaultAzureCredential":
                    case "":
                    case null:
                        credential = new DefaultAzureCredential();
                        break;
                    default:
                        throw new Exception("Unsupported AzureCredentialType. Available options are DeviceCodeCredential, DefaultAzureCredential.");
                }

                if (Debugger.IsAttached && string.IsNullOrWhiteSpace(Configuration.AzureCredentialType))
                {
                    this.Logger.LogInformation("Using interactive browser credentials when debugger is attached.");
                    credential = new InteractiveBrowserCredential();
                }

                Dictionary<string, ResourceState> Resources = new Dictionary<string, ResourceState>();

                // Does this expire?
                ArmClient client = new ArmClient(credential);

                DateTime lastResourceDiscovery = DateTime.MinValue;

                // Initial resource discovery
                this.Logger.LogInformation("Performing initial resource discovery and expansion...");
                await DiscoverResources(client, Resources, stoppingToken);
                lastResourceDiscovery = DateTime.UtcNow;

                while (!stoppingToken.IsCancellationRequested)
                {
                    // Expired or invalid license: throw error after 12 hours of runtime
                    if (this.LicenseInfo.IsRestricted)
                    {
                        CheckAndThrowErrorAfter12Hours();
                    }

                    // Check if we need to discover new resources
                    if ((DateTime.UtcNow - lastResourceDiscovery) >= Configuration.ResourceDiscoveryFrequencyParsed)
                    {
                        try
                        {
                            await DiscoverResources(client, Resources, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogError(ex, "Failed to discover resources: {0}", ex.Message);
                        }
                        finally
                        {
                            // wait for next timeout to try again
                            lastResourceDiscovery = DateTime.UtcNow;
                        }
                    }

                    // Process all resources, but limit if license is expired or invalid
                    int processedCount = 0;

                    foreach (var resourceState in Resources.Values)
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            this.Logger.LogWarning("Program::ExecuteAsync IsCancellationRequested=true");
                            break;
                        }

                        if (resourceState.Configuration.Enabled == false)
                        {
                            continue;
                        }

                        if (resourceState.NextEvaluationSeconds() > 0)
                        {
                            continue;
                        }

                        // Skip if license is restricted and we've reached the limit
                        if (this.LicenseInfo.IsRestricted && processedCount >= this.LicenseInfo.License.MaxResources)
                        {
                            resourceState.Logger.LogWarning("License {0}: Skipping resource (limit: {2} resources)",
this.LicenseInfo.Reason, this.LicenseInfo.License.MaxResources);
                            continue;
                        }

                        try
                        {
                            await RunLoop(dimensions, resourceState, stoppingToken, credential, client, LicenseInfo);
                            processedCount++;
                        }
                        catch (ResourceNotFoundException ex)
                        {
                            this.Logger.LogWarning("Resource '{0}' no longer exists in Azure and will be skipped.", ex.ResourceId);
                        }
                        catch (Exception ex)
                        {
                            resourceState.Logger.LogError(ex, ex.Message);
                            resourceState.Logger.LogWarning("Resource evaluation will be disabled for 1 hour."); // Hardcoded right now
                            resourceState.DisabledUntil["Unhandled exception"] = DateTime.UtcNow.AddHours(1);
                        }
                        finally
                        {
                            resourceState?.ResetEvaluation();
                        }

                        resourceState?.Logger.LogDebug("Next evaluation in {0}", TimeSpan.FromSeconds(resourceState.NextEvaluationSeconds()).ToString("g"));
                    }

                    await Task.Delay(2000, stoppingToken);
                }
            }

            private async Task DiscoverResources(ArmClient client, Dictionary<string, ResourceState> resources, CancellationToken stoppingToken)
            {
                var discoveredResources = new HashSet<string>();
                int addedResources = 0;
                int removedResources = 0;

                // Phase 1: Discover all resources that should exist and add new ones
                foreach (var resource in this.Configuration.Resources)
                {
                    if (resource.Enabled == false)
                    {
                        this.Logger.LogTrace($"Skipping disabled resource: " + string.Join(",", resource.Resources.Values.Select(i => i.Id)));
                        continue;
                    }

                    foreach (var resourceInstance in resource.Resources)
                    {
                        Dictionary<string, string> expandedResourceIds;
                        try
                        {
                            expandedResourceIds = await ResourceStateFactory.ExpandResources(client, resourceInstance.Key, resourceInstance.Value.ResourceId, this.Logger, stoppingToken);
                        }
                        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                        {
                            // Resource/cluster was deleted, skip it
                            this.Logger.LogError("Skipping deleted or non existing resource during expansion: {0}. Please remove this resource from your configuration.", resourceInstance.Value.ResourceId);
                            continue;
                        }
                        catch (Azure.RequestFailedException ex) when (ex.Status == 403)
                        {
                            // Authorization error - log as error to alert operator
                            this.Logger.LogError(ex, "Not authorized to access resource during expansion: {0}. Please check your credentials and permissions.", resourceInstance.Value.ResourceId);
                            continue;
                        }

                        foreach (var expandedResourceId in expandedResourceIds)
                        {
                            resourceInstance.Value.Id = resourceInstance.Key;
                            discoveredResources.Add(expandedResourceId.Key);

                            // Check if this resource already exists
                            if (resources.ContainsKey(expandedResourceId.Key))
                            {
                                // Resource already exists - keep it as-is (optimal: no reinstantiation)
                                this.Logger.LogTrace("Keeping existing resource: {0}", expandedResourceId.Value);
                            }
                            else
                            {
                                // New resource - add it
                                this.Logger.LogInformation("Adding new resource {0}: {1}", expandedResourceId.Key, expandedResourceId.Value);
                                
                                var resourceLogger = this.LogFactory.CreateLogger(expandedResourceId.Key);
                                var state = ResourceStateFactory.Create(expandedResourceId.Value, resourceLogger, resource, resourceInstance.Value);
                                resourceLogger.LogDebug("Replacements: {0}", string.Join(", ", state.ResourceParts.Select((i) => $"{i.Key}={i.Value}")));
                                resources[expandedResourceId.Key] = state;
                                addedResources++;
                            }
                        }
                    }
                }

                // Phase 2: Cleanup - remove resources that no longer exist
                var resourcesToRemove = new List<string>();
                foreach (var existingResourceKey in resources.Keys)
                {
                    if (!discoveredResources.Contains(existingResourceKey))
                    {
                        resourcesToRemove.Add(existingResourceKey);
                    }
                }

                foreach (var resourceKey in resourcesToRemove)
                {
                    this.Logger.LogInformation("Resource no longer found, removing: {0}", resources[resourceKey].Resource.Id);
                    resources.Remove(resourceKey);
                    removedResources++;
                }

                this.Logger.LogInformation("Resource introspection completed. Total resources: {0} (Added: {1}, Removed: {2})",
                    resources.Count,
                    addedResources,
                    removedResources);
            }

            private void CheckAndThrowErrorAfter12Hours()
            {
                var runtime = DateTime.UtcNow - _startTime;

                // Throw error once after 12 hours of runtime
                if (runtime.TotalHours >= 12)
                {
                    throw new InvalidOperationException("License expired: Application has been running for more than 12 hours - container restart required");
                }
            }

            private async Task RunLoop(
                List<IDimension> dimensions,
                ResourceState state,
                CancellationToken stoppingToken,
                TokenCredential credential,
                ArmClient armClient,
                LicenseInfo licenseInfo)
            {
                var logger = state.Logger;

                var finder = new ConfigFinder();

                var scalingConfigurations = (from p in state.Configuration.ScalingConfigurations.Values
                                             where finder.SettingIsActive(p, DateTime.UtcNow)
                                             select p).ToList();

                if (!scalingConfigurations.Any())
                {
                    logger.LogTrace("No scaling configurations apply right now.");
                    return;
                }

                logger.LogTrace("The following ScalingConfigurations are active and will be evaluated: " + string.Join(", ", scalingConfigurations.Select((i) => i.Id)));

                // Refresh resource state
                await state.Refresh(armClient, credential, stoppingToken);

                logger.LogDebug("Existing object state {0}", HelperExtensions.SerializeSimple(state.ExistingStateRaw));

                if (state.IsDisabled())
                {
                    logger.LogDebug("Resource is currently disabled: " + string.Join(", ", state.DisabledUntil.Keys));
                    return;
                }

                foreach (var setting in scalingConfigurations)
                {
                    var metricsClient = new MetricsQueryClient(credential, new MetricsQueryClientOptions(MetricsQueryClientOptions.ServiceVersion.V2018_01_01));

                    Dictionary<string, MetricEvalDtoResult> metrics = await GatherMetrics(
                        setting,
                        state,
                        metricsClient,
                        credential,
                        armClient,
                        stoppingToken,
                        logger);

                    logger.LogTrace("Evaluating scale configuration {0}", setting.Id);

                    foreach (var rule in setting.ScalingRules.Values)
                    {
                        IDimension dimension = null;

                        foreach (var dim in dimensions)
                        {
                            if (dim.CanApplyDimension(state, rule, logger))
                            {
                                dimension = dim;
                                break;
                            }
                        }

                        if (dimension == null)
                        {
                            // This is not necessarily bad. We might want to setup rules that are generalistic (i.e. for all databsaes in an sql) but some of them
                            // will only work for specific tiers. I.E. you might have mixed ElasticPools + Indpendant SKU in the same server, and not all rules
                            // will work for all of them.
                            logger.LogDebug($@"No dimension handler compatible with dimension '{rule.Dimension}' found in this resource. Rule: '{rule.Id}'");
                            continue;
                        }

                        logger.LogDebug("Current request state {0}", HelperExtensions.SerializeSimple(state.RequestedStateRaw));
                        logger.LogDebug("Evaluating rule {0}", rule.Id);

                        dimension.ValidateRuleConfiguration(rule);

                        var strategy = GetRuleStrategy(rule);

                        var currentDimensionValue = dimension.GetCurrentDimensionValue(state);

                        var targetDimensionValue = await strategy.EvaluateTargetDimensionValue(rule, dimension, state, logger, credential, stoppingToken, metrics);

                        // If we are going to scale down, do it wisely because resources are billed by natural hour
                        if (dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == -1)
                        {
                            if (state.LastScale != null && (DateTime.UtcNow - state.LastScale).Value.TotalSeconds <
                                rule.ScaleDownCooldownSeconds)
                            {
                                logger.LogTrace(
                                    "Skipping scale down from {0} to {1} because ScaleUpCooldownSeconds {2}s have not yet passed.",
                                    currentDimensionValue, targetDimensionValue, rule.ScaleDownCooldownSeconds);
                                continue;
                            }

                            // 10 Minute window for scale downs. The 5 minute margin at the end of the hour is to make
                            // sure the scaling operation is completed before the hour ends.
                            if (setting.ScaleDownLockWindowMinutes.HasValue && DateTime.UtcNow.Minute < setting.ScaleDownLockWindowMinutes)
                            {
                                logger.LogTrace(
                                    "Skipping scale down from {0} to {1} not allowed before minute {2} of a billable hour.",
                                    currentDimensionValue, targetDimensionValue, setting.ScaleDownLockWindowMinutes);
                                continue;
                            }
                        }

                        // If we are going to scale up, consider that there is a cooldown period so that metrics reflect properly
                        // the impact of scale up.
                        if (dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == 1)
                        {
                            if (state.LastScale != null && (DateTime.UtcNow - state.LastScale).Value.TotalSeconds <
                                rule.ScaleUpCooldownSeconds)
                            {
                                logger.LogTrace(
                                    "Skipping scale up from {0} to {1} because ScaleUpCooldownSeconds {2}s have not yet passed.",
                                    currentDimensionValue, targetDimensionValue, rule.ScaleUpCooldownSeconds);
                                continue;
                            }

                            // It might make no sense to scale up if only a few minutes are left in the current billable hour. In
                            // the long run this should be compensated with some predictive scaling based on historical data so that
                            // scaling is not "reactive" but "proactive".
                            if (setting.ScaleDownLockWindowMinutes.HasValue && DateTime.UtcNow.Minute > setting.ScaleUpAllowWindowMinutes)
                            {
                                logger.LogTrace(
                                    "Skipping scale up from {0} to {1} not allowed after minute {0} of a billable hour.",
                                    currentDimensionValue, targetDimensionValue, setting.ScaleUpAllowWindowMinutes);
                                continue;
                            }
                        }

                        logger.LogTrace("Setting target value {0}", targetDimensionValue);

                        var existingDimensionRequest = dimension.GetRequestedDimensionValue(state);

                        // Expired or invalid license: add 1 minute delay to scaling operations
                        if (licenseInfo.IsRestricted)
                        {
                            logger.LogTrace("License {0}: Adding 1 minute delay to scaling operation", licenseInfo.Reason);
                            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        }

                        // La dimensión siempre la establecemos, se encarga el estado del recurso de aceptar o no el cambio si han habido otras peticiones
                        await dimension.SetDimensionValue(stoppingToken, state, logger, credential, targetDimensionValue);

                        var newDimensionRequest = dimension.GetRequestedDimensionValue(state);

                        if (existingDimensionRequest != newDimensionRequest)
                        {
                            logger.LogDebug("Rule {0} Dimension request changed from {1} to {2}", rule.Id, existingDimensionRequest, newDimensionRequest);
                        }
                    }
                }

                // Prepare patch info
                var patchOperation = state.PreparePatch();

                if (patchOperation.HasChanges)
                {
                    logger.LogInformation("Existing resource state {0}", HelperExtensions.SerializeSimple(state.ExistingStateRaw));
                    logger.LogInformation("Target resource state: {0}", HelperExtensions.SerializeSimple(patchOperation.PatchData));
                    logger.LogInformation("Patch operation disruptive: {0}", patchOperation.Disruptive ? "yes" : "no");

                    if (state.Configuration.WhatIf == true)
                    {
                        logger.LogInformation("Patching Resource (WHATIF)");
                    }
                    else
                    {
                        await state.ApplyChanges(patchOperation, stoppingToken);
                    }

                    state.LastScale = DateTime.UtcNow;
                }
                else
                {
                    logger.LogDebug("No changes in patch operation");
                    logger.LogDebug("Target resource state: {0}", HelperExtensions.SerializeSimple(patchOperation.PatchData));
                }
            }

            private async Task<Dictionary<string, MetricEvalDtoResult>> GatherMetrics(
                ScalingConfiguration setting,
                ResourceState state,
                MetricsQueryClient metricsClient,
                TokenCredential credential,
                ArmClient armClient,
                CancellationToken stoppingToken,
                ILogger logger)
            {
                Dictionary<string, MetricEvalDtoResult> metrics = new Dictionary<string, MetricEvalDtoResult>();

                MetricEvaluation eval = new MetricEvaluation(logger);

                if (setting.Metrics != null)
                {
                    foreach (var metric in setting.Metrics.Values)
                    {
                        if (metric.Name.StartsWith("custom_"))
                        {
                            metrics.Add(metric.Id,
                                await state.CustomMetric(armClient, credential, stoppingToken, setting,
                                    metric.Name));
                            continue;
                        }

                        var metricWindow = TimeSpan.Parse(metric.Window);
                        var metricTimeGrain = TimeSpan.Parse(metric.TimeGrain ?? "00:01");

                        var targetResource = metric.ResourceId ?? state.Resource.Id;

                        string splitName = metric.SplitName;
                        string splitValue = metric.SplitValue;

                        targetResource = state.ReplaceResourceParts(targetResource);
                        splitName = state.ReplaceResourceParts(splitName);
                        splitValue = state.ReplaceResourceParts(splitValue);

                        List<MetricAggregationType> aggregations = new List<MetricAggregationType>()
                        {
                            MetricAggregationType.Average
                        };

                        if (metric.ParsedAggregations != null)
                        {
                            aggregations = metric.ParsedAggregations;
                        }

                        if (!aggregations.Any())
                        {
                            throw new Exception(
                                "Empty metric aggregation types. Aggregations should be explicitly set to avoid mismatch between rules and metric data.");
                        }

                        logger.LogTrace($"Metrics query: Name={metric.Name}, SplitName={splitName}, SplitValue={splitValue}, Aggregations={string.Join(", ", aggregations)} TargetResource={targetResource}, TimeRange={Math.Round(metricWindow.TotalHours, 2)}h");

                        List<MetricEvalDtoResultValue> values;
                        try
                        {
                            values = await eval.RetrieveHistory(
                                metricsClient,
                                targetResource,
                                metric.Name,
                                metricWindow,
                                metricTimeGrain,
                                stoppingToken,
                                splitName,
                                splitValue,
                                aggregations);
                        }
                        catch (Azure.RequestFailedException ex) when (ex.Status == 400)
                        {
                            if (metric.AllowFail)
                            {
                                logger.LogDebug($"Failed to load metric configuration (allowed as per configuration). {ex.Message}", metric.Id, metric.Name, targetResource);
                                continue;
                            }
                            ExceptionDispatchInfo.Capture(ex).Throw();
                            throw;
                        }

                        values.Reverse();

                        // Remove data points without data only from the start of the time series
                        var originalCount = values.Count;
                        values = values.SkipWhile(v => !v.HasData()).ToList();
                        var removedCount = originalCount - values.Count;

                        // One datapoint loss is commong due to how metric windows work.
                        if (removedCount > 1)
                        {
                            logger.LogDebug($"Removed {removedCount} data points from a total of {originalCount} without data from the beginning of the time series for {metric.Name}. This is not necessarily bad. Review your metrics configuration.");
                        }

                        metrics.Add(metric.Id, new MetricEvalDtoResult());
                        metrics[metric.Id].Values = values.Select((i) => metric.TransformExpression(i)).ToList();
                    }
                }

                return metrics;
            }

            private IRuleStrategy GetRuleStrategy(ScalingRule rule)
            {
                if (rule.ScalingStrategy == "Fixed")
                {
                    return new RuleStrategyFixed();
                }
                else if (rule.ScalingStrategy == "Autoadjust")
                {
                    return new RuleStrategyAutoAdjust();
                }

                throw new ArgumentException($"Invalid scaling strategy: '{rule.ScalingStrategy}'");
            }
        }
    }
}