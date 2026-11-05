namespace CarKit.Maui;

/// <summary>Android Auto and Android Automotive options.</summary>
public sealed class CarKitAndroidOptions
{
    /// <summary>
    /// Gets the hosts allowed to bind to the car app service in release builds, as
    /// <c>"&lt;SHA-256 signing certificate digest&gt;,&lt;package name&gt;"</c> entries. When the list is empty,
    /// CarKit uses its built-in list (Android Auto and the Android Automotive template host). Debuggable builds
    /// allow every host so the Desktop Head Unit works.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    // Format of the Car App Library hosts_allowlist resource: "digest,package"; the digest is lowercase without
    // separators (keytool prints it in uppercase with colons)
    internal static (string Digest, string Package) ParseAllowedHost(string entry)
    {
        var parts = (entry ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        return parts is [{ Length: > 0 } digest, { Length: > 0 } package]
            ? (digest.Replace(":", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), package)
            : throw new InvalidOperationException($"car.Android.AllowedHosts entry '{entry}' must be '<SHA-256 digest>,<package name>'.");
    }
}
