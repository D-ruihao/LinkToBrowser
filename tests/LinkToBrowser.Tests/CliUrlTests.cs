using LinkToBrowser.Core;
using Xunit;

namespace LinkToBrowser.Tests
{
    public sealed class CliUrlTests
    {
        [Theory]
        [InlineData(new string[0], CliKind.OpenSettings, false, "")]
        [InlineData(new[] { "--settings" }, CliKind.OpenSettings, false, "")]
        [InlineData(new[] { "--register" }, CliKind.Register, false, "")]
        [InlineData(new[] { "--unregister" }, CliKind.Unregister, false, "")]
        [InlineData(new[] { "-Embedding" }, CliKind.Ignore, false, "")]
        [InlineData(new[] { "--version" }, CliKind.Ignore, false, "")]
        [InlineData(new[] { "--no-ui" }, CliKind.Ignore, true, "")]
        [InlineData(new[] { "--no-ui", "https://a.test" }, CliKind.HandleUrl, true, "https://a.test")]
        [InlineData(new[] { "https://a.test/x", "y" }, CliKind.HandleUrl, false, "https://a.test/x y")]
        public void Parse_ClassifiesArgs(string[] args, CliKind expectedKind, bool expectedNoUi, string expectedUrl)
        {
            var inv = CliParser.Parse(args);
            Assert.Equal(expectedKind, inv.Kind);
            Assert.Equal(expectedNoUi, inv.NoUi);
            Assert.Equal(expectedUrl, inv.RawUrl);
        }

        [Theory]
        [InlineData("LinkToBrowser.URL:https://a.example/x?y=1&z=2", "https://a.example/x?y=1&z=2")]
        [InlineData("linktobrowser.url: http://127.0.0.1:12606/#/sessions?id=abc", "http://127.0.0.1:12606/#/sessions?id=abc")]
        [InlineData("linkclip.url:https://legacy.example/path", "https://legacy.example/path")]
        [InlineData("\"http://127.0.0.1:12606/#/sessions?id=abc\"", "http://127.0.0.1:12606/#/sessions?id=abc")]
        [InlineData("https://e.com/p?q=%E4%B8%AD", "https://e.com/p?q=%E4%B8%AD")]
        [InlineData("https://e.com/a?x=\"q\"", "https://e.com/a?x=\"q\"")]
        public void TryNormalize_PreservesValidUrlsVerbatim(string raw, string expected)
        {
            Assert.True(UrlService.TryNormalize(raw, out var actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("file:///C:/x.exe")]
        [InlineData("javascript:alert(1)")]
        [InlineData("https://e.com/a\r\nb")]
        [InlineData("https://e.com/a\0b")]
        [InlineData("")]
        [InlineData("   ")]
        public void TryNormalize_RejectsUnsafeOrInvalidInputs(string raw)
        {
            Assert.False(UrlService.TryNormalize(raw, out _));
        }

        [Fact]
        public void StripTracking_RemovesTrackingKeys_PreservesEncodingAndHashRoute()
        {
            var input = "http://h:1/p?keep=%E4%B8%AD&utm_source=wx&fbclid=1&GCLID=x&msclkid=2&spm=3#/s?utm_source=x";
            var actual = UrlService.StripTracking(input);
            Assert.Equal("http://h:1/p?keep=%E4%B8%AD#/s?utm_source=x", actual);
        }

        [Fact]
        public void StripTracking_DropsQuestionMarkWhenQueryBecomesEmpty()
        {
            Assert.Equal("https://h/p", UrlService.StripTracking("https://h/p?utm_a=1&fbclid=2"));
            Assert.Equal("https://h/p#frag", UrlService.StripTracking("https://h/p?utm_a=1#frag"));
        }

        [Fact]
        public void Host_ExtractsAuthorityForDisplay()
        {
            Assert.Equal("example.com", UrlService.Host("https://example.com/a/b"));
            Assert.Equal("127.0.0.1:12606", UrlService.Host("http://127.0.0.1:12606/#/sessions"));
        }
    }
}
