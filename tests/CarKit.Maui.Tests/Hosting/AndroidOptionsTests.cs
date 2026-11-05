using Xunit;

namespace CarKit.Maui.Tests.Hosting;

public class AndroidOptionsTests
{
    [Fact]
    public void Allowed_host_entries_parse_digest_and_package()
    {
        var (digest, package) = CarKitAndroidOptions.ParseAllowedHost(" fdb00c43 , com.google.android.projection.gearhead ");

        Assert.Equal("fdb00c43", digest);
        Assert.Equal("com.google.android.projection.gearhead", package);
    }

    // keytool prints the fingerprint in uppercase with colons; the Car App Library resource uses lowercase without separators
    [Fact]
    public void Allowed_host_digest_is_normalized()
    {
        var (digest, _) = CarKitAndroidOptions.ParseAllowedHost("FD:B0:0C:43,com.example.host");

        Assert.Equal("fdb00c43", digest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("com.example.host")]
    [InlineData(",com.example.host")]
    [InlineData("abc,")]
    [InlineData("a,b,c")]
    public void Malformed_allowed_host_entries_are_rejected(string entry)
    {
        var error = Assert.Throws<InvalidOperationException>(() => CarKitAndroidOptions.ParseAllowedHost(entry));
        Assert.Contains("AllowedHosts", error.Message, StringComparison.Ordinal);
    }
}
