using System;
using System.Collections.Generic;
using System.Diagnostics;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Xunit;

namespace LinkToBrowser.Tests
{
    public sealed class BrowserTests
    {
        [Fact]
        public void TrySplit_HandlesQuotedUnquotedSpacesAndFlags()
        {
            bool Probe(string p) =>
                string.Equals(p, @"C:\Program Files\Internet Explorer\iexplore.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, @"G:\Quark\quark.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, @"C:\Chrome\chrome.exe", StringComparison.OrdinalIgnoreCase);

            Assert.True(CommandLineHelper.TrySplit(@"C:\Program Files\Internet Explorer\iexplore.exe", Probe, out var ieExe, out var ieArgs));
            Assert.Equal(@"C:\Program Files\Internet Explorer\iexplore.exe", ieExe);
            Assert.Equal(string.Empty, ieArgs);

            Assert.True(CommandLineHelper.TrySplit(@"G:\Quark\quark.exe --brand-quark ""%1""", Probe, out var qExe, out var qArgs));
            Assert.Equal(@"G:\Quark\quark.exe", qExe);
            Assert.Equal(@"--brand-quark ""{url}""", qArgs);

            Assert.True(CommandLineHelper.TrySplit(@"""C:\Chrome\chrome.exe"" --single-argument %1", Probe, out var cExe, out var cArgs));
            Assert.Equal(@"C:\Chrome\chrome.exe", cExe);
            Assert.Equal("--single-argument {url}", cArgs);
        }

        [Theory]
        [InlineData("", "https://a.test/p?x=1&y=2", "\"https://a.test/p?x=1&y=2\"")]
        [InlineData("--incognito {url}", "https://a.test", "--incognito \"https://a.test\"")]
        [InlineData("--profile-directory=\"P 1\" \"{url}\"", "https://a.test", "--profile-directory=\"P 1\" \"https://a.test\"")]
        [InlineData("-osint -url \"%1\"", "https://a.test", "-osint -url \"https://a.test\"")]
        [InlineData("--single-argument {url}", "https://mp.weixin.qq.com/s/WHK_vWomvUuMK8n11Pt-dA", "--single-argument https://mp.weixin.qq.com/s/WHK_vWomvUuMK8n11Pt-dA")]
        [InlineData("--brand-quark --single-argument %1", "https://a.test/p?x=1&y=2", "--brand-quark --single-argument https://a.test/p?x=1&y=2")]
        [InlineData("--single-argument \"{url}\"", "https://a.test", "--single-argument https://a.test")]
        public void BuildArguments_SubstitutesPlaceholdersWithoutDoubleQuoting(string template, string url, string expected)
        {
            Assert.Equal(expected, BrowserLauncher.BuildArguments(template, url));
        }

        [Fact]
        public void BuildArguments_EscapesQuotesInUrl()
        {
            var built = BrowserLauncher.BuildArguments("--incognito {url}", "https://a.test/?x=\"q\"");
            Assert.Equal("--incognito \"https://a.test/?x=%22q%22\"", built);
        }

        [Fact]
        public void TryLaunch_SelfPathBlocked()
        {
            var launcher = new BrowserLauncher(
                @"D:\Tools\LinkToBrowser.exe",
                _ => throw new InvalidOperationException("Start should never be called"),
                _ => true);

            Assert.False(launcher.TryLaunch(@"d:\tools\linktobrowser.exe", "", "https://a.test", out var err));
            Assert.Contains("self", err, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryLaunch_CommandOver32000Chars_FailsWithoutStarting()
        {
            var launcher = new BrowserLauncher(
                @"D:\Tools\LinkToBrowser.exe",
                _ => throw new InvalidOperationException("Start should never be called"),
                _ => true);

            var hugeUrl = "https://a.test/" + new string('x', 33000);
            Assert.False(launcher.TryLaunch(@"C:\Chrome\chrome.exe", "", hugeUrl, out var err));
            Assert.Contains("32000", err);
        }

        [Fact]
        public void TryLaunch_StartThrows_ReturnsFalseWithError()
        {
            var launcher = new BrowserLauncher(
                @"D:\Tools\LinkToBrowser.exe",
                _ => throw new InvalidOperationException("access denied"),
                _ => true);

            Assert.False(launcher.TryLaunch(@"C:\Chrome\chrome.exe", "", "https://a.test", out var err));
            Assert.Contains("access denied", err);
        }

        [Fact]
        public void TryLaunch_SetsWorkingDirectoryToBrowserFolder()
        {
            ProcessStartInfo captured = null;
            var launcher = new BrowserLauncher(
                @"D:\Tools\LinkToBrowser.exe",
                psi => { captured = psi; return true; },
                _ => true);

            Assert.True(launcher.TryLaunch(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "--single-argument {url}", "https://a.test", out _));
            Assert.NotNull(captured);
            Assert.Equal(@"C:\Program Files\Google\Chrome\Application", captured.WorkingDirectory);
            Assert.Equal("--single-argument https://a.test", captured.Arguments);
        }

        [Fact]
        public void ParseChromiumProfiles_RequiresAtLeastTwoProfiles()
        {
            var singleJson = @"{""profile"":{""info_cache"":{""Default"":{""name"":""Default""}}}}";
            Assert.Empty(BrowserCatalog.ParseChromiumProfiles("Google Chrome", @"C:\Chrome\chrome.exe", singleJson));

            var multiJson = @"{""profile"":{""info_cache"":{""Default"":{""name"":""Personal""},""Profile 2"":{""name"":""JP""}}}}";
            var entries = BrowserCatalog.ParseChromiumProfiles("Google Chrome", @"C:\Chrome\chrome.exe", multiJson);
            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, e =>
                e.ArgumentsTemplate == "--profile-directory=\"Profile 2\" {url}" &&
                e.Name == "Google Chrome · JP" &&
                e.Kind == BrowserKind.ChromiumProfile);
        }

        [Fact]
        public void Arrange_Deduplicates_AppliesOrder_AndFiltersHidden()
        {
            var a = new BrowserEntry { Id = "id-a", Name = "Zulu", ExecutablePath = @"C:\a.exe" };
            var b = new BrowserEntry { Id = "id-b", Name = "Alpha", ExecutablePath = @"C:\b.exe" };
            var c = new BrowserEntry { Id = "id-c", Name = "Beta", ExecutablePath = @"C:\c.exe" };
            var dupA = new BrowserEntry { Id = "id-a", Name = "Zulu Dup", ExecutablePath = @"C:\a.exe" };

            var cfg = new AppConfig
            {
                BrowserOrder = new List<string> { "id-a", "id-c" },
                HiddenBrowserIds = new List<string> { "id-c" }
            };

            var visible = BrowserCatalog.Arrange(new[] { b, c, a, dupA }, cfg, includeHidden: false);
            Assert.Equal(2, visible.Count);
            Assert.Equal("id-a", visible[0].Id);
            Assert.Equal("id-b", visible[1].Id);

            var all = BrowserCatalog.Arrange(new[] { b, c, a, dupA }, cfg, includeHidden: true);
            Assert.Equal(3, all.Count);
            Assert.Equal(new[] { "id-a", "id-c", "id-b" }, new[] { all[0].Id, all[1].Id, all[2].Id });
        }
    }
}
