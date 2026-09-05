using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class TokenProtectorTests
{
    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        const string plain = "refresh-token-abc123";
        var stored = TokenProtector.Protect(plain);
        Assert.StartsWith("DPAPI:", stored);
        Assert.NotEqual(plain, stored);
        Assert.Equal(plain, TokenProtector.Unprotect(stored));
    }

    [Fact]
    public void EmptyOrNull_ReturnsAsIs()
    {
        Assert.Equal("", TokenProtector.Protect(""));
        Assert.Equal("", TokenProtector.Unprotect(""));
    }

    [Fact]
    public void LegacyPlainText_ReturnsAsIs_ForBackwardCompatibility()
    {
        const string legacy = "old-plain-token";
        Assert.Equal(legacy, TokenProtector.Unprotect(legacy));
    }

    [Fact]
    public void TamperedCipher_ReturnsEmpty()
    {
        var stored = TokenProtector.Protect("some-token");
        var tampered = stored[..^4] + "AAAA";
        Assert.Equal("", TokenProtector.Unprotect(tampered));
    }
}
