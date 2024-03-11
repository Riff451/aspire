// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents an ngrok executable.
/// </summary>
/// <param name="name">The name of the resource.</param>
/// <param name="authtoken">The ngrok authtoken identifying a user.</param>
/// <param name="region">The ngrok server region.</param>
public class NgrokResource(string name, string authtoken, string? region = null)
    : ContainerResource(name)
{
    internal const string ConfigFileContainerPath = "/etc/ngrok.yml";

    /// <summary>
    /// Gets the ngrok authtoken identifying a user.
    /// </summary>
    public string AuthToken => authtoken;

    /// <summary>
    /// Gets the ngrok server region.
    /// </summary>
    public string? Region => region;

    /// <summary>
    /// Gets the ngrok tunnels as a collection of endpoint references.
    /// </summary>
    public ICollection<EndpointReference> Tunnels { get; } = [];
}
