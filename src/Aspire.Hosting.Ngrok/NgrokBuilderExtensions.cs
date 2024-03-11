// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Ngrok;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding ngrok resources to the application model.
/// </summary>
public static class NgrokBuilderExtensions
{
    /// <summary>
    /// Add an ngrok container resource to the application model.
    /// It uses the version 3 of the ngrok image.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/> to add the resource to.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="authToken">The ngrok authentication token identifying a user.</param>
    /// <param name="hostPort">The host port for the ngrok inspection ui.</param>
    /// <param name="region">The ngrok server region. If not passed it defaults to closest.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<NgrokResource> AddNgrok(this IDistributedApplicationBuilder builder, string name, string authToken, int? hostPort = null, string? region = null)
    {
        if (builder.Resources.OfType<NgrokResource>().Any())
        {
            throw new DistributedApplicationException($"Cannot add resource of type '{typeof(NgrokResource)}' because a resource of type '{typeof(NgrokResource)}' already exists.");
        }

        builder.Services.TryAddLifecycleHook<NgrokConfigWriterHook>();

        var resource = new NgrokResource(name, authToken, region);

        return builder.AddResource(resource)
                      .WithImage(NgrokContainerImageTags.Image)
                      .WithImageTag(NgrokContainerImageTags.Tag)
                      .WithArgs("start", "--all", "--config", NgrokResource.ConfigFileContainerPath)
                      .WithEnvironment("NGROK_AUTHTOKEN", authToken)
                      .WithHttpEndpoint(containerPort: 4040, hostPort: hostPort, name: "ngrok-inspection-ui")
                      .WithBindMount(Path.GetTempFileName(), NgrokResource.ConfigFileContainerPath)
                      .ExcludeFromManifest();
    }

    /// <summary>
    /// Add an ngrok tunnel to a resource.
    /// </summary>
    /// <param name="builder">The builder of the resource.</param>
    /// <param name="ngrokBuilder">The builder of the ngrok resource.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/></returns>
    public static IResourceBuilder<T> WithNgrokTunnel<T>(this IResourceBuilder<T> builder, IResourceBuilder<NgrokResource> ngrokBuilder)
        where T : IResourceWithEndpoints
    {
        var resource = builder.Resource;

        if (resource.TryGetEndpoints(out var endpoints))
        {
            foreach (var endpoint in endpoints)
            {
                ngrokBuilder.Resource.Tunnels.Add(new EndpointReference(resource, endpoint));
            }
        }

        return builder;
    }
}
