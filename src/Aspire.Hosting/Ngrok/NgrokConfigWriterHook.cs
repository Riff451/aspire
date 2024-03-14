// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dcp.Process;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Utils;

namespace Aspire.Hosting.Ngrok;

internal class NgrokConfigWriterHook : IDistributedApplicationLifecycleHook
{
    /// <summary>
    /// TODO
    /// </summary>
    /// <param name="appModel">The application model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AfterEndpointsAllocatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
    {
        if (appModel.Resources.OfType<NgrokResource>().SingleOrDefault() is not { } ngrokResource)
        {
            // No-op if there is no ngrok resource.
            return;
        }

        if (!ngrokResource.TryGetAnnotationsOfType<EndpointReferenceAnnotation>(out var endpointReferences))
        {
            // No-op if there is no reference added to ngrok to work with.
            return;
        }

        var defaultConfigLocation = await GetDefaultConfigLocationAsync(ngrokResource.Command).ConfigureAwait(false);
        var configFilePath = PathNormalizer.NormalizePathForCurrentPlatform(Path.Combine(ngrokResource.WorkingDirectory, "ngrok-aspire.yml"));

        using var stream = new FileStream(configFilePath, FileMode.Create);
        using var writer = new StreamWriter(stream);

        var configFileBuilder = new StringBuilder();

        configFileBuilder.Append("""
    log: stdout
    version: "2"
    tunnels:

    """);

        foreach (var resource in endpointReferences.Select(reference => new
        {
            Name = reference.Resource.Name,
            Endpoints = reference.Resource.GetEndpoints(),
        }))
        {
            configFileBuilder.AppendLine(CultureInfo.InvariantCulture, $"    {resource.Name}:");

            foreach (var endpoint in resource.Endpoints)
            {
                configFileBuilder.Append(CultureInfo.InvariantCulture, $"""
            addr: {endpoint.Port}
            schemes:
                - https
            proto: {endpoint.Scheme}
    """);
            }
        }

        await writer.WriteAsync(configFileBuilder, cancellationToken).ConfigureAwait(false);

        ngrokResource.Annotations.Add(new CommandLineArgsCallbackAnnotation(updatedArgs =>
        {
            updatedArgs.Add("--config");
            if (defaultConfigLocation is not null)
            {
                updatedArgs.Add($"{defaultConfigLocation},{configFilePath}");
            }
            else
            {
                updatedArgs.Add(configFilePath);
            }
            
            updatedArgs.Add("start");
            updatedArgs.Add("--all");
        }));
    }

    private static async Task<string?> GetDefaultConfigLocationAsync(string ngrokCmd)
    {
        var outputStringBuilder = new StringBuilder();

        // run 'ngrok config check'
        var ngrokConfigCheckSpec = new ProcessSpec(ngrokCmd)
        {
            Arguments = "config check",
            OnOutputData = data => outputStringBuilder.AppendLine(data),
        };

        await ExecuteCommandAsync(ngrokConfigCheckSpec).ConfigureAwait(false);

        var output = outputStringBuilder.ToString();

        if (string.IsNullOrWhiteSpace(output)
            || !output.Contains("valid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var startIndex = output.IndexOf("at ", StringComparison.OrdinalIgnoreCase) + 2;

        return PathNormalizer.NormalizePathForCurrentPlatform(output[startIndex..].Trim(' ', '\r', '\n'));
    }

    private static async Task<ProcessResult?> ExecuteCommandAsync(ProcessSpec processSpec)
    {
        var (task, disposable) = ProcessUtil.Run(processSpec);

        try
        {
            var result = await task.ConfigureAwait(false);

            return result;
        }
        finally
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
