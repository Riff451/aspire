// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Configuration;

namespace Aspire.Hosting.Ngrok;

internal sealed class NgrokConfigWriterHook : IDistributedApplicationLifecycleHook
{
    private readonly IConfiguration _configuration;

    public NgrokConfigWriterHook(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Retrieves all the endpoint references of the ngrok resource and
    /// it configures a tunnel for each.
    /// Since the only way to run multiple tunnels with ngrok is by using
    /// the config file(s), this method writes a config file in the ngrok resource container mount.
    /// </summary>
    /// <param name="appModel">The application model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task AfterEndpointsAllocatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
    {
        if (appModel.Resources.OfType<NgrokResource>().SingleOrDefault() is not { } ngrokResource)
        {
            // No-op if there is no ngrok resource.
            return Task.CompletedTask;
        }

        if (!ngrokResource.Tunnels.Any(endpoint => endpoint.IsAllocated))
        {
            // No-op if there is no tunnel added to ngrok to work with.
            return Task.CompletedTask;
        }

        var containerFileMount = ngrokResource.Annotations.OfType<ContainerMountAnnotation>().Single(v => v.Target == NgrokResource.ConfigFileContainerPath);

        using var stream = new FileStream(containerFileMount.Source!, FileMode.Create);
        using var writer = new StreamWriter(stream);

        var configFileBuilder = new StringBuilder();

        // it's a YAML file so indentation matters
        configFileBuilder.Append(
            """
            log: stdout
            version: "2"

            """);

        if (!string.IsNullOrWhiteSpace(ngrokResource.Region))
        {
            configFileBuilder.Append(CultureInfo.InvariantCulture,
            $"""
            region: {ngrokResource.Region}

            """);
        }

        configFileBuilder.Append(CultureInfo.InvariantCulture,
            $"""
            tunnels:

            """);

        foreach (var endpointGrp in ngrokResource.Tunnels
                                                    .Where(endpoint => endpoint.IsAllocated)
                                                    .GroupBy(endpoint => endpoint.Resource.Name))
        {
            configFileBuilder.Append(CultureInfo.InvariantCulture,
            $"""
              {endpointGrp.Key}:

            """);

            foreach (var endpoint in endpointGrp)
            {
                configFileBuilder.Append(
            CultureInfo.InvariantCulture,
            $"""
                addr: {ReplaceLocalhostWithContainerHost("localhost", _configuration)}:{endpoint.Port}
                schemes:
                  - https
                proto: {endpoint.Scheme}

            """);
            }
        }

        writer.Write(configFileBuilder);

        return Task.CompletedTask;
    }

    private static string ReplaceLocalhostWithContainerHost(string value, IConfiguration configuration)
    {
        // https://stackoverflow.com/a/43541732/45091

        // This configuration value is a workaround for the fact that host.docker.internal is not available on Linux by default.
        var hostName = configuration["AppHost:ContainerHostname"] ?? "host.docker.internal";

        return value.Replace("localhost", hostName, StringComparison.OrdinalIgnoreCase)
                    .Replace("127.0.0.1", hostName)
                    .Replace("[::1]", hostName);
    }
}
