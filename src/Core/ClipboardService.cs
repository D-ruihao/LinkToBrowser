using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace LinkToBrowser.Core
{
    public static class ClipboardService
    {
        public static bool CopyWithRetry(string text, int timeoutMs = 3000)
        {
            string backup = null;
            try
            {
                if (Clipboard.ContainsText())
                {
                    backup = Clipboard.GetText();
                }
            }
            catch
            {
            }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    Clipboard.SetDataObject(text, true, 5, 60);
                    if (Clipboard.ContainsText() && Clipboard.GetText() == text)
                    {
                        return true;
                    }
                }
                catch
                {
                }

                Thread.Sleep(60);
            }

            if (!string.IsNullOrEmpty(backup))
            {
                try
                {
                    Clipboard.SetDataObject(backup, true, 3, 60);
                }
                catch
                {
                }
            }

            return false;
        }
    }
}
