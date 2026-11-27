using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using LinkToBrowser.Models;
using Microsoft.Win32;

namespace LinkToBrowser.Core
{
    public static class BrowserCatalog
    {
        private static readonly (string HivePath, RegistryKey Root)[] StartMenuRoots =
        {
            (@"SOFTWARE\Clients\StartMenuInternet", Registry.CurrentUser),
            (@"SOFTWARE\Clients\StartMenuInternet", Registry.LocalMachine),
            (@"SOFTWARE\WOW6432Node\Clients\StartMenuInternet", Registry.LocalMachine)
        };

        private static readonly Dictionary<string, string> ChromiumUserDataRelativePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome.exe", @"Google\Chrome\User Data\Local State" },
            { "msedge.exe", @"Microsoft\Edge\User Data\Local State" },
            { "brave.exe", @"BraveSoftware\Brave-Browser\User Data\Local State" },
            { "vivaldi.exe", @"Vivaldi\User Data\Local State" }
        };

        public static string ResolveProgIdCommand(string progId)
        {
            if (string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

            var subkey = $@"Software\Classes\{progId}\shell\open\command";
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var key = root.OpenSubKey(subkey);
                    var val = key?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public static List<BrowserEntry> DiscoverRegistry(string selfExePath)
        {
            var results = new List<BrowserEntry>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalizedSelf = NormalizePath(selfExePath);

            foreach (var (path, root) in StartMenuRoots)
            {
                try
                {
                    using var baseKey = root.OpenSubKey(path);
                    if (baseKey == null)
                    {
                        continue;
                    }

                    foreach (var subName in baseKey.GetSubKeyNames())
                    {
                        if (string.Equals(subName, "CopyURLOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        using var appKey = baseKey.OpenSubKey(subName);
                        if (appKey == null)
                        {
                            continue;
                        }

                        using var assocKey = appKey.OpenSubKey(@"Capabilities\URLAssociations");
                        var progId = FirstNonWhiteSpace(
                            assocKey?.GetValue("https") as string,
                            assocKey?.GetValue("http") as string);
                        if (string.IsNullOrWhiteSpace(progId) ||
                            string.Equals(progId, "LinkToBrowser.URL", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(progId, "LinkClip.URL", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var cmd = ResolveProgIdCommand(progId);
                        if (!CommandLineHelper.TrySplit(cmd, File.Exists, out var exe, out var args))
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(normalizedSelf) &&
                            string.Equals(NormalizePath(exe), normalizedSelf, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var id = BrowserEntry.MakeId(BrowserKind.Registry, exe, args);
                        if (!seenIds.Add(id))
                        {
                            continue;
                        }

                        using var capKey = appKey.OpenSubKey("Capabilities");
                        var name = FirstNonWhiteSpace(
                            capKey?.GetValue("ApplicationName") as string,
                            appKey.GetValue(null) as string,
                            subName);

                        results.Add(new BrowserEntry
                        {
                            Id = id,
                            Name = name.Trim(),
                            ExecutablePath = exe,
                            ArgumentsTemplate = args,
                            ProgId = progId,
                            Kind = BrowserKind.Registry
                        });
                    }
                }
                catch
                {
                }
            }

            return results;
        }

        public static List<BrowserEntry> DiscoverChromium(IEnumerable<BrowserEntry> registryEntries)
        {
            var results = new List<BrowserEntry>();
            if (registryEntries == null)
            {
                return results;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var seenLocalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in registryEntries)
            {
                var fileName = Path.GetFileName(entry.ExecutablePath ?? string.Empty);
                if (string.IsNullOrEmpty(fileName) || !ChromiumUserDataRelativePaths.TryGetValue(fileName, out var relPath))
                {
                    continue;
                }

                var localStatePath = Path.Combine(localAppData, relPath);
                if (!seenLocalStates.Add(localStatePath) || !File.Exists(localStatePath))
                {
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(localStatePath);
                    results.AddRange(ParseChromiumProfiles(entry.Name, entry.ExecutablePath, json));
                }
                catch
                {
                }
            }

            return results;
        }

        public static List<BrowserEntry> ParseChromiumProfiles(string name, string exe, string localStateJson)
        {
            var list = new List<BrowserEntry>();
            if (string.IsNullOrWhiteSpace(localStateJson))
            {
                return list;
            }

            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(localStateJson);
                if (root == null || !root.TryGetValue("profile", out var profileObj) || profileObj is not Dictionary<string, object> profile)
                {
                    return list;
                }

                if (!profile.TryGetValue("info_cache", out var cacheObj) || cacheObj is not Dictionary<string, object> infoCache || infoCache.Count < 2)
                {
                    return list;
                }

                foreach (var kv in infoCache)
                {
                    var dirKey = kv.Key;
                    var profileName = dirKey;
                    if (kv.Value is Dictionary<string, object> meta &&
                        meta.TryGetValue("name", out var rawName) &&
                        rawName is string s &&
                        !string.IsNullOrWhiteSpace(s))
                    {
                        profileName = s.Trim();
                    }

                    var args = $"--profile-directory=\"{dirKey}\" {{url}}";
                    list.Add(new BrowserEntry
                    {
                        Id = BrowserEntry.MakeId(BrowserKind.ChromiumProfile, exe, args),
                        Name = $"{name} · {profileName}",
                        ExecutablePath = exe,
                        ArgumentsTemplate = args,
                        Kind = BrowserKind.ChromiumProfile
                    });
                }

                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
            }

            return list;
        }

        public static List<BrowserEntry> Arrange(IEnumerable<BrowserEntry> all, AppConfig cfg, bool includeHidden)
        {
            var config = cfg ?? new AppConfig();
            var hidden = new HashSet<string>(config.HiddenBrowserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var orderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (config.BrowserOrder != null)
            {
                for (var i = 0; i < config.BrowserOrder.Count; i++)
                {
                    var id = config.BrowserOrder[i];
                    if (!string.IsNullOrEmpty(id) && !orderIndex.ContainsKey(id))
                    {
                        orderIndex[id] = i;
                    }
                }
            }

            var deduped = new List<BrowserEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (all != null)
            {
                foreach (var item in all)
                {
                    if (item == null || string.IsNullOrEmpty(item.Id) || !seen.Add(item.Id))
                    {
                        continue;
                    }

                    if (!includeHidden && hidden.Contains(item.Id))
                    {
                        continue;
                    }

                    deduped.Add(item);
                }
            }

            deduped.Sort((a, b) =>
            {
                var hasA = orderIndex.TryGetValue(a.Id, out var idxA);
                var hasB = orderIndex.TryGetValue(b.Id, out var idxB);
                if (hasA && hasB) return idxA.CompareTo(idxB);
                if (hasA) return -1;
                if (hasB) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return deduped;
        }

        public static List<BrowserEntry> GetAll(AppConfig cfg, string selfExePath, bool includeHidden = true)
        {
            var config = cfg ?? new AppConfig();
            var collected = new List<BrowserEntry>();
            var reg = DiscoverRegistry(selfExePath);
            collected.AddRange(reg);

            if (config.ShowChromiumProfiles)
            {
                collected.AddRange(DiscoverChromium(reg));
            }

            if (config.CustomBrowsers != null)
            {
                foreach (var c in config.CustomBrowsers)
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.ExecutablePath))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(c.Id))
                    {
                        c.Id = BrowserEntry.MakeId(BrowserKind.Custom, c.ExecutablePath, c.ArgumentsTemplate);
                    }

                    c.Kind = BrowserKind.Custom;
                    collected.Add(c);
                }
            }

            return Arrange(collected, config, includeHidden);
        }

        public static List<BrowserEntry> GetAllWithRunning(AppConfig cfg, string selfExePath, bool includeHidden = true)
        {
            var config = cfg ?? new AppConfig();
            var baseEntries = GetAll(config, selfExePath, includeHidden);
            var running = RecentBrowserService.GetRunningIsolatedBrowsers(config, selfExePath, includeHidden);
            return MergeWithRunning(baseEntries, running, config, includeHidden);
        }

        public static List<BrowserEntry> MergeWithRunning(
            IReadOnlyList<BrowserEntry> baseEntries,
            IReadOnlyList<BrowserEntry> runningEntries,
            AppConfig cfg,
            bool includeHidden = true)
        {
            var config = cfg ?? new AppConfig();
            if (runningEntries == null || runningEntries.Count == 0)
            {
                return baseEntries != null ? new List<BrowserEntry>(baseEntries) : new List<BrowserEntry>();
            }

            // Place newly discovered active isolated windows before static entries unless already explicitly ordered
            var combined = new List<BrowserEntry>(runningEntries.Count + (baseEntries?.Count ?? 0));
            combined.AddRange(runningEntries);
            if (baseEntries != null)
            {
                combined.AddRange(baseEntries);
            }

            var orderedIds = new HashSet<string>(config.BrowserOrder ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var unorderedRunning = new List<BrowserEntry>();
            var rest = new List<BrowserEntry>();
            var hidden = new HashSet<string>(config.HiddenBrowserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in combined)
            {
                if (item == null || string.IsNullOrEmpty(item.Id) || !seen.Add(item.Id))
                {
                    continue;
                }

                if (!includeHidden && hidden.Contains(item.Id))
                {
                    continue;
                }

                if (item.Kind == BrowserKind.RunningWindow && !orderedIds.Contains(item.Id))
                {
                    unorderedRunning.Add(item);
                }
                else
                {
                    rest.Add(item);
                }
            }

            var arrangedRest = Arrange(rest, config, includeHidden);
            var final = new List<BrowserEntry>(unorderedRunning.Count + arrangedRest.Count);
            final.AddRange(unorderedRunning);
            final.AddRange(arrangedRest);
            return final;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                return path.Trim();
            }
        }

        private static string FirstNonWhiteSpace(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v.Trim();
                }
            }

            return string.Empty;
        }
    }
}
