using MochiDock.Core;

public class VerificationCodeExtractorTests
{
    [Fact]
    public void CloudflareAccessMailGivesTheLoginCodeNotAFragmentOfALinkOrId()
    {
        const string subject = "Cloudflare Access login code for app.example.com";
        const string body = "Your login code: 110445 Your Cloudflare Access code 110445 This code expires after 10 minutes or if you request a new code. " +
                            "Prefer not to use the code? Finish logging in to app.example.com with this link: " +
                            "https://example.cloudflareaccess.com/cdn-cgi/access/callback?state=what-957a&nonce=hat-957a " +
                            "Request ID hat-957a";
        Assert.Equal("110445", VerificationCodeExtractor.Extract(subject, body));
    }

    [Theory]
    [InlineData("G-123456 is your Google verification code", "", "G-123456")]
    [InlineData("Sign in", "Your code is 123-456. It expires soon.", "123-456")]
    [InlineData("Microsoft account", "Security code: 4920", "4920")]
    [InlineData("Login", "OTP 849201 for your account", "849201")]
    [InlineData("Masuk", "Kode verifikasi Anda 5521. Jangan berikan kode ini.", "5521")]
    [InlineData("Sign in", "Use this one-time password: 738201", "738201")]
    [InlineData("Verify", "Your verification code is AB12CD", "AB12CD")]
    public void CommonFormats(string subject, string body, string expected) =>
        Assert.Equal(expected, VerificationCodeExtractor.Extract(subject, body));

    [Theory]
    [InlineData("Your order", "Order placed in 2026. Your verification code is 552901.", "552901")]
    [InlineData("Login at 21:14", "Your code: 8841 was requested at 21:14", "8841")]
    [InlineData("Payment", "You paid Rp 150000. Your OTP is 6620", "6620")]
    [InlineData("Payment", "Total $2500 charged. Verification code 77310", "77310")]
    [InlineData("Call us", "Call +62 812 3456 7890 if you did not request code 9081", "9081")]
    public void NumbersThatAreNotTheCodeLose(string subject, string body, string expected) =>
        Assert.Equal(expected, VerificationCodeExtractor.Extract(subject, body));

    [Fact]
    public void HyphenatedWordsAreNeverACode() =>
        Assert.Null(VerificationCodeExtractor.Extract("Your one-time sign-up", "Verify your e-mail with the follow-up link."));

    [Fact]
    public void ACodeThatOnlyAppearsInsideALinkIsIgnored() =>
        Assert.Null(VerificationCodeExtractor.Extract("Verify your account", "Open https://example.com/verify?code=482910 to continue."));

    [Theory]
    [InlineData("Team lunch", "We meet at room 4021 on Friday.")]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    public void NoCodeKeywordOrNoTextGivesNull(string? subject, string? body) =>
        Assert.Null(VerificationCodeExtractor.Extract(subject, body));
}
