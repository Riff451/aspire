// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

var ngrok = builder.AddNgrok("ngrok-tunnels", builder.Configuration["Ngrok:AuthToken"]!, hostPort: null, region: "eu");

var apiA = builder.AddProject<Projects.NgRokEndToEnd_ApiServiceA>("test-api-a")
    .WithNgrokTunnel(ngrok);
builder.AddProject<Projects.NgRokEndToEnd_ApiServiceB>("test-api-b")
    .WithReference(apiA)
    .WithNgrokTunnel(ngrok);

// This project is only added in playground projects to support development/debugging
// of the dashboard. It is not required in end developer code. Comment out this code
// to test end developer dashboard launch experience. Refer to Directory.Build.props
// for the path to the dashboard binary (defaults to the Aspire.Dashboard bin output
// in the artifacts dir).
builder.AddProject<Projects.Aspire_Dashboard>(KnownResourceNames.AspireDashboard);

builder.Build().Run();