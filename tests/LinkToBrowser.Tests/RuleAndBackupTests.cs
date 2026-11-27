using System;
using System.IO;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Xunit;

namespace LinkToBrowser.Tests
{
    public sealed class RuleAndBackupTests : IDisposable
    {
        private readonly string _tempDir;

        public RuleAndBackupTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LinkToBrowserRuleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void RuleMatcher_DomainSuffix_EnforcesDotBoundary_AndSupportsMultiPatternAndPort()
        {
            var rules = new[]
            {
                new RoutingRule
                {
                    Id = "r1",
                    Enabled = true,
                    MatchKind = RuleMatchKind.DomainSuffix,
                    Pattern = "*.github.com, localhost:3000"
                }
            };

            Assert.NotNull(RuleMatcher.Match("https://github.com/D-ruihao", rules));
            Assert.NotNull(RuleMatcher.Match("https://gist.github.com/abc", rules));
            Assert.NotNull(RuleMatcher.Match("http://localhost:3000/app", rules));

            Assert.Null(RuleMatcher.Match("https://mygithub.com/abc", rules));
            Assert.Null(RuleMatcher.Match("http://localhost:8080/app", rules));
        }

        [Fact]
        public void RuleMatcher_PrefixWildcardAndPriorityOrder_MatchesFirstEnabledRule()
        {
            var disabled = new RoutingRule
            {
                Id = "r0",
                Enabled = false,
                MatchKind = RuleMatchKind.DomainSuffix,
                Pattern = "example.com"
            };
            var specificPrefix = new RoutingRule
            {
                Id = "r1",
                Enabled = true,
                MatchKind = RuleMatchKind.UrlPrefix,
                Pattern = "https://example.com/admin/"
            };
            var wildcard = new RoutingRule
            {
                Id = "r2",
                Enabled = true,
                MatchKind = RuleMatchKind.Wildcard,
                Pattern = "*oauth*"
            };
            var domainFallback = new RoutingRule
            {
                Id = "r3",
                Enabled = true,
                MatchKind = RuleMatchKind.DomainSuffix,
                Pattern = "example.com"
            };

            var list = new[] { disabled, specificPrefix, wildcard, domainFallback };

            Assert.Equal("r1", RuleMatcher.Match("https://example.com/admin/users", list)?.Id);
            Assert.Equal("r2", RuleMatcher.Match("https://auth.test.com/oauth/login", list)?.Id);
            Assert.Equal("r3", RuleMatcher.Match("https://example.com/home", list)?.Id);
        }

        [Fact]
        public void RuleMatcher_CreateDefaultSamples_AreDisabledByDefault()
        {
            var samples = RuleMatcher.CreateDefaultSamples(isEnglish: true);
            Assert.True(samples.Count >= 3);
            Assert.All(samples, s => Assert.False(s.Enabled));
            Assert.All(samples, s => Assert.False(string.IsNullOrWhiteSpace(s.Pattern)));
        }

        [Fact]
        public void Dispatcher_AppliesMatchingRule_AndModifierOverridesRule()
        {
            var targetBrowser = new BrowserEntry
            {
                Id = "custom:work-chrome",
                Name = "Work Chrome",
                ExecutablePath = @"C:\chrome.exe",
                ArgumentsTemplate = "--profile-directory=\"Work\" {url}",
                Kind = BrowserKind.Custom
            };

            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.ClipboardOnly,
                ShowClipboardPopup = false,
                Modifier = ModifierKey.Shift,
                EnableRoutingRules = true,
                Rules =
                {
                    new RoutingRule
                    {
                        Id = "r1",
                        Enabled = true,
                        MatchKind = RuleMatchKind.DomainSuffix,
                        Pattern = "github.com",
                        Action = RuleActionKind.OpenTarget,
                        TargetBrowser = targetBrowser
                    }
                }
            };

            string launchedExe = null;
            string launchedTpl = null;
            var shiftHeld = false;

            var dispatcher = new LinkDispatcher(
                cfg,
                (exe, tpl, url) => { launchedExe = exe; launchedTpl = tpl; return (true, null); },
                _ => true,
                _ => string.Empty,
                _ => shiftHeld,
                _ => (false, string.Empty, string.Empty));

            // Matching URL hits rule -> opens target browser
            var res = dispatcher.Dispatch(CliParser.Parse(new[] { "https://github.com/D-ruihao" }));
            Assert.Equal(DispatchKind.Done, res.Kind);
            Assert.Equal(@"C:\chrome.exe", launchedExe);
            Assert.Contains("Work", launchedTpl);

            // Modifier held -> overrides rule and opens picker
            shiftHeld = true;
            var modRes = dispatcher.Dispatch(CliParser.Parse(new[] { "https://github.com/D-ruihao" }));
            Assert.Equal(DispatchKind.ShowPicker, modRes.Kind);
        }

        [Fact]
        public void ConfigBackup_ExportParseMergeReplaceAndPreImportBackup()
        {
            var current = new AppConfig
            {
                GlobalMode = GlobalMode.ClipboardOnly,
                Theme = ThemeMode.Dark,
                Language = LanguageMode.EnUs,
                OriginalDefaultProgId = "ChromeHTML",
                Rules =
                {
                    new RoutingRule
                    {
                        Id = "r-existing",
                        Enabled = true,
                        Name = "Existing",
                        MatchKind = RuleMatchKind.DomainSuffix,
                        Pattern = "localhost",
                        Action = RuleActionKind.PickerPopup
                    }
                }
            };

            var toExport = new AppConfig
            {
                GlobalMode = GlobalMode.RecentBrowser,
                Theme = ThemeMode.Light,
                OriginalDefaultProgId = "MSEdgeHTM",
                CustomBrowsers =
                {
                    new BrowserEntry { Id = "c1", Name = "Custom1", ExecutablePath = @"C:\a.exe", Kind = BrowserKind.Custom }
                },
                Rules =
                {
                    new RoutingRule { Id = "r-dup", Enabled = false, MatchKind = RuleMatchKind.DomainSuffix, Pattern = "localhost", Action = RuleActionKind.ClipboardOnly },
                    new RoutingRule { Id = "r-new", Enabled = true, Name = "GitHub", MatchKind = RuleMatchKind.DomainSuffix, Pattern = "github.com", Action = RuleActionKind.PickerPopup }
                }
            };

            // Rules-only export preserves rules and custom browsers, excludes global mode
            var rulesOnlyJson = ConfigBackupService.ExportToJson(toExport, rulesAndCustomOnly: true);
            Assert.True(ConfigBackupService.TryParseBackup(rulesOnlyJson, out var parsedRulesOnly, out var err), err);
            Assert.True(parsedRulesOnly.RulesOnly);

            var merged = ConfigBackupService.ApplyImport(current, parsedRulesOnly, replaceAll: false);
            Assert.Equal(GlobalMode.ClipboardOnly, merged.GlobalMode);
            Assert.Equal(LanguageMode.EnUs, merged.Language);
            Assert.Equal("ChromeHTML", merged.OriginalDefaultProgId);
            Assert.Single(merged.CustomBrowsers);
            Assert.Equal(2, merged.Rules.Count); // localhost deduplicated, github.com added

            // Full export & replace preserves local OriginalDefaultProgId snapshot
            var fullJson = ConfigBackupService.ExportToJson(toExport, rulesAndCustomOnly: false);
            Assert.True(ConfigBackupService.TryParseBackup(fullJson, out var parsedFull, out _));
            var replaced = ConfigBackupService.ApplyImport(current, parsedFull, replaceAll: true);
            Assert.Equal(GlobalMode.RecentBrowser, replaced.GlobalMode);
            Assert.Equal(ThemeMode.Light, replaced.Theme);
            Assert.Equal("ChromeHTML", replaced.OriginalDefaultProgId);

            // Invalid JSON rejected
            Assert.False(ConfigBackupService.TryParseBackup("{\"unrelated\":123}", out _, out _));
            Assert.False(ConfigBackupService.TryParseBackup("{not-json", out _, out _));

            // Pre-import backup writes config.bak.json
            var cfgPath = Path.Combine(_tempDir, "config.json");
            File.WriteAllText(cfgPath, "{\"ConfigVersion\":1}");
            ConfigBackupService.CreatePreImportBackupIfExists(cfgPath);
            Assert.True(File.Exists(Path.Combine(_tempDir, "config.bak.json")));
        }

        [Fact]
        public void CliScriptCommands_SupportJsonHelpStatusRulesTestUrlAndBackup()
        {
            var cfgDir = Path.Combine(_tempDir, "cli-cfg");
            Directory.CreateDirectory(cfgDir);
            var cfgSvc = new ConfigService(cfgDir, cfgDir);
            var logSvc = new LogService(Path.Combine(cfgDir, "logs"));
            var reg = new SystemRegistration(@"D:\Tools\LinkToBrowser.exe");

            string RunCli(params string[] args)
            {
                var sw = new StringWriter();
                var inv = CliParser.Parse(args);
                Assert.Equal(CliKind.ScriptCommand, inv.Kind);
                var code = CliCommandHandler.Execute(inv, cfgSvc, logSvc, reg, @"D:\Tools\LinkToBrowser.exe", sw);
                Assert.Equal(0, code);
                return sw.ToString();
            }

            // 1. --help --json
            var helpJson = RunCli("--help", "--json");
            Assert.Contains("\"ok\":true", helpJson);
            Assert.Contains("--test-url", helpJson);

            // 2. --set-mode recent --fallback clipboard --json
            var modeJson = RunCli("--set-mode", "recent", "--fallback", "clipboard", "--json");
            Assert.Contains("\"ok\":true", modeJson);
            Assert.Contains("RecentBrowser", modeJson);

            // 3. --rules add & list & test-url
            var addJson = RunCli("--rules", "add", "--name", "GitHub", "--kind", "domain", "--pattern", "github.com", "--action", "clipboard", "--json");
            Assert.Contains("\"ok\":true", addJson);

            var testHitJson = RunCli("--test-url", "https://github.com/D-ruihao/LinkToBrowser", "--json");
            Assert.Contains("Rule:ClipboardOnly", testHitJson);

            var testMissJson = RunCli("--test-url", "https://example.org", "--json");
            Assert.Contains("Global:RecentBrowser", testMissJson);

            // 4. --rules disable / enable / remove
            RunCli("--rules", "disable", "GitHub", "--json");
            var testDisabledJson = RunCli("--test-url", "https://github.com/D-ruihao/LinkToBrowser", "--json");
            Assert.Contains("Global:RecentBrowser", testDisabledJson);

            RunCli("--rules", "enable", "GitHub", "--json");
            var testEnabledJson = RunCli("--test-url", "https://github.com/D-ruihao/LinkToBrowser", "--json");
            Assert.Contains("Rule:ClipboardOnly", testEnabledJson);

            // 5. --export & --import
            var exportFile = Path.Combine(_tempDir, "exported.json");
            RunCli("--export", exportFile, "--rules-only", "--json");
            Assert.True(File.Exists(exportFile));

            RunCli("--rules", "remove", "GitHub", "--json");
            var afterRemove = RunCli("--test-url", "https://github.com/D-ruihao/LinkToBrowser", "--json");
            Assert.Contains("Global:RecentBrowser", afterRemove);

            RunCli("--import", exportFile, "--json");
            var afterImport = RunCli("--test-url", "https://github.com/D-ruihao/LinkToBrowser", "--json");
            Assert.Contains("Rule:ClipboardOnly", afterImport);
        }

        [Fact]
        public void RuleMatcher_SupportsDefaultPortWildcardPortIpv6AndRejectsSeparatorOnlyPatterns()
        {
            var rules = new[]
            {
                new RoutingRule { Id = "r1", Enabled = true, MatchKind = RuleMatchKind.DomainSuffix, Pattern = "example.com:443" },
                new RoutingRule { Id = "r2", Enabled = true, MatchKind = RuleMatchKind.DomainSuffix, Pattern = "*.corp.test:8443" },
                new RoutingRule { Id = "r3", Enabled = true, MatchKind = RuleMatchKind.DomainSuffix, Pattern = "[::1]" },
                new RoutingRule { Id = "r4", Enabled = true, MatchKind = RuleMatchKind.DomainSuffix, Pattern = "[::1]:3000" }
            };

            Assert.Equal("r1", RuleMatcher.Match("https://example.com/path", rules)?.Id);
            Assert.Null(RuleMatcher.Match("http://example.com/path", rules));
            Assert.Equal("r2", RuleMatcher.Match("https://api.corp.test:8443/v1", rules)?.Id);
            Assert.Null(RuleMatcher.Match("https://api.corp.test:9443/v1", rules));
            Assert.Equal("r3", RuleMatcher.Match("http://[::1]:8080/dev", rules)?.Id);
            Assert.Equal("r4", RuleMatcher.Match("http://[::1]:3000/app", new[] { rules[3] })?.Id);
            Assert.Null(RuleMatcher.Match("http://[::1]:8080/app", new[] { rules[3] }));

            Assert.False(RuleMatcher.HasValidTokens(", , ; \r\n "));
            Assert.True(RuleMatcher.HasValidTokens(" , github.com ; "));
        }

        [Fact]
        public void Dispatcher_RunningWindowTarget_NeverFallsBackToRawProcessWhenCdpDisabled()
        {
            var cfg = new AppConfig
            {
                GlobalMode = GlobalMode.ClipboardOnly,
                EnableCdpForIsolatedBrowsers = false,
                EnableRoutingRules = true,
                Rules =
                {
                    new RoutingRule
                    {
                        Id = "r-bit",
                        Enabled = true,
                        MatchKind = RuleMatchKind.DomainSuffix,
                        Pattern = "github.com",
                        Action = RuleActionKind.OpenTarget,
                        TargetBrowser = new BrowserEntry
                        {
                            Id = "cdp:d:\\bitbrowser\\p1",
                            Name = "BitBrowser · Profile 1",
                            ExecutablePath = @"C:\BitBrowser\chrome.exe",
                            ArgumentsTemplate = "--user-data-dir=\"D:\\bitbrowser\\p1\" {url}",
                            UserDataDir = @"D:\bitbrowser\p1",
                            Kind = BrowserKind.RunningWindow
                        }
                    }
                }
            };

            var processLaunched = false;
            var copied = false;
            var dispatcher = new LinkDispatcher(
                cfg,
                (_, _, _) => { processLaunched = true; return (true, null); },
                _ => { copied = true; return true; },
                _ => string.Empty,
                _ => false,
                _ => (false, string.Empty, string.Empty),
                null,
                (_, _) => (true, null),
                null);

            var res = dispatcher.Dispatch(CliParser.Parse(new[] { "https://github.com/D-ruihao" }));
            Assert.False(processLaunched);
            Assert.True(copied);
            Assert.Equal(DispatchKind.ShowClipboard, res.Kind);
            Assert.False(string.IsNullOrWhiteSpace(res.Warning));
        }

        [Fact]
        public void ConfigBackup_IsoTimestamp_RulesOnlyReplace_AndMergeIdCollisionProtection()
        {
            var current = new AppConfig
            {
                GlobalMode = GlobalMode.ClipboardOnly,
                Theme = ThemeMode.Dark,
                Language = LanguageMode.EnUs,
                OriginalDefaultProgId = "ChromeHTML",
                Rules =
                {
                    new RoutingRule
                    {
                        Id = "shared-id",
                        Enabled = true,
                        Name = "Local",
                        MatchKind = RuleMatchKind.DomainSuffix,
                        Pattern = "localhost",
                        Action = RuleActionKind.PickerPopup
                    }
                }
            };

            var incoming = new AppConfig
            {
                GlobalMode = GlobalMode.RecentBrowser,
                Theme = ThemeMode.Light,
                Rules =
                {
                    new RoutingRule
                    {
                        Id = "shared-id",
                        Enabled = true,
                        Name = "GitHub",
                        MatchKind = RuleMatchKind.DomainSuffix,
                        Pattern = "github.com",
                        Action = RuleActionKind.ClipboardOnly
                    }
                }
            };

            var rulesOnlyJson = ConfigBackupService.ExportToJson(incoming, rulesAndCustomOnly: true);
            Assert.DoesNotContain("\\/Date(", rulesOnlyJson);
            Assert.True(ConfigBackupService.TryParseBackup(rulesOnlyJson, out var parsed, out var err), err);
            Assert.True(DateTime.TryParse(parsed.ExportedAtUtc, out _));

            // Merge deduplicates conflicting Id instead of keeping duplicate IDs
            var merged = ConfigBackupService.ApplyImport(current, parsed, replaceAll: false);
            Assert.Equal(2, merged.Rules.Count);
            Assert.NotEqual(merged.Rules[0].Id, merged.Rules[1].Id);

            // Replace on RulesOnly backup replaces rules while preserving local machine settings
            var replacedRulesOnly = ConfigBackupService.ApplyImport(current, parsed, replaceAll: true);
            Assert.Single(replacedRulesOnly.Rules);
            Assert.Equal("github.com", replacedRulesOnly.Rules[0].Pattern);
            Assert.Equal(GlobalMode.ClipboardOnly, replacedRulesOnly.GlobalMode);
            Assert.Equal(ThemeMode.Dark, replacedRulesOnly.Theme);
            Assert.Equal("ChromeHTML", replacedRulesOnly.OriginalDefaultProgId);
        }

        [Fact]
        public void CliParserAndHandler_DoNotSwallowFollowingFlags_AndEmitConsistentJson()
        {
            var invExport = CliParser.Parse(new[] { "--export", "--json" });
            Assert.Equal(CliKind.ScriptCommand, invExport.Kind);
            Assert.Equal("export", invExport.Command);
            Assert.True(invExport.Json);
            Assert.Equal(string.Empty, invExport.FileArg);

            var cfgDir = Path.Combine(_tempDir, "cli-edge");
            Directory.CreateDirectory(cfgDir);
            var cfgSvc = new ConfigService(cfgDir, cfgDir);
            var logSvc = new LogService(Path.Combine(cfgDir, "logs"));
            var reg = new SystemRegistration(@"D:\Tools\LinkToBrowser.exe");

            var swExport = new StringWriter();
            var exportCode = CliCommandHandler.Execute(invExport, cfgSvc, logSvc, reg, @"D:\Tools\LinkToBrowser.exe", swExport);
            Assert.Equal(2, exportCode);
            Assert.Contains("\"ok\":false", swExport.ToString());

            // --help does not advertise invalid 'recent' fallback
            var swHelp = new StringWriter();
            Assert.Equal(0, CliCommandHandler.Execute(CliParser.Parse(new[] { "--help", "--json" }), cfgSvc, logSvc, reg, @"D:\Tools\LinkToBrowser.exe", swHelp));
            Assert.DoesNotContain("default|recent|native", swHelp.ToString());

            // --rules add outputs camelCase string enums and rejects duplicate --id and separator-only pattern
            var swAdd = new StringWriter();
            Assert.Equal(0, CliCommandHandler.Execute(
                CliParser.Parse(new[] { "--rules", "add", "--id", "custom-gh", "--name", "公司GitHub", "--pattern", "github.com", "--action", "clipboard", "--json" }),
                cfgSvc, logSvc, reg, @"D:\Tools\LinkToBrowser.exe", swAdd));
            var addOut = swAdd.ToString();
            Assert.Contains("\"matchKind\":\"DomainSuffix\"", addOut);
            Assert.Contains("\"action\":\"ClipboardOnly\"", addOut);
            Assert.Contains("公司GitHub", addOut);

            var swDup = new StringWriter();
            Assert.Equal(2, CliCommandHandler.Execute(
                CliParser.Parse(new[] { "--rules", "add", "--id", "custom-gh", "--pattern", "example.com", "--action", "clipboard", "--json" }),
                cfgSvc, logSvc, reg, @"D:\Tools\LinkToBrowser.exe", swDup));
            Assert.Contains("already exists", swDup.ToString());

            var swSepOnly = new StringWriter();
            Assert.Equal(2, CliCommandHandler.Execute(
                CliParser.Parse(new[] { "--rules", "add", "--pattern", " , ; ", "--action", "clipboard", "--json" }),
                cfgSvc, logSvc, reg, @"D:\Tools\LinkToBrowser.exe", swSepOnly));
            Assert.Contains("\"ok\":false", swSepOnly.ToString());
        }
    }
}
