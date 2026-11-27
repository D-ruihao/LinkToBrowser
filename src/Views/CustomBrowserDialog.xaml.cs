using System;
using System.IO;
using System.Windows;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace LinkToBrowser.Views
{
    public partial class CustomBrowserDialog : FluentWindow
    {
        private readonly string _selfExe;
        private readonly string _existingId;

        public BrowserEntry ResultEntry { get; private set; }

        public CustomBrowserDialog(ThemeMode theme, string selfExe, BrowserEntry existing = null)
        {
            _selfExe = selfExe;
            _existingId = existing?.Id;
            InitializeComponent();
            L.Apply(L.IsEnglish ? LanguageMode.EnUs : LanguageMode.ZhCn, this);
            UiHelpers.ApplyTheme(this, theme);

            if (existing != null)
            {
                NameBox.Text = existing.Name;
                ExeBox.Text = existing.ExecutablePath;
                ArgsBox.Text = existing.ArgumentsTemplate;
            }
        }

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = L.Get("S_CustomFileFilter"),
                Title = L.Get("S_CustomFileTitle")
            };
            if (dlg.ShowDialog(this) == true)
            {
                ExeBox.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                {
                    NameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
                }
            }
        }

        private void PresetProfile_Click(object sender, RoutedEventArgs e)
        {
            ArgsBox.Text = "--profile-directory=\"Profile 1\" {url}";
        }

        private void PresetIncognito_Click(object sender, RoutedEventArgs e)
        {
            ArgsBox.Text = "--incognito {url}";
        }

        private void TestLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildEntry(out var entry, out var error))
            {
                ShowError(error);
                return;
            }

            var launcher = new BrowserLauncher(_selfExe);
            if (!launcher.TryLaunch(entry.ExecutablePath, entry.ArgumentsTemplate, "https://example.com", out var launchErr))
            {
                ShowError(launchErr);
                return;
            }

            ErrorBar.IsOpen = false;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildEntry(out var entry, out var error))
            {
                ShowError(error);
                return;
            }

            ResultEntry = entry;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool TryBuildEntry(out BrowserEntry entry, out string error)
        {
            entry = null;
            var name = (NameBox.Text ?? string.Empty).Trim();
            var exe = (ExeBox.Text ?? string.Empty).Trim().Trim('"');
            var args = (ArgsBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                error = L.Get("S_CustomErrName");
                return false;
            }

            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                error = L.Get("S_CustomErrExe");
                return false;
            }

            if (new BrowserLauncher(_selfExe).IsSelfPath(exe))
            {
                error = L.Get("S_CustomErrSelf");
                return false;
            }

            entry = new BrowserEntry
            {
                Id = string.IsNullOrEmpty(_existingId) ? $"custom:{Guid.NewGuid():N}" : _existingId,
                Name = name,
                ExecutablePath = exe,
                ArgumentsTemplate = args,
                Kind = BrowserKind.Custom
            };
            error = string.Empty;
            return true;
        }

        private void ShowError(string message)
        {
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }
    }
}
