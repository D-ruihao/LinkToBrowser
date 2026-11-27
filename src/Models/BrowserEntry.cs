namespace LinkToBrowser.Models
{
    public enum BrowserKind
    {
        Registry,
        ChromiumProfile,
        Custom
    }

    public sealed class BrowserEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string ArgumentsTemplate { get; set; } = string.Empty;
        public string ProgId { get; set; } = string.Empty;
        public BrowserKind Kind { get; set; } = BrowserKind.Registry;

        public static string MakeId(BrowserKind kind, string exe, string args)
        {
            var normalizedExe = (exe ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedArgs = (args ?? string.Empty).Trim();
            return $"{kind}:{normalizedExe}|{normalizedArgs}";
        }
    }
}
