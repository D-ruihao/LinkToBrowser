using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LinkToBrowser.Core;
using LinkToBrowser.Models;
using Wpf.Ui.Controls;

namespace LinkToBrowser.Views
{
    public partial class RuleDialog : FluentWindow
    {
        private sealed class ComboItem<T>
        {
            public T Value { get; set; }
            public string Label { get; set; }
            public override string ToString() => Label;
        }

        private readonly string _existingId;
        private readonly bool _existingEnabled;

        public RoutingRule ResultRule { get; private set; }

        public RuleDialog(ThemeMode theme, IEnumerable<BrowserEntry> availableBrowsers, RoutingRule existing = null)
        {
            _existingId = existing?.Id;
            _existingEnabled = existing?.Enabled ?? true;

            L.Apply(L.IsEnglish ? LanguageMode.EnUs : LanguageMode.ZhCn, this);
            InitializeComponent();
            UiHelpers.ApplyTheme(this, theme);

            MatchKindCombo.ItemsSource = new[]
            {
                new ComboItem<RuleMatchKind> { Value = RuleMatchKind.DomainSuffix, Label = L.Get("S_RuleMatchDomain") },
                new ComboItem<RuleMatchKind> { Value = RuleMatchKind.UrlPrefix, Label = L.Get("S_RuleMatchPrefix") },
                new ComboItem<RuleMatchKind> { Value = RuleMatchKind.Wildcard, Label = L.Get("S_RuleMatchWildcard") }
            };

            ActionCombo.ItemsSource = new[]
            {
                new ComboItem<RuleActionKind> { Value = RuleActionKind.OpenTarget, Label = L.Get("S_RuleActOpenTarget") },
                new ComboItem<RuleActionKind> { Value = RuleActionKind.ClipboardOnly, Label = L.Get("S_RuleActClipboard") },
                new ComboItem<RuleActionKind> { Value = RuleActionKind.PickerPopup, Label = L.Get("S_RuleActPicker") },
                new ComboItem<RuleActionKind> { Value = RuleActionKind.RecentBrowser, Label = L.Get("S_RuleActRecent") },
                new ComboItem<RuleActionKind> { Value = RuleActionKind.NativePassthrough, Label = L.Get("S_RuleActNative") }
            };

            var choices = (availableBrowsers ?? Enumerable.Empty<BrowserEntry>()).ToList();
            if (existing?.TargetBrowser != null &&
                !choices.Any(b => string.Equals(b.Id, existing.TargetBrowser.Id, StringComparison.OrdinalIgnoreCase)))
            {
                choices.Insert(0, existing.TargetBrowser);
            }

            TargetBrowserCombo.ItemsSource = choices;

            if (existing != null)
            {
                NameBox.Text = existing.Name;
                PatternBox.Text = existing.Pattern;
                SourceAppsBox.Text = existing.SourceApps;
                SelectItem(MatchKindCombo, existing.MatchKind, RuleMatchKind.DomainSuffix);
                SelectItem(ActionCombo, existing.Action, RuleActionKind.OpenTarget);
                if (existing.TargetBrowser != null)
                {
                    TargetBrowserCombo.SelectedItem = choices.FirstOrDefault(b =>
                        string.Equals(b.Id, existing.TargetBrowser.Id, StringComparison.OrdinalIgnoreCase));
                }
            }
            else
            {
                SelectItem(MatchKindCombo, RuleMatchKind.DomainSuffix, RuleMatchKind.DomainSuffix);
                SelectItem(ActionCombo, RuleActionKind.OpenTarget, RuleActionKind.OpenTarget);
                if (choices.Count > 0)
                {
                    TargetBrowserCombo.SelectedIndex = 0;
                }
            }

            UpdateTargetVisibility();
        }

        private static void SelectItem<T>(ComboBox combo, T value, T fallback)
        {
            if (combo.ItemsSource is not IEnumerable<ComboItem<T>> items) return;
            var list = items.ToList();
            combo.SelectedItem = list.FirstOrDefault(i => EqualityComparer<T>.Default.Equals(i.Value, value))
                                 ?? list.FirstOrDefault(i => EqualityComparer<T>.Default.Equals(i.Value, fallback));
        }

        private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTargetVisibility();
        }

        private void UpdateTargetVisibility()
        {
            var isTarget = (ActionCombo.SelectedItem as ComboItem<RuleActionKind>)?.Value == RuleActionKind.OpenTarget;
            TargetBrowserPanel.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
                Close();
            }
        }

        private void PresetLocal_Click(object sender, RoutedEventArgs e)
        {
            SelectItem(MatchKindCombo, RuleMatchKind.DomainSuffix, RuleMatchKind.DomainSuffix);
            PatternBox.Text = "localhost, 127.0.0.1";
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = L.Get("S_RulePresetLocalName");
        }

        private void PresetGitHub_Click(object sender, RoutedEventArgs e)
        {
            SelectItem(MatchKindCombo, RuleMatchKind.DomainSuffix, RuleMatchKind.DomainSuffix);
            PatternBox.Text = "github.com, gist.github.com";
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = L.Get("S_RulePresetGitHubName");
        }

        private void PresetOAuth_Click(object sender, RoutedEventArgs e)
        {
            SelectItem(MatchKindCombo, RuleMatchKind.Wildcard, RuleMatchKind.Wildcard);
            PatternBox.Text = "*oauth*, */login/callback*";
            SelectItem(ActionCombo, RuleActionKind.ClipboardOnly, RuleActionKind.ClipboardOnly);
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = L.Get("S_RulePresetOAuthName");
        }

        private void PresetWeChat_Click(object sender, RoutedEventArgs e)
        {
            SourceAppsBox.Text = "WeChat.exe, Weixin.exe, QQ.exe";
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = "WeChat / QQ";
        }

        private void PresetVSCode_Click(object sender, RoutedEventArgs e)
        {
            SourceAppsBox.Text = "Code.exe, Cursor.exe";
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = "VS Code / Cursor";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var pattern = (PatternBox.Text ?? string.Empty).Trim();
            var sourceApps = (SourceAppsBox.Text ?? string.Empty).Trim();
            if (!RuleMatcher.HasValidTokens(pattern) && !RuleMatcher.HasValidTokens(sourceApps))
            {
                ErrorBar.Message = L.Get("S_RuleErrPattern");
                ErrorBar.IsOpen = true;
                return;
            }

            var matchKind = (MatchKindCombo.SelectedItem as ComboItem<RuleMatchKind>)?.Value ?? RuleMatchKind.DomainSuffix;
            var action = (ActionCombo.SelectedItem as ComboItem<RuleActionKind>)?.Value ?? RuleActionKind.OpenTarget;
            BrowserEntry target = null;

            if (action == RuleActionKind.OpenTarget)
            {
                if (TargetBrowserCombo.SelectedItem is not BrowserEntry selected)
                {
                    ErrorBar.Message = L.Get("S_RuleErrTarget");
                    ErrorBar.IsOpen = true;
                    return;
                }

                target = new BrowserEntry
                {
                    Id = selected.Id,
                    Name = selected.Name,
                    ExecutablePath = selected.ExecutablePath,
                    ArgumentsTemplate = selected.ArgumentsTemplate,
                    ProgId = selected.ProgId,
                    UserDataDir = selected.UserDataDir,
                    Kind = selected.Kind
                };
            }

            var name = (NameBox.Text ?? string.Empty).Trim();
            var fallbackName = !string.IsNullOrEmpty(pattern) ? pattern : sourceApps;
            ResultRule = new RoutingRule
            {
                Id = string.IsNullOrEmpty(_existingId) ? $"rule:{Guid.NewGuid():N}" : _existingId,
                Enabled = _existingEnabled,
                Name = string.IsNullOrEmpty(name) ? fallbackName : name,
                MatchKind = matchKind,
                Pattern = pattern,
                SourceApps = sourceApps,
                Action = action,
                TargetBrowser = target
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
