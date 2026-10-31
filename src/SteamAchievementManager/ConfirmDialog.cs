using System.Windows;
using System.Windows.Controls;

namespace SteamAchievementManager;

/// <summary>Small dark dialog in the app's style instead of the stock MessageBox.</summary>
public static class ConfirmDialog
{
    /// <summary>Returns the 1-based index of the chosen action, or 0 for cancel/close.</summary>
    public static int Show(Window? owner, string title, string text, string cancel, params (string Label, bool Danger)[] actions)
    {
        int result = 0;
        var window = new Window
        {
            Style = (Style)Application.Current.FindResource("AppWindow"),
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner is { IsLoaded: true } ? owner : null,
        };
        WindowTheme.Apply(window);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelButton = new Button { Content = cancel, IsCancel = true, Style = (Style)window.FindResource(actions.Length == 0 ? "PrimaryButton" : "QuietButton") };
        cancelButton.Click += (_, _) => window.Close();
        buttons.Children.Add(cancelButton);
        for (int i = 0; i < actions.Length; i++)
        {
            int index = i + 1;
            var button = new Button
            {
                Content = actions[i].Label,
                Margin = new Thickness(8, 0, 0, 0),
                Style = actions[i].Danger ? (Style)window.FindResource("DangerButton") : (Style)window.FindResource("PrimaryButton"),
            };
            button.Click += (_, _) => { result = index; window.Close(); };
            buttons.Children.Add(button);
        }

        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            MaxWidth = 440,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold },
                new TextBlock
                {
                    Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 22),
                    Foreground = (System.Windows.Media.Brush)window.FindResource("TextDimBrush"),
                },
                buttons,
            },
        };
        window.ShowDialog();
        return result;
    }
}
