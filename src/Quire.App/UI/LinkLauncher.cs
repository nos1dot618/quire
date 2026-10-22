using System.Diagnostics;
using System.Windows;

namespace Quire.UI;

public static class LinkLauncher
{
    /// <summary>
    /// Opens a web or mail address in the default handler. Other schemes (file:, custom protocol
    /// handlers) are refused, since a link in a shared document could otherwise launch a program.
    /// </summary>
    public static bool Open(string? url)
    {
        if (Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }

        MessageBox.Show(Application.Current?.MainWindow!, "Only http, https and mailto links can be opened.", "Quire",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
}
