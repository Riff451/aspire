// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Sockets;
using Aspire.Hosting.Ngrok;
using Aspire.Hosting.Publishing;
using Aspire.Hosting.Tests.Helpers;
using Aspire.Hosting.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspire.Hosting.Tests.Ngrok;

public class AddNgrokTests
{
    [Fact]
    public async Task AddNgrokContainerWithDefaultsAddsAnnotationMetadata()
    {
        var appBuilder = DistributedApplication.CreateBuilder();

        appBuilder.AddNgrok("ngrok-tunnels", "testauthtoken", 1234, "eu");

        using var app = appBuilder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var containerResource = Assert.Single(appModel.Resources.OfType<NgrokResource>());
        Assert.Equal("ngrok-tunnels", containerResource.Name);

        var manifestAnnotation = Assert.Single(containerResource.Annotations.OfType<ManifestPublishingCallbackAnnotation>());
        Assert.Null(manifestAnnotation.Callback);

        var endpoint = Assert.Single(containerResource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(4040, endpoint.ContainerPort);
        Assert.False(endpoint.IsExternal);
        Assert.Equal("ngrok-inspection-ui", endpoint.Name);
        Assert.Equal(1234, endpoint.Port);
        Assert.Equal(ProtocolType.Tcp, endpoint.Protocol);
        Assert.Equal("http", endpoint.Transport);
        Assert.Equal("http", endpoint.UriScheme);

        var containerAnnotation = Assert.Single(containerResource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(NgrokContainerImageTags.Tag, containerAnnotation.Tag);
        Assert.Equal(NgrokContainerImageTags.Image, containerAnnotation.Image);
        Assert.Null(containerAnnotation.Registry);

        var config = await EnvironmentVariableEvaluator.GetEnvironmentVariablesAsync(containerResource);
        Assert.Collection(config,
            env =>
            {
                Assert.Equal("NGROK_AUTHTOKEN", env.Key);
                Assert.Equal("testauthtoken", env.Value);
            });

        var volume = containerResource.Annotations.OfType<ContainerMountAnnotation>().Single();
        Assert.True(File.Exists(volume.Source)); // File should exist, but will be empty.
        Assert.Equal(NgrokResource.ConfigFileContainerPath, volume.Target);

        var argsAnnotation = Assert.Single(containerResource.Annotations.OfType<CommandLineArgsCallbackAnnotation>());
        Assert.NotNull(argsAnnotation.Callback);
        var args = new List<object>();
        await argsAnnotation.Callback(new CommandLineArgsCallbackContext(args));
        Assert.Equal($"start --all --config {NgrokResource.ConfigFileContainerPath}".Split(' '), args);
    }

    [Fact]
    public async Task WithNgrokTunnelProducesValidNgrokConfigFile()
    {
        var builder = CreateBuilder();

        var ngrokBuilder = builder.AddNgrok("ngrok-tunnels", "testauthtoken", 1234, "eu");

        var projectA = builder.AddProject<TestProject>("testProjectA", launchProfileName: null)
            .WithHttpEndpoint(1235)
            .WithNgrokTunnel(ngrokBuilder);
        var projectB = builder.AddProject<TestProject>("testProjectB", launchProfileName: null)
            .WithHttpEndpoint(1236)
            .WithNgrokTunnel(ngrokBuilder);

        // Add fake allocated endpoints.
        projectA.WithEndpoint("http", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 5001));
        projectB.WithEndpoint("http", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 5002));

        var ngrok = builder.Resources.OfType<NgrokResource>().Single();
        Assert.True(ngrok.Tunnels.Count == 2);

        var volume = ngrok.Annotations.OfType<ContainerMountAnnotation>().Single();

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var hook = new NgrokConfigWriterHook(builder.Configuration);
        await hook.AfterEndpointsAllocatedAsync(appModel, CancellationToken.None);

        using var stream = File.OpenRead(volume.Source!);
        var fileContents = new StreamReader(stream).ReadToEnd();

        var expectedConfig =
            """
            log: stdout
            version: "2"
            region: eu
            tunnels:
              testProjectA:
                addr: host.docker.internal:5001
                schemes:
                  - https
                proto: http
              testProjectB:
                addr: host.docker.internal:5002
                schemes:
                  - https
                proto: http

            """;
        Assert.Equal(expectedConfig, fileContents);
    }

    [Fact]
    public async Task WithNgrokTunnelProducesEmptyNgrokConfigFileWhenNoEndpointsFound()
    {
        var builder = CreateBuilder();

        var ngrokBuilder = builder.AddNgrok("ngrok-tunnels", "testauthtoken", 1234, "eu");

        var projectA = builder.AddProject<TestProject>("testProjectA", launchProfileName: null)
            .WithNgrokTunnel(ngrokBuilder);
        var projectB = builder.AddProject<TestProject>("testProjectB", launchProfileName: null)
            .WithNgrokTunnel(ngrokBuilder);

        // Add fake allocated endpoints.
        projectA.WithEndpoint("http", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 5001));
        projectB.WithEndpoint("http", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 5002));

        var ngrok = builder.Resources.OfType<NgrokResource>().Single();
        Assert.Empty(ngrok.Tunnels);

        var volume = ngrok.Annotations.OfType<ContainerMountAnnotation>().Single();

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var hook = new NgrokConfigWriterHook(builder.Configuration);
        await hook.AfterEndpointsAllocatedAsync(appModel, CancellationToken.None);

        using var stream = File.OpenRead(volume.Source!);
        var fileContents = new StreamReader(stream).ReadToEnd();

        Assert.Empty(fileContents);
    }

    [Fact]
    public async Task WithNgrokTunnelProducesEmptyNgrokConfigFileWhenNoAllocatedEndpointsFound()
    {
        var builder = CreateBuilder();

        var ngrokBuilder = builder.AddNgrok("ngrok-tunnels", "testauthtoken", 1234, "eu");

        builder.AddProject<TestProject>("testProjectA", launchProfileName: null)
            .WithHttpEndpoint(1235)
            .WithNgrokTunnel(ngrokBuilder);
        builder.AddProject<TestProject>("testProjectB", launchProfileName: null)
            .WithHttpEndpoint(1236)
            .WithNgrokTunnel(ngrokBuilder);

        var ngrok = builder.Resources.OfType<NgrokResource>().Single();
        Assert.True(ngrok.Tunnels.Count == 2);

        var volume = ngrok.Annotations.OfType<ContainerMountAnnotation>().Single();

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var hook = new NgrokConfigWriterHook(builder.Configuration);
        await hook.AfterEndpointsAllocatedAsync(appModel, CancellationToken.None);

        using var stream = File.OpenRead(volume.Source!);
        var fileContents = new StreamReader(stream).ReadToEnd();

        Assert.Empty(fileContents);
    }

    [Fact]
    public void AddNgrokContainerCanOnlyBeCalledOnce()
    {
        var appBuilder = DistributedApplication.CreateBuilder();

        appBuilder.AddNgrok("ngrok-tunnels-a", "testauthtoken");

        Assert.Throws<DistributedApplicationException>(() => appBuilder.AddNgrok("ngrok-tunnels-b", "testauthtoken"));
    }

    private static IDistributedApplicationBuilder CreateBuilder(DistributedApplicationOperation operation = DistributedApplicationOperation.Publish)
    {
        var args = operation == DistributedApplicationOperation.Publish ? new[] { "--publisher", "manifest" } : Array.Empty<string>();
        var appBuilder = DistributedApplication.CreateBuilder(args);
        // Block DCP from actually starting anything up as we don't need it for this test.
        appBuilder.Services.AddKeyedSingleton<IDistributedApplicationPublisher, NoopPublisher>("manifest");

        return appBuilder;
    }

    private sealed class TestProject : IProjectMetadata
    {
        public string ProjectPath => "another-path";

        public LaunchSettings? LaunchSettings { get; set; }
    }
}
