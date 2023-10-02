// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Aspire.StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using StackExchange.Redis;

namespace Microsoft.Extensions.Hosting;

public static class AspireRedisExtensions
{
    private const string DefaultConfigSectionName = "Aspire:StackExchange:Redis";

    /// <summary>
    /// Registers <see cref="IConnectionMultiplexer"/> as a singleton in the services provided by the <paramref name="builder"/>.
    /// Enables retries, corresponding health check, logging, and telemetry.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">An optional name used to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="StackExchangeRedisSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureOptions">An optional method that can be used for customizing the <see cref="ConfigurationOptions"/>. It's invoked after the options are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire.StackExchange.Redis" section.</remarks>
    public static void AddRedis(this IHostApplicationBuilder builder, string? connectionName = null, Action<StackExchangeRedisSettings>? configureSettings = null, Action<ConfigurationOptions>? configureOptions = null)
        => AddRedis(builder, DefaultConfigSectionName, configureSettings, configureOptions, connectionName, serviceKey: null);

    /// <summary>
    /// Registers <see cref="IConnectionMultiplexer"/> as a keyed singleton for the given <paramref name="name"/> in the services provided by the <paramref name="builder"/>.
    /// Enables retries, corresponding health check, logging, and telemetry.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="name">The name of the component, which is used as the <see cref="ServiceDescriptor.ServiceKey"/> of the service and also to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="StackExchangeRedisSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureOptions">An optional method that can be used for customizing the <see cref="ConfigurationOptions"/>. It's invoked after the options are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire.StackExchange.Redis:{name}" section.</remarks>
    public static void AddKeyedRedis(this IHostApplicationBuilder builder, string name, Action<StackExchangeRedisSettings>? configureSettings = null, Action<ConfigurationOptions>? configureOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        AddRedis(builder, $"{DefaultConfigSectionName}:{name}", configureSettings, configureOptions, connectionName: name, serviceKey: name);
    }

    private static void AddRedis(IHostApplicationBuilder builder, string configurationSectionName, Action<StackExchangeRedisSettings>? configureSettings, Action<ConfigurationOptions>? configureOptions, string? connectionName, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configSection = builder.Configuration.GetSection(configurationSectionName);

        StackExchangeRedisSettings settings = new();
        configSection.Bind(settings);

        configureSettings?.Invoke(settings);

        // see comments on ConfigurationOptionsFactory for why a factory is used here
        builder.Services.TryAdd(ServiceDescriptor.Transient(typeof(IOptionsFactory<ConfigurationOptions>),
            sp => new ConfigurationOptionsFactory(
                settings,
                connectionName,
                sp.GetRequiredService<IConfiguration>(),
                sp.GetServices<IConfigureOptions<ConfigurationOptions>>(),
                sp.GetServices<IPostConfigureOptions<ConfigurationOptions>>(),
                sp.GetServices<IValidateOptions<ConfigurationOptions>>())));

        string? optionsName = serviceKey is null ? null : connectionName;
        builder.Services.Configure<ConfigurationOptions>(
            optionsName ?? Options.Options.DefaultName,
            configurationOptions =>
            {
                BindToConfiguration(configurationOptions, configSection);

                configureOptions?.Invoke(configurationOptions);
            });

        if (serviceKey is null)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(
                sp => ConnectionMultiplexer.Connect(GetConfigurationOptions(sp, configurationSectionName, optionsName), CreateLogger(sp)));
        }
        else
        {
            builder.Services.AddKeyedSingleton<IConnectionMultiplexer>(serviceKey,
                (sp, key) => ConnectionMultiplexer.Connect(GetConfigurationOptions(sp, configurationSectionName, optionsName), CreateLogger(sp)));
        }

        if (settings.Tracing)
        {
            // Supports distributed tracing
            builder.Services.AddOpenTelemetry()
                .WithTracing(t =>
                {
                    t.AddRedisInstrumentation();
                });
        }

        if (settings.HealthChecks)
        {
            builder.Services.AddHealthChecks()
                .AddRedis(
                    // The connection factory tries to open the connection and throws when it fails.
                    // That is why we don't invoke it here, but capture the state (in a closure)
                    // and let the health check invoke it and handle the exception (if any).
                    connectionMultiplexerFactory: sp => serviceKey is null ? sp.GetRequiredService<IConnectionMultiplexer>() : sp.GetRequiredKeyedService<IConnectionMultiplexer>(serviceKey),
                    name: serviceKey is null ? "StackExchange.Redis" : $"StackExchange.Redis_{connectionName}");
        }

        static TextWriter? CreateLogger(IServiceProvider serviceProvider)
            => serviceProvider.GetService<ILoggerFactory>() is { } loggerFactory
                ? new LoggingTextWriter(loggerFactory.CreateLogger("Aspire.StackExchange.Redis"))
                : null;
    }

    private static ConfigurationOptions GetConfigurationOptions(IServiceProvider serviceProvider, string configurationSectionName, string? optionsName)
    {
        var configurationOptions = optionsName is null ?
            serviceProvider.GetRequiredService<IOptions<ConfigurationOptions>>().Value :
            serviceProvider.GetRequiredService<IOptionsMonitor<ConfigurationOptions>>().Get(optionsName);

        if (configurationOptions is null || configurationOptions.EndPoints.Count == 0)
        {
            throw new InvalidOperationException($"No endpoints specified. Ensure a valid connection string was provided for the '{configurationSectionName}:ConnectionString' configuration key.");
        }

        return configurationOptions;
    }

    private static ConfigurationOptions BindToConfiguration(ConfigurationOptions options, IConfiguration configuration)
    {
        var configurationOptionsSection = configuration.GetSection("ConfigurationOptions");
        configurationOptionsSection.Bind(options);

        return options;
    }

    private sealed class LoggingTextWriter(ILogger logger) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value) => logger.LogTrace(value);
    }

    /// <summary>
    /// ConfigurationOptionsFactory parses a ConfigurationOptions options object from Configuration.
    /// </summary>
    /// <remarks>
    /// Using an OptionsFactory to create the object allows parsing the ConfigurationOptions IOptions object from a connection string.
    /// ConfigurationOptions.Parse(string) returns the ConfigurationOptions and doesn't support parsing to an existing object.
    /// Using a normal Configure callback isn't feasible since that only works with an existing object. Using an OptionsFactory
    /// allows us to create the initial object ourselves.
    ///
    /// This still allows for others to Configure/PostConfigure/Validate the ConfigurationOptions since it just overrides <see cref="CreateInstance(string)"/>.
    /// </remarks>
    private sealed class ConfigurationOptionsFactory : OptionsFactory<ConfigurationOptions>
    {
        private readonly StackExchangeRedisSettings _settings;
        private readonly string? _connectionStringName;
        private readonly IConfiguration _configuration;

        public ConfigurationOptionsFactory(StackExchangeRedisSettings settings, string? connectionStringName, IConfiguration configuration, IEnumerable<IConfigureOptions<ConfigurationOptions>> setups, IEnumerable<IPostConfigureOptions<ConfigurationOptions>> postConfigures, IEnumerable<IValidateOptions<ConfigurationOptions>> validations)
            : base(setups, postConfigures, validations)
        {
            _settings = settings;
            _connectionStringName = connectionStringName;
            _configuration = configuration;
        }

        protected override ConfigurationOptions CreateInstance(string name)
        {
            var connectionString = _settings.ConnectionString;
            if (string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(_connectionStringName))
            {
                connectionString = _configuration.GetConnectionString(_connectionStringName);
            }

            return connectionString is not null ?
                ConfigurationOptions.Parse(connectionString) :
                base.CreateInstance(name);
        }
    }
}