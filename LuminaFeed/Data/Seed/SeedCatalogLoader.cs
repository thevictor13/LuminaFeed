using System.Reflection;
using System.Text.Json;

namespace LuminaFeed.Data.Seed;

/// <summary>
/// Loads and parses the embedded G0.R research catalogue (<c>rss-feeds.json</c>). Parsing is kept pure and
/// separate from database access so it can be unit-tested directly.
/// </summary>
internal static class SeedCatalogLoader
{
    /// <summary>File name of the embedded research resource (linked from <c>doc/research/</c> in the csproj).</summary>
    private const string ResourceFileName = "rss-feeds.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Parses catalogue JSON. Throws <see cref="InvalidOperationException"/> if it is empty/invalid.</summary>
    public static SeedCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Seed catalogue JSON is empty.");

        var catalog = JsonSerializer.Deserialize<SeedCatalog>(json, Options)
            ?? throw new InvalidOperationException("Seed catalogue JSON deserialized to null.");

        if (catalog.Categories.Count == 0 || catalog.Feeds.Count == 0)
            throw new InvalidOperationException("Seed catalogue has no categories or no feeds.");

        return catalog;
    }

    /// <summary>Parses catalogue JSON from a stream.</summary>
    public static SeedCatalog Parse(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Loads the catalogue embedded in this assembly.</summary>
    public static SeedCatalog LoadEmbedded()
    {
        var assembly = typeof(SeedCatalogLoader).Assembly;
        using var stream = OpenEmbeddedResource(assembly);
        return Parse(stream);
    }

    private static Stream OpenEmbeddedResource(Assembly assembly)
    {
        // Resolve by suffix rather than a hard-coded manifest name (the linked resource's logical name
        // depends on the root namespace and link path, which are easy to get wrong).
        var name = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith(ResourceFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Embedded seed resource '{ResourceFileName}' not found in {assembly.GetName().Name}.");

        return assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded seed resource stream '{name}' could not be opened.");
    }
}
