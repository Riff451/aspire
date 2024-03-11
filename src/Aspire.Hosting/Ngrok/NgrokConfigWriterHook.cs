// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dcp.Process;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.Logging;

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

        var configFilePath = PathNormalizer.NormalizePathForCurrentPlatform(Path.Combine(ngrokResource.WorkingDirectory, "ngrok.yml"));

        using var stream = new FileStream(configFilePath, FileMode.Create);
        using var writer = new StreamWriter(stream);

        await writer.WriteAsync("""
    tunnels:
        website:
            addr: 8888
            schemes:
                -https
            proto: http
    """).ConfigureAwait(false);
    }

    private static async Task<string> GetDefaultConfigLocationAsync()
    {
        var ngrokConfigCheckSpec = new ProcessSpec()
        {
            Arguments = "config check",
            OnOutputData = data => armTemplateContents.AppendLine(data),
            OnErrorData = data => resourceLogger.Log(LogLevel.Error, 0, data, null, (s, e) => s),
        };
    }

    private static async Task<ProcessResult?> ExecuteCommandAsync(ProcessSpec processSpec)
    {
        var sw = Stopwatch.StartNew();
        var (task, disposable) = ProcessUtil.Run(processSpec);

        try
        {
            var result = await task.ConfigureAwait(false);
            sw.Stop();

            return result;
        }
        finally
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
