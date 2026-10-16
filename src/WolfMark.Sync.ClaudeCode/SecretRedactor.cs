using System.Text.RegularExpressions;

namespace WolfMark.Sync.ClaudeCode;

/// <summary>
/// Removes likely secrets from prompts before they are written to OneDrive (SPEC §4.7, M7-06).
/// Best effort – it reduces risk, it does not guarantee anything.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "[REDACTED]";

    public static string Redact(string text)
    {
        text = PrivateKeyRegex().Replace(text, Mask);
        text = TokenRegex().Replace(text, Mask);
        text = JwtRegex().Replace(text, Mask);
        text = BearerRegex().Replace(text, "Bearer " + Mask);
        text = KeyValueRegex().Replace(text, m => m.Groups["key"].Value + m.Groups["sep"].Value + Mask);
        return text;
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(
        @"\b(?:sk-ant-[A-Za-z0-9_\-]{20,}|sk-(?:proj-)?[A-Za-z0-9_\-]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|xox[abposr]-[A-Za-z0-9\-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_\-]{35}|glpat-[A-Za-z0-9_\-]{20,}|npm_[A-Za-z0-9]{36})")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]{20,}=*")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)(?<key>\b(?:password|passwd|pwd|secret|client[_-]?secret|api[_-]?key|access[_-]?token|auth[_-]?token|token)\b)(?<sep>\s*[=:]\s*)(?!\[REDACTED\])[""']?[^\s;'""]{4,}[""']?")]
    private static partial Regex KeyValueRegex();
}
