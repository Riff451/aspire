// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dcp.Process;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Utils;

namespace Aspire.Hosting.Ngrok;

internal class NgrokConfigWriterHook : IDistributedApplicationLifecycleHook
{
    public async Task AfterEndpointsAllocatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken)
    {
        if (appModel.Resources.OfType<NgrokResource>().SingleOrDefault() is not { } ngrokResource)
        {
            // No-op if there is no ngrok resource (removed after hook added).
            return;
        }

        var defaultConfigLocation = await GetDefaultConfigLocationAsync().ConfigureAwait(false);

        var configFilePath = PathNormalizer.NormalizePathForCurrentPlatform(Path.Combine(ngrokResource.WorkingDirectory, "ngrok.yml"));

        using var stream = new FileStream(configFilePath, FileMode.Create);
        using var writer = new StreamWriter(stream);

        await writer.WriteAsync("""
    version: "2"
    tunnels:
        website:
            addr: 8888
            schemes:
                - https
            proto: http
    """).ConfigureAwait(false);

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

    private static async Task<string?> GetDefaultConfigLocationAsync()
    {
        var outputStringBuilder = new StringBuilder();

        // run 'ngrok config check'
        var ngrokConfigCheckSpec = new ProcessSpec("ngrok")
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
