// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents an ngrok executable.
/// </summary>
/// <param name="name">The name of the resource.</param>
/// <param name="command">The command to execute.</param>
/// <param name="workingDirectory">The working directory to use for the command.</param>
/// <param name="args">The arguments to pass to the command.</param>
public class NgrokResource(string name, string command, string workingDirectory, string[]? args)
    : ExecutableResource(name, command, workingDirectory, args)
{

}
