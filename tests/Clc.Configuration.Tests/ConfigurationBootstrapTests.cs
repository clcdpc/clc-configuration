using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Clc.Configuration.Tests;

public sealed class ConfigurationBootstrapTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuccessfulInitializationRecordsPathsForOnlyThatBuilder(bool web, bool bootstrap)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration("{\"Value\":\"configured\"}");
        var builder = fixture.CreateBuilder(web);
        var otherBuilder = fixture.CreateBuilder(web);

        var paths = fixture.Load(builder, bootstrap: bootstrap);

        Assert.Same(paths, builder.GetClcApplicationPaths());
        Assert.Same(paths, builder.Properties[typeof(ClcApplicationPaths)]);
        Assert.Equal(fixture.ConfigurationPath, paths.ConfigurationFilePath);
        Assert.Equal(Path.GetDirectoryName(fixture.ConfigurationPath), paths.ConfigurationDirectory);
        Assert.Equal(Path.GetDirectoryName(paths.ConfigurationDirectory), paths.ApplicationDataDirectory);
        Assert.Equal("configured", builder.Configuration["Value"]);
        Assert.Throws<InvalidOperationException>(() => otherBuilder.GetClcApplicationPaths());

        const string otherName = "clc-other-test";
        fixture.WriteConfiguration("{}", otherName);
        var otherPaths = fixture.Load(otherBuilder, bootstrap: bootstrap, applicationDirectoryName: otherName);
        Assert.Same(otherPaths, otherBuilder.GetClcApplicationPaths());
        Assert.NotEqual(paths.ApplicationDataDirectory, otherPaths.ApplicationDataDirectory);
        Assert.Same(paths, builder.GetClcApplicationPaths());

        // Retrieval uses the recorded object even when the configuration no longer exists.
        File.Delete(fixture.ConfigurationPath);
        Assert.Same(paths, builder.GetClcApplicationPaths());
        Assert.False(File.Exists(fixture.ConfigurationPath));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedInitializationDoesNotRecordPaths(bool web, bool bootstrap)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration("not json");
        var builder = fixture.CreateBuilder(web);

        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder, bootstrap: bootstrap));

        Assert.Equal(ClcConfigurationErrorReason.MalformedJson, error.Reason);
        Assert.False(builder.Properties.ContainsKey(typeof(ClcApplicationPaths)));
        Assert.Throws<InvalidOperationException>(() => builder.GetClcApplicationPaths());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlySuccessfulReinitializationUpdatesTheRecordedPaths(bool web, bool bootstrap)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration("{}");
        var builder = fixture.CreateBuilder(web);
        var first = fixture.Load(builder, bootstrap: !bootstrap);
        const string nextName = "clc-next-test";
        fixture.WriteConfiguration("{\"Value\":\"latest\"}", nextName);

        var latest = fixture.Load(builder, bootstrap: bootstrap, applicationDirectoryName: nextName);

        Assert.NotEqual(first.ApplicationDataDirectory, latest.ApplicationDataDirectory);
        Assert.Same(latest, builder.GetClcApplicationPaths());
        Assert.Equal("latest", builder.Configuration["Value"]);

        fixture.WriteConfiguration("not json");
        Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder, bootstrap: bootstrap));
        Assert.Same(latest, builder.GetClcApplicationPaths());
        Assert.Equal("latest", builder.Configuration["Value"]);

        // The public entry point also preserves the previous registration on validation failure.
        Assert.Throws<ArgumentException>(() =>
        {
            if (bootstrap) { builder.AddClcConfigurationWithBootstrap("../invalid"); }
            else { builder.AddClcConfiguration("../invalid"); }
        });
        Assert.Same(latest, builder.GetClcApplicationPaths());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BootstrapCreationPreservesPreviouslyConfiguredPaths(bool web)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration("{}");
        fixture.WriteExample("{}");
        var builder = fixture.CreateBuilder(web);
        var paths = fixture.Load(builder);

        var error = Assert.Throws<ClcConfigurationException>(() =>
            fixture.Load(builder, applicationDirectoryName: "clc-new-bootstrap-test"));

        Assert.Equal(ClcConfigurationErrorReason.ConfigurationCreated, error.Reason);
        Assert.Same(paths, builder.GetClcApplicationPaths());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstRunCreatesTheConventionalFileThenStopsUntilItIsEdited(bool web)
    {
        using var fixture = new BootstrapFixture();
        const string example = "{\"Token\":\"SECRET_SENTINEL_REPLACE_ME\"}";
        fixture.WriteExample(example);
        var builder = fixture.CreateBuilder(web);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Before"] = "retained" });
        var sources = builder.Configuration.Sources.ToArray();

        var created = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder));

        Assert.Equal(ClcConfigurationErrorReason.ConfigurationCreated, created.Reason);
        Assert.Equal(fixture.ConfigurationPath, created.FilePath);
        Assert.Contains("Startup has stopped", created.Message);
        Assert.Equal(example, File.ReadAllText(fixture.ConfigurationPath));
        Assert.Equal(example, File.ReadAllText(fixture.ExamplePath));
        Assert.Equal(sources, builder.Configuration.Sources);
        Assert.False(builder.Properties.ContainsKey(typeof(ClcApplicationPaths)));
        Assert.Throws<InvalidOperationException>(() => builder.GetClcApplicationPaths());
        Assert.Null(created.InnerException);
        Assert.DoesNotContain("SECRET_SENTINEL", created.ToString());

        var unfinished = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder));
        Assert.Equal(ClcConfigurationErrorReason.UnreplacedPlaceholder, unfinished.Reason);
        Assert.DoesNotContain("SECRET_SENTINEL", unfinished.ToString());
        Assert.Null(unfinished.InnerException);
        Assert.Equal(sources, builder.Configuration.Sources);
        Assert.Throws<InvalidOperationException>(() => builder.GetClcApplicationPaths());

        File.WriteAllText(fixture.ConfigurationPath, "{\"Token\":\"edited\"}");
        var paths = fixture.Load(builder);
        Assert.Same(paths, builder.GetClcApplicationPaths());
        Assert.Equal("edited", builder.Configuration["Token"]);
        Assert.Null(builder.Configuration["Before"]);
    }

    [Fact]
    public void FirstRunStopsEvenWhenExampleHasNoPlaceholders()
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteExample("{}");
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(ClcConfigurationErrorReason.ConfigurationCreated, error.Reason);
        Assert.Equal("{}", File.ReadAllText(fixture.ConfigurationPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitExampleCanBeAbsoluteOrRelativeToContentRoot(bool absolute)
    {
        using var fixture = new BootstrapFixture();
        var relative = Path.Combine("templates", "custom.json");
        var path = Path.Combine(fixture.ContentRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"Source\":\"custom\"}");

        var error = Assert.Throws<ClcConfigurationException>(() =>
            fixture.Load(fixture.CreateBuilder(), absolute ? path : relative));

        Assert.Equal(ClcConfigurationErrorReason.ConfigurationCreated, error.Reason);
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(fixture.ConfigurationPath));
    }

    [Fact]
    public void WindowsDriveRelativePathsCannotDependOnTheProcessDirectory()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var fixture = new BootstrapFixture();
        foreach (var path in new[] { "C:example.json", @"\templates\example.json" })
        {
            Assert.Throws<ArgumentException>(() => fixture.Load(fixture.CreateBuilder(), path));
        }
        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.ConfigurationPath)));
    }

    [Fact]
    public void MissingExampleFailsBeforeCreatingDestinationDirectories()
    {
        using var fixture = new BootstrapFixture();
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(ClcConfigurationErrorReason.ExampleFileMissing, error.Reason);
        Assert.Equal(fixture.ExamplePath, error.FilePath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.ConfigurationPath)));
    }

    [Fact]
    public void DirectoryInsteadOfExampleIsReportedAsUnreadable()
    {
        using var fixture = new BootstrapFixture();
        Directory.CreateDirectory(fixture.ExamplePath);
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(ClcConfigurationErrorReason.ExampleFileUnreadable, error.Reason);
        Assert.False(File.Exists(fixture.ConfigurationPath));
    }

    [Fact]
    public void DestinationFailureIsRedactedAndDoesNotRemoveExistingData()
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteExample("{\"Token\":\"SECRET_SENTINEL\"}");
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ConfigurationPath)!);
        Directory.CreateDirectory(fixture.ConfigurationPath);
        var sentinelPath = Path.Combine(fixture.ConfigurationPath, "keep.txt");
        File.WriteAllText(sentinelPath, "keep");

        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));

        Assert.Equal(ClcConfigurationErrorReason.BootstrapFailed, error.Reason);
        Assert.Equal(fixture.ConfigurationPath, error.FilePath);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("SECRET_SENTINEL", error.ToString());
        Assert.Equal("keep", File.ReadAllText(sentinelPath));
    }

    [Fact]
    public void ExistingConfigurationIsNotOverwrittenOrDependentOnAnExample()
    {
        using var fixture = new BootstrapFixture();
        const string existing = "{\"Value\":\"existing\",\"urls\":\"http://external.test\"}";
        fixture.WriteConfiguration(existing);
        var builder = fixture.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Value"] = "ambient", ["AmbientOnly"] = "removed", ["http_ports"] = "8080"
        });
        // No example exists. Even an unusable source option is irrelevant to an existing file.
        fixture.Load(builder, " ");
        Assert.Equal("existing", builder.Configuration["Value"]);
        Assert.Null(builder.Configuration["AmbientOnly"]);
        Assert.Equal("8080", builder.Configuration["http_ports"]);
        Assert.Equal("http://external.test", builder.Configuration["urls"]);
        Assert.Equal(existing, File.ReadAllText(fixture.ConfigurationPath));
        fixture.WriteExample("{\"Value\":\"replacement\"}");
        fixture.Load(builder);
        Assert.Equal(existing, File.ReadAllText(fixture.ConfigurationPath));
        File.WriteAllText(fixture.ConfigurationPath, "{\"Value\":\"later\"}");
        Assert.Equal("existing", builder.Configuration["Value"]);
    }

    [Theory]
    [InlineData("{\"Value\":\"REPLACE_ME\"}")]
    [InlineData("{\"Connection\":\"Server=private;Password=REPLACE_ME;\"}")]
    [InlineData("{\"Values\":[\"REPLACE_ME\"]}")]
    [InlineData("{\"REPLACE_ME\":\"value\"}")]
    [InlineData("{ /* REPLACE_ME */ \"Value\":\"value\"}")]
    public void LiteralMarkerAnywhereInExistingTextStopsOnlyTheOptedInHelper(string json)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration(json);
        var builder = fixture.CreateBuilder();
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder));
        Assert.Equal(ClcConfigurationErrorReason.UnreplacedPlaceholder, error.Reason);
        Assert.Equal(json, File.ReadAllText(fixture.ConfigurationPath));
        Assert.DoesNotContain("Server=private", error.ToString());
        // Preserve the existing normal-startup and hostless-reader contract.
        ExternalConfigurationLoader.AddFromFile(builder, BootstrapFixture.AppName, fixture.ConfigurationPath);
        ClcConfigurationReader.LoadFromFile(BootstrapFixture.AppName, fixture.ConfigurationPath);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf32")]
    public void MarkerCheckingUsesTheSameDecodedSnapshotForSupportedEncodings(string name)
    {
        using var fixture = new BootstrapFixture();
        var encoding = name switch { "utf16" => Encoding.Unicode, "utf32" => Encoding.UTF32, _ => Encoding.UTF8 };
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ConfigurationPath)!);
        File.WriteAllText(fixture.ConfigurationPath, "{\"Token\":\"REPLACE_ME\"}", encoding);
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(ClcConfigurationErrorReason.UnreplacedPlaceholder, error.Reason);
    }

    [Theory]
    [InlineData("replace_me")]
    [InlineData("Replace_Me")]
    [InlineData("REPLACE\\u005fME")]
    public void MarkerIsLiteralAndCaseSensitive(string value)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration("{\"Value\":\"" + value + "\"}");
        fixture.Load(fixture.CreateBuilder());
    }

    [Fact]
    public void InvalidExistingJsonStillFailsWithoutReplacingIt()
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration("not json");
        fixture.WriteExample("{}");
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(ClcConfigurationErrorReason.MalformedJson, error.Reason);
        Assert.Equal("not json", File.ReadAllText(fixture.ConfigurationPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicBootstrapOverloadsUseContentRootAndDoNotWriteWhenExampleIsAbsent(bool web)
    {
        using var fixture = new BootstrapFixture();
        var name = $"clc-bootstrap-public-{Guid.NewGuid():N}";
        var builder = fixture.CreateBuilder(web);
        var error = Assert.Throws<ClcConfigurationException>(() =>
        {
            if (builder is WebApplicationBuilder webBuilder) { webBuilder.AddClcConfigurationWithBootstrap(name); }
            else { builder.AddClcConfigurationWithBootstrap(name); }
        });
        Assert.Equal(ClcConfigurationErrorReason.ExampleFileMissing, error.Reason);
        Assert.Equal(fixture.ExamplePath, error.FilePath);
        Assert.False(Directory.Exists(ClcApplicationPaths.Resolve(name).ApplicationDataDirectory));
        Assert.False(builder.Properties.ContainsKey(typeof(ClcApplicationPaths)));
        Assert.Throws<InvalidOperationException>(() => builder.GetClcApplicationPaths());
    }

    [Fact]
    public void PublicOverloadsRejectNullBuilders()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ClcConfigurationBuilderExtensions.AddClcConfigurationWithBootstrap((WebApplicationBuilder)null!, "clc-test"));
        Assert.Throws<ArgumentNullException>(() =>
            ClcConfigurationBuilderExtensions.AddClcConfigurationWithBootstrap((IHostApplicationBuilder)null!, "clc-test"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Other":{"ApplicationDirectoryName":"elsewhere"},"ClcConfiguration:ApplicationDirectoryName":"elsewhere"}""")]
    [InlineData("""{"ClcConfiguration":{}}""")]
    public void AbsentNestedPropertyUsesTheDefault(string json)
    {
        using var fixture = new BootstrapFixture();
        File.WriteAllText(fixture.SettingsPath, json);
        fixture.WriteConfiguration("{}");
        Assert.Equal(fixture.ConfigurationPath, fixture.Load(fixture.CreateBuilder(), bootstrap: false).ConfigurationFilePath);
    }

    [Fact]
    public void IndependentInitializationsOfTheSameDefaultKeepTheirOwnIdentity()
    {
        using var fixture = new BootstrapFixture();
        var original = fixture.CreateBuilder();
        var preview = fixture.CreateBuilder();
        fixture.WriteConfiguration("""{"Value":"original"}""");
        fixture.WriteConfiguration("""{"Value":"preview"}""", "preview");
        var first = fixture.Load(original, bootstrap: false);
        File.WriteAllText(fixture.SettingsPath, """{"ClcConfiguration":{"ApplicationDirectoryName":"preview"}}""");
        var second = fixture.Load(preview, bootstrap: false);
        File.Delete(fixture.SettingsPath);
        Assert.Equal(fixture.ConfigurationPath, first.ConfigurationFilePath);
        Assert.Equal(fixture.ResolvePaths("preview").ConfigurationFilePath, second.ConfigurationFilePath);
        Assert.Equal("original", original.Configuration["Value"]);
        Assert.Equal("preview", preview.Configuration["Value"]);
        Assert.Same(first, original.GetClcApplicationPaths());
        Assert.Same(second, preview.GetClcApplicationPaths());
        Assert.Equal(first.ConfigurationFilePath, fixture.Load(preview, bootstrap: false).ConfigurationFilePath);
        Assert.Equal("original", preview.Configuration["Value"]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"  \"")]
    [InlineData("42")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"../SECRET\"")]
    [InlineData("\"C:\\\\SECRET\"")]
    [InlineData("\"CON.txt\"")]
    [InlineData("\"SECRET \"")]
    [InlineData("\"SECRET\\uDC00\"")]
    public void InvalidOverrideCannotFallBackToPreviouslyLoadedDefault(string value)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteConfiguration("""{"Value":"original"}""");
        var builder = fixture.CreateBuilder();
        var previous = fixture.Load(builder);
        File.WriteAllText(fixture.SettingsPath, """{"ClcConfiguration":{"ApplicationDirectoryName":""" + value + "}}");
        foreach (var bootstrap in new[] { true, false })
        {
            var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder, bootstrap: bootstrap));
            Assert.Equal(ClcConfigurationErrorReason.InvalidApplicationDirectoryOverride, error.Reason);
            Assert.Equal(fixture.SettingsPath, error.FilePath);
            Assert.Contains("ClcConfiguration:ApplicationDirectoryName", error.Message);
            Assert.DoesNotContain("SECRET", error.ToString());
            Assert.Null(error.InnerException);
            Assert.Same(previous, builder.GetClcApplicationPaths());
            Assert.Equal("original", builder.Configuration["Value"]);
        }
    }

    [Theory]
    [InlineData("""{"ClcConfiguration":null}""")]
    [InlineData("""{"ClcConfiguration":"SECRET"}""")]
    [InlineData("""{"ClcConfiguration":[]}""")]
    [InlineData("""{"ClcConfiguration":{},"clcConfiguration":{}}""")]
    [InlineData("""{"ClcConfiguration":{"ApplicationDirectoryName":"SECRET","applicationDirectoryName":"SECRET"}}""")]
    [InlineData("""{"SECRET":""", ClcConfigurationErrorReason.MalformedJson)]
    [InlineData("[]", ClcConfigurationErrorReason.InvalidRootShape)]
    public void InvalidSettingsStopBootstrapBeforeCreatingFiles(string json,
        ClcConfigurationErrorReason reason = ClcConfigurationErrorReason.InvalidApplicationDirectoryOverride)
    {
        using var fixture = new BootstrapFixture();
        fixture.WriteExample("{}");
        File.WriteAllText(fixture.SettingsPath, json);
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(reason, error.Reason);
        Assert.Equal(fixture.SettingsPath, error.FilePath);
        Assert.DoesNotContain("SECRET", error.ToString());
        Assert.Null(error.InnerException);
        Assert.False(File.Exists(fixture.ConfigurationPath));
    }

    [Fact]
    public void UnreadableSettingsAreDifferentFromAnAbsentFile()
    {
        using var fixture = new BootstrapFixture();
        Directory.CreateDirectory(fixture.SettingsPath);
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(ClcConfigurationErrorReason.UnreadableFile, error.Reason);
        Assert.Equal(fixture.SettingsPath, error.FilePath);
    }

    [Fact]
    public void DanglingSettingsLinkDoesNotSelectTheDefaultOnUnix()
    {
        // Windows developer mode or administrative privileges would be needed to create the fixture.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var fixture = new BootstrapFixture();
        File.CreateSymbolicLink(fixture.SettingsPath, Path.Combine(fixture.BaseDirectory, "missing.json"));
        var error = Assert.Throws<ClcConfigurationException>(() => fixture.Load(fixture.CreateBuilder()));
        Assert.Equal(ClcConfigurationErrorReason.UnreadableFile, error.Reason);
        Assert.Equal(fixture.SettingsPath, error.FilePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverrideIsResolvedBeforeLoadingOrBootstrapping(bool web)
    {
        using var fixture = new BootstrapFixture();
        File.WriteAllText(fixture.SettingsPath, """
            { // Only the nested identity selects a directory.
              "clcCONFIGURATION":{"applicationDIRECTORYname":"preview",},
              "Logging":{"LogLevel":{"Default":"irrelevant"}},
              "ConnectionStrings":{"Default":"local-only"},
            }
            """);
        File.WriteAllText(Path.Combine(fixture.ContentRoot, "appsettings.json"),
            """{"ClcConfiguration":{"ApplicationDirectoryName":"wrong-content-root"}}""");
        const string example = """{"Value":"REPLACE_ME"}""";
        fixture.WriteExample(example);
        var builder = fixture.CreateBuilder(web);
        Assert.Throws<ArgumentException>(() => fixture.Load(builder, applicationDirectoryName: "../invalid"));
        var missing = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder, bootstrap: false));
        Assert.Equal(ClcConfigurationErrorReason.MissingFile, missing.Reason);
        var created = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder));
        Assert.Equal(ClcConfigurationErrorReason.ConfigurationCreated, created.Reason);
        Assert.Equal(fixture.ResolvePaths("preview").ConfigurationFilePath, created.FilePath);
        Assert.Equal(missing.FilePath, created.FilePath);
        Assert.Equal(example, File.ReadAllText(created.FilePath));
        var placeholder = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder));
        Assert.Equal(ClcConfigurationErrorReason.UnreplacedPlaceholder, placeholder.Reason);
        Assert.False(Directory.Exists(fixture.ResolvePaths(BootstrapFixture.AppName).ApplicationDataDirectory));
        const string operational = """{"Value":"preview","ClcConfiguration":{"ApplicationDirectoryName":"elsewhere"}}""";
        File.WriteAllText(created.FilePath, operational);
        Assert.Equal(created.FilePath, fixture.Load(builder).ConfigurationFilePath);
        Assert.Equal("preview", builder.Configuration["Value"]);
        Assert.Equal("elsewhere", builder.Configuration["ClcConfiguration:ApplicationDirectoryName"]);
        Assert.Null(builder.Configuration["ConnectionStrings:Default"]);
        Assert.Equal(operational, File.ReadAllText(created.FilePath));

        fixture.WriteConfiguration("""{"Value":"default"}""");
        File.Delete(created.FilePath);
        var stillMissing = Assert.Throws<ClcConfigurationException>(() => fixture.Load(builder, bootstrap: false));
        Assert.Equal(created.FilePath, stillMissing.FilePath);
        Assert.Equal(created.FilePath, builder.GetClcApplicationPaths().ConfigurationFilePath);
    }

    private sealed class BootstrapFixture : IDisposable
    {
        internal const string AppName = "clc-bootstrap-test";
        private readonly string root = Directory.CreateTempSubdirectory("clc-bootstrap-test-").FullName;
        internal string ContentRoot => Directory.CreateDirectory(Path.Combine(root, "content")).FullName;
        internal string BaseDirectory => Directory.CreateDirectory(Path.Combine(root, "deployment")).FullName;
        internal string SettingsPath => Path.Combine(BaseDirectory, "appsettings.json");
        internal string ExamplePath => Path.Combine(ContentRoot, "config", "example.json");
        internal string ConfigurationPath => ResolvePaths(AppName).ConfigurationFilePath;

        internal ClcApplicationPaths ResolvePaths(string applicationDirectoryName) =>
            ClcApplicationPaths.Resolve(applicationDirectoryName, Path.Combine(root, "appdata"));

        internal IHostApplicationBuilder CreateBuilder(bool web = false) => web
            ? WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = ContentRoot })
            : Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = ContentRoot });

        internal void WriteExample(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ExamplePath)!);
            File.WriteAllText(ExamplePath, json);
        }

        internal void WriteConfiguration(string json, string applicationDirectoryName = AppName)
        {
            var paths = ResolvePaths(applicationDirectoryName);
            Directory.CreateDirectory(paths.ConfigurationDirectory);
            File.WriteAllText(paths.ConfigurationFilePath, json);
        }

        internal ClcApplicationPaths Load(IHostApplicationBuilder builder, string? example = null,
            bool bootstrap = true, string applicationDirectoryName = AppName)
        {
            var paths = ApplicationDirectoryResolver.Resolve(applicationDirectoryName, BaseDirectory, Path.Combine(root, "appdata"));
            var result = bootstrap
                ? ClcConfigurationBuilderExtensions.AddClcConfigurationWithBootstrap(builder, paths, example)
                : ClcConfigurationBuilderExtensions.AddClcConfiguration(builder, paths);
            Assert.Same(builder, result);
            return paths;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
