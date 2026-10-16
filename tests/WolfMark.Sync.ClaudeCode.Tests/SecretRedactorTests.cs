namespace WolfMark.Sync.ClaudeCode.Tests;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("key sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123 end", "key [REDACTED] end")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789", "[REDACTED]")]
    [InlineData("AKIAABCDEFGHIJKLMNOP", "[REDACTED]")]
    [InlineData("Server=x;User Id=sa;Password=Sup3rSecret!;", "Server=x;User Id=sa;Password=[REDACTED];")]
    [InlineData("export API_KEY=abcd1234efgh", "export API_KEY=[REDACTED]")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz.123", "Authorization: Bearer [REDACTED]")]
    [InlineData("nothing secret here, password policy is fine", "nothing secret here, password policy is fine")]
    public void Redacts(string input, string expected) => Assert.Equal(expected, SecretRedactor.Redact(input));

    [Fact]
    public void Redacts_private_keys()
    {
        var text = "a\n-----BEGIN RSA PRIVATE KEY-----\nMIIE\nabc\n-----END RSA PRIVATE KEY-----\nb";
        Assert.Equal("a\n[REDACTED]\nb", SecretRedactor.Redact(text));
    }
}
