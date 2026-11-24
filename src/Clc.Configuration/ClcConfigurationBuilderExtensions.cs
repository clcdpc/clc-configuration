using Microsoft.Extensions.Hosting;

namespace Clc.Configuration;

/// <summary>Loads application configuration from the CLC ProgramData convention.</summary>
/// <remarks>Startup and bootstrap resolve the optional nested ClcConfiguration.ApplicationDirectoryName
/// from appsettings.json in AppContext.BaseDirectory before accessing the operational configuration.</remarks>
public static class ClcConfigurationBuilderExtensions
{
    /// <summary>Returns the application paths recorded by successful configuration initialization on this builder.</summary>
    /// <param name="builder">The configured application host builder.</param>
    /// <returns>The paths established by the latest successful configuration initialization.</returns>
    /// <remarks>Does not load configuration or access the filesystem.</remarks>
    /// <exception cref="ArgumentNullException">The builder is null.</exception>
    /// <exception cref="InvalidOperationException">Configuration has not successfully initialized this builder.</exception>
    public static ClcApplicationPaths GetClcApplicationPaths(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Properties.TryGetValue(typeof(ClcApplicationPaths), out var value) &&
            value is ClcApplicationPaths paths)
        {
            return paths;
        }

        throw new InvalidOperationException(
            "CLC configuration has not successfully initialized this builder. " +
            "Call AddClcConfiguration or AddClcConfigurationWithBootstrap with the application directory name before retrieving application paths.");
    }

    /// <summary>
    /// Opts into first-run template copying and REPLACE_ME detection before loading configuration.
    /// A newly created file always throws ConfigurationCreated so it can be reviewed before startup.
    /// </summary>
    /// <typeparam name="TBuilder">The concrete application builder type.</typeparam>
    /// <param name="builder">The web or Generic Host application builder.</param>
    /// <param name="applicationDirectoryName">The application's default portable directory name beneath OS CommonApplicationData.</param>
    /// <param name="exampleConfigurationPath">An absolute path or a path relative to ContentRootPath; defaults to config/example.json.</param>
    /// <returns>The same builder, with the external configuration loaded.</returns>
    public static TBuilder AddClcConfigurationWithBootstrap<TBuilder>(
        this TBuilder builder,
        string applicationDirectoryName,
        string? exampleConfigurationPath = null) where TBuilder : IHostApplicationBuilder
    {
        AddClcConfigurationWithBootstrap((IHostApplicationBuilder)builder, applicationDirectoryName, exampleConfigurationPath);
        return builder;
    }

    /// <summary>
    /// Opts a Generic Host or worker into first-run template copying and REPLACE_ME detection.
    /// Existing files are never overwritten. A new file must be reviewed before restarting.
    /// </summary>
    /// <param name="builder">The application host builder, before services read configuration.</param>
    /// <param name="applicationDirectoryName">The application's default portable directory name beneath OS CommonApplicationData.</param>
    /// <param name="exampleConfigurationPath">An absolute path or a path relative to ContentRootPath; defaults to config/example.json.</param>
    /// <returns>The same builder, with the external configuration loaded.</returns>
    public static IHostApplicationBuilder AddClcConfigurationWithBootstrap(
        this IHostApplicationBuilder builder,
        string applicationDirectoryName,
        string? exampleConfigurationPath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var paths = ApplicationDirectoryResolver.Resolve(applicationDirectoryName);
        return AddClcConfigurationWithBootstrap(builder, paths, exampleConfigurationPath);
    }

    /// <summary>
    /// Replaces the builder's application configuration providers with the application's
    /// deployment-specific ProgramData <c>application.json</c> snapshot.
    /// </summary>
    /// <typeparam name="TBuilder">The concrete application builder type.</typeparam>
    /// <param name="builder">The web or Generic Host application builder.</param>
    /// <param name="applicationDirectoryName">The application's default portable directory name beneath OS CommonApplicationData.</param>
    /// <returns>The same builder, with the external configuration loaded.</returns>
    public static TBuilder AddClcConfiguration<TBuilder>(
        this TBuilder builder,
        string applicationDirectoryName) where TBuilder : IHostApplicationBuilder
    {
        AddClcConfiguration((IHostApplicationBuilder)builder, applicationDirectoryName);
        return builder;
    }

    /// <summary>
    /// Replaces a Generic Host or worker builder's application configuration providers with
    /// the same authoritative startup snapshot used by the web integration.
    /// </summary>
    /// <param name="builder">The application host builder, before services read configuration.</param>
    /// <param name="applicationDirectoryName">The application's default portable directory name beneath OS CommonApplicationData.</param>
    /// <returns>The same builder, with the external configuration loaded.</returns>
    public static IHostApplicationBuilder AddClcConfiguration(
        this IHostApplicationBuilder builder,
        string applicationDirectoryName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var paths = ApplicationDirectoryResolver.Resolve(applicationDirectoryName);
        return AddClcConfiguration(builder, paths);
    }

    // Shared initialization paths let tests use resolved temporary paths without a public root override.
    internal static IHostApplicationBuilder AddClcConfiguration(
        IHostApplicationBuilder builder,
        ClcApplicationPaths paths)
    {
        ExternalConfigurationLoader.AddFromFile(builder, paths.ApplicationDirectoryName, paths.ConfigurationFilePath);
        builder.Properties[typeof(ClcApplicationPaths)] = paths;
        return builder;
    }

    internal static IHostApplicationBuilder AddClcConfigurationWithBootstrap(
        IHostApplicationBuilder builder,
        ClcApplicationPaths paths,
        string? exampleConfigurationPath)
    {
        ConfigurationBootstrap.AddFromFile(builder, paths.ApplicationDirectoryName, paths.ConfigurationFilePath, exampleConfigurationPath);
        builder.Properties[typeof(ClcApplicationPaths)] = paths;
        return builder;
    }
}
