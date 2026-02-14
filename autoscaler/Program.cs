using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
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

            var service = new poolautoscaler.licensing.LicenseService();
            var result = service.Generate(args[0], args[1], args[2], args[3]);

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

        public class AutoscalerService : BackgroundService
        {
            private readonly IConfiguration RawConfiguration;
            private readonly ILoggerFactory LogFactory;
            private readonly ILogger Logger;
            private readonly Configuration Configuration;
            private readonly LicenseInfo LicenseInfo;
            private readonly DateTime _startTime;
            private readonly LicenseService _licenseService;
            private readonly ResourceManager _resourceManager;

            public AutoscalerService(IConfiguration configuration, ILoggerFactory factory, LicenseService licenseService, ResourceManager resourceManager)
            {
                this.RawConfiguration = configuration;
                this.Configuration = configuration.Get<Configuration>();
                this.LogFactory = factory;
                this.Logger = factory.CreateLogger("autoscaler");
                _licenseService = licenseService;
                _resourceManager = resourceManager;

                this.LicenseInfo = _licenseService.GetLicenseInfo();

                // Track startup time for expired license error checking
                _startTime = DateTime.UtcNow;

                // Log version information
                PrintVersionInfo();

                // Log license information
                _licenseService.LogLicenseInfo(this.Logger);

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

                TokenCredential credential;

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

                ArmClient client = new ArmClient(credential);

                const int MinIterationIntervalSeconds = 2;

                while (!stoppingToken.IsCancellationRequested)
                {
                    var iterationStart = DateTime.UtcNow;

                    // Expired or invalid license: throw error after 12 hours of runtime
                    if (this.LicenseInfo.IsRestricted)
                    {
                        CheckAndThrowErrorAfter12Hours();
                    }

                    // Discover resources when due (first call runs immediately; then every ResourceDiscoveryFrequency)
                    await _resourceManager.DiscoverAsync(client, Configuration.Resources, Configuration.ResourceDiscoveryFrequencyParsed, stoppingToken);

                    // Process all resources, but limit if license is expired or invalid
                    int processedCount = 0;

                    foreach (var resourceState in _resourceManager.Resources.Values)
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
                            resourceState.DisabledUntil["Unhandled exception: " + ex.Message] = DateTime.UtcNow.AddHours(1);
                        }
                        finally
                        {
                            resourceState?.ResetEvaluation();
                        }

                        resourceState?.Logger.LogDebug("Next evaluation in {0}", TimeSpan.FromSeconds(resourceState.NextEvaluationSeconds()).ToString("g"));
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
                    // Only log the disabled message once per hour to avoid log spam
                    if ((DateTime.UtcNow - state.LastDisabledMessageLogged).TotalHours >= 1)
                    {
                        logger.LogInformation("Resource is currently disabled: " + string.Join(", ", state.DisabledUntil.Keys));
                        state.LastDisabledMessageLogged = DateTime.UtcNow;
                    }
                    return;
                }

                // Reset the disabled message counter so it shows immediately if disabled again
                state.LastDisabledMessageLogged = DateTime.MinValue;

                // Wrap the logger in a capturing logger to capture messages that would be lost
                // If a scale operation happens, we'll flush these at INFO level so they're visible
                var capturingLogger = new CapturingLogger(logger);

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
                        capturingLogger);

                    // Output detailed metric information in debug mode - one compact line per metric
                    foreach (var metricEntry in metrics)
                    {
                        var metricId = metricEntry.Key;
                        var metricResult = metricEntry.Value;

                        if (!metricResult.Values.Any())
                        {
                            capturingLogger.LogDebug($"Metric={metricId}, valid={metricResult.Valid}, values=0, aggregation=none, valuedetail=<empty>");
                            continue;
                        }

                        // Determine aggregation type from first value
                        var aggregationType = metricResult.Values.First().GetAggregationType();

                        // Extract all values into a compact list with validity status
                        var valuesDetail = string.Join(",", metricResult.Values.Select(v => v.RenderValueWithStatus()));
                        var invalidReason = metricResult.Valid ? "" : $", reason=\"{metricResult.InvalidReason}\"";

                        capturingLogger.LogDebug($"Metric={metricId}, valid={metricResult.Valid}, values={metricResult.Values.Count}, aggregation={aggregationType}, valuedetail={valuesDetail}{invalidReason}");
                    }

                    capturingLogger.LogTrace("Evaluating scale configuration {0}", setting.Id);

                    // Check if any metrics required by this rule are invalid
                    var invalidMetrics = metrics.Where(m => !m.Value.Valid).ToList();
                    if (invalidMetrics.Any())
                    {
                        var metricNames = string.Join(", ", invalidMetrics.Select(m => m.Key));
                        var reasons = string.Join("; ", invalidMetrics.Select(m => $"{m.Key}: {m.Value.InvalidReason}"));
                        capturingLogger.LogWarning($"Skipping scaling configuratoin '{setting.Id}' because one or more metrics are invalid: {reasons}");
                        continue;
                    }

                    foreach (var rule in setting.ScalingRules.Values)
                    {
                        IDimension dimension = null;

                        foreach (var dim in dimensions)
                        {
                            if (dim.CanApplyDimension(state, rule, capturingLogger))
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
                            capturingLogger.LogDebug($@"No dimension handler compatible with dimension '{rule.Dimension}' found in this resource. Rule: '{rule.Id}'");
                            continue;
                        }

                        capturingLogger.LogDebug("Current request state {0}", HelperExtensions.SerializeSimple(state.RequestedStateRaw));
                        capturingLogger.LogTrace("Evaluating rule {0}", rule.Id);

                        dimension.ValidateRuleConfiguration(rule);

                        var strategy = GetRuleStrategy(rule);

                        var currentDimensionValue = dimension.GetCurrentDimensionValue(state);

                        var targetDimensionValue = await strategy.EvaluateTargetDimensionValue(rule, dimension, state, capturingLogger, credential, stoppingToken, metrics);

                        // If we are going to scale down, do it wisely because resources are billed by natural hour
                        if (dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == -1)
                        {
                            if (state.LastScale != null && (DateTime.UtcNow - state.LastScale).Value.TotalSeconds <
                                rule.ScaleDownCooldownSeconds)
                            {
                                capturingLogger.LogTrace(
                                    "Skipping scale down from {0} to {1} because ScaleUpCooldownSeconds {2}s have not yet passed.",
                                    currentDimensionValue, targetDimensionValue, rule.ScaleDownCooldownSeconds);
                                continue;
                            }

                            // 10 Minute window for scale downs. The 5 minute margin at the end of the hour is to make
                            // sure the scaling operation is completed before the hour ends.
                            if (setting.ScaleDownLockWindowMinutes.HasValue && DateTime.UtcNow.Minute < setting.ScaleDownLockWindowMinutes)
                            {
                                capturingLogger.LogTrace(
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
                                capturingLogger.LogTrace(
                                    "Skipping scale up from {0} to {1} because ScaleUpCooldownSeconds {2}s have not yet passed.",
                                    currentDimensionValue, targetDimensionValue, rule.ScaleUpCooldownSeconds);
                                continue;
                            }

                            // It might make no sense to scale up if only a few minutes are left in the current billable hour. In
                            // the long run this should be compensated with some predictive scaling based on historical data so that
                            // scaling is not "reactive" but "proactive".
                            if (setting.ScaleDownLockWindowMinutes.HasValue && DateTime.UtcNow.Minute > setting.ScaleUpAllowWindowMinutes)
                            {
                                capturingLogger.LogTrace(
                                    "Skipping scale up from {0} to {1} not allowed after minute {0} of a billable hour.",
                                    currentDimensionValue, targetDimensionValue, setting.ScaleUpAllowWindowMinutes);
                                continue;
                            }
                        }

                        var existingDimensionRequest = dimension.GetRequestedDimensionValue(state);

                        // Expired or invalid license: add 1 minute delay to scaling operations
                        if (licenseInfo.IsRestricted)
                        {
                            capturingLogger.LogInformation("License {0}: Adding 1 minute delay to scaling operation", licenseInfo.Reason);
                            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        }

                        // La dimensión siempre la establecemos, se encarga el estado del recurso de aceptar o no el cambio si han habido otras peticiones
                        await dimension.SetDimensionValue(stoppingToken, state, capturingLogger, credential, targetDimensionValue);

                        var newDimensionRequest = dimension.GetRequestedDimensionValue(state);

                        if (existingDimensionRequest != newDimensionRequest)
                        {
                            capturingLogger.LogDebug("Rule '{0}' Dimension request changed from {1} to {2}", rule.Id, existingDimensionRequest ?? "(null)", newDimensionRequest);
                        }
                        else
                        {
                            capturingLogger.LogDebug("Rule '{0}' requested target value '{1}'", rule.Id, targetDimensionValue);
                        }
                    }
                }

                // Prepare patch info
                var patchOperation = state.PreparePatch();

                if (patchOperation.HasChanges)
                {
                    // Replay any captured messages as INFO so they're visible.
                    // The capturing logger only captures messages the inner logger doesn't show,
                    // so this will be empty if running at DEBUG/TRACEa level.
                    capturingLogger.Replay(LogLevel.Information, LogLevel.Debug);

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

                // Clean up the capturing logger buffer
                capturingLogger.Clear();
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

                        MetricEvalDtoResult metricResult;

                        try
                        {
                            metricResult = await eval.RetrieveHistory(
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

                        metricResult.Values.Reverse();

                        // Remove data points without data only from the start of the time series
                        var originalCount = metricResult.Values.Count;
                        metricResult.Values = metricResult.Values.SkipWhile(v => !v.HasData()).ToList();
                        var removedCount = originalCount - metricResult.Values.Count;

                        // One datapoint loss is commong due to how metric windows work.
                        if (removedCount > 1)
                        {
                            logger.LogDebug($"Removed {removedCount} data points from a total of {originalCount} without data from the beginning of the time series for {metric.Name}. This is not necessarily bad. Review your metrics configuration.");
                        }

                        // Apply transform expression to values
                        metricResult.Values = metricResult.Values.Select((i) => metric.TransformExpression(i)).ToList();

                        // Validate each data point in the metric against configured bounds
                        // Mark individual values as invalid, then assess overall metric validity
                        if (metricResult.Values.Any() && (metric.ValidValueMin.HasValue || metric.ValidValueMax.HasValue))
                        {
                            foreach (var value in metricResult.Values)
                            {
                                // Use the Default property which contains the primary aggregation value
                                if (value.Default.HasValue)
                                {
                                    if (metric.ValidValueMin.HasValue && value.Default.Value < metric.ValidValueMin.Value)
                                    {
                                        value.Valid = false;
                                        value.InvalidReason = $"Value {value.Default.Value:F2} is below minimum valid value {metric.ValidValueMin.Value:F2}";
                                    }
                                    else if (metric.ValidValueMax.HasValue && value.Default.Value > metric.ValidValueMax.Value)
                                    {
                                        value.Valid = false;
                                        value.InvalidReason = $"Value {value.Default.Value:F2} is above maximum valid value {metric.ValidValueMax.Value:F2}";
                                    }
                                }
                            }

                            // If any value is invalid, mark the entire metric as invalid
                            var invalidValues = metricResult.Values.Where(v => !v.Valid).ToList();
                            if (invalidValues.Any())
                            {
                                metricResult.Valid = false;
                                metricResult.InvalidReason = $"{invalidValues.Count} out of {metricResult.Values.Count} data points are invalid (e.g., {invalidValues.First().InvalidReason} at {invalidValues.First().TimeStamp:yyyy-MM-dd HH:mm:ss})";
                                logger.LogWarning($"Metric '{metric.Name}' appears broken or unreliable: {metricResult.InvalidReason}");
                            }
                        }

                        metrics.Add(metric.Id, metricResult);
                    }
                }

                // Set default values, not the best place, but covers custom and regular metrics
                foreach (var metric in metrics.Values)
                {
                    foreach (var metricValue in metric.Values)
                    {
                        metricValue.Default = metric.PrimaryAggregation switch
                        {
                            MetricAggregationType.Average => metricValue.Average,
                            MetricAggregationType.Maximum => metricValue.Maximum,
                            MetricAggregationType.Minimum => metricValue.Minimum,
                            MetricAggregationType.Total => metricValue.Total,
                            MetricAggregationType.Count => metricValue.Count,
                            null => metricValue.Average ?? metricValue.Maximum ?? metricValue.Minimum ?? metricValue.Total ?? metricValue.Count
                        };
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