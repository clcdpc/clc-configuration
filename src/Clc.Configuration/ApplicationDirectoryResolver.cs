using System.Text.Json;

namespace Clc.Configuration;

internal static class ApplicationDirectoryResolver
{
    // The optional locations allow isolated tests to run the same selection and path resolution.
    internal static ClcApplicationPaths Resolve(string defaultName, string? baseDirectory = null, string? programDataRoot = null)
    {
        ClcApplicationPaths.ValidateApplicationDirectoryName(defaultName);
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "appsettings.json");
        var name = ReadOverride(path) ?? defaultName;
        return programDataRoot is null
            ? ClcApplicationPaths.Resolve(name)
            : ClcApplicationPaths.Resolve(name, programDataRoot);
    }

    private static string? ReadOverride(string path)
    {
        // Check the entry itself: opening a dangling symlink reports file-not-found too.
        // File.Exists would also silently treat access errors as an absent override.
        try
        {
            File.GetAttributes(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new ClcConfigurationException(ClcConfigurationErrorReason.UnreadableFile, path, null, null);
        }

        var bytes = ExternalConfigurationLoader.ReadJsonBytes(path)
            ?? throw new ClcConfigurationException(ClcConfigurationErrorReason.UnreadableFile, path, null, null);
        using var document = ExternalConfigurationLoader.ParseJson(path, bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ClcConfigurationException(ClcConfigurationErrorReason.InvalidRootShape, path, null, null);
        }

        var section = FindProperty(document.RootElement, "ClcConfiguration", path);
        if (section.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }
        if (section.ValueKind != JsonValueKind.Object)
        {
            throw InvalidOverride(path);
        }

        var property = FindProperty(section, "ApplicationDirectoryName", path);
        if (property.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }
        if (property.ValueKind != JsonValueKind.String)
        {
            throw InvalidOverride(path);
        }

        try
        {
            var name = property.GetString()!;
            ClcApplicationPaths.ValidateApplicationDirectoryName(name);
            return name;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            // Decoding a string with an unpaired escaped surrogate can also fail here.
            throw InvalidOverride(path);
        }
    }

    private static JsonElement FindProperty(JsonElement obj, string name, string path)
    {
        JsonElement match = default;
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(ExternalConfigurationLoader.GetPropertyName(path, property), name, StringComparison.OrdinalIgnoreCase))
            {
                if (match.ValueKind != JsonValueKind.Undefined)
                {
                    throw InvalidOverride(path);
                }
                match = property.Value;
            }
        }
        return match;
    }

    private static ClcConfigurationException InvalidOverride(string path) =>
        new(ClcConfigurationErrorReason.InvalidApplicationDirectoryOverride, path, null, null);
}
