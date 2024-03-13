// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Ngrok;
using Aspire.Hosting.Utils;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding ngrok resources to the application model.
/// </summary>
public static class NgrokBuilderExtensions
{
    /// <summary>
    /// Add an ngrok executable resource to the application model.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/> to add the resource to.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="workingDirectory">The working directory to use for the command. If null, the working directory of the current process is used.</param>
    /// <param name="args">The arguments to pass to the command.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<NgrokResource> AddNgrok(this IDistributedApplicationBuilder builder, string name, string? workingDirectory = null, string[]? args = null)
    {
        args ??= [];
        workingDirectory ??= string.Empty;
        workingDirectory = PathNormalizer.NormalizePathForCurrentPlatform(Path.Combine(builder.AppHostDirectory, workingDirectory));

        builder.Services.TryAddLifecycleHook<NgrokConfigWriterHook>();

        var resource = new NgrokResource(name, "ngrok", workingDirectory, args);

        return builder.AddResource(resource)
                      .WithEndpoint(hostPort: 4040, scheme: "http", name: "ngrok-inspection-interface", env: null, isProxied: false);
    }
}
