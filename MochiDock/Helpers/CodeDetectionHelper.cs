namespace MochiDock.Helpers;

public static class CodeDetectionHelper
{
    /// <summary>The verification code in a mail, for the Email card's Copy Code button (see <see cref="MochiDock.Core.VerificationCodeExtractor"/>).</summary>
    public static string? ExtractVerificationCode(string? subject, string? body) =>
        MochiDock.Core.VerificationCodeExtractor.Extract(subject, body);
}
