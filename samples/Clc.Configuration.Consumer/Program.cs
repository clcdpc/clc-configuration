using System;
using System.Threading.Tasks;
using Clc.Configuration;
using Microsoft.AspNetCore.Builder;

public static class Program
{
    // Deployments can override this default in appsettings.json beside the published DLL.
    private const string ApplicationDirectoryName = "clc-configuration-consumer";

    public static async Task<int> Main(string[] args)
    {
        // Opt in before logging, host construction, or operational configuration reads.
        if (ClcConfigurationCommandLine.TryHandle(args, ApplicationDirectoryName, out var exitCode))
        {
            return exitCode;
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.AddClcConfiguration(ApplicationDirectoryName);

        await using var app = builder.Build();
        Console.WriteLine($"Loaded external configuration from {builder.GetClcApplicationPaths().ConfigurationFilePath}.");
        return 0;
    }
}
