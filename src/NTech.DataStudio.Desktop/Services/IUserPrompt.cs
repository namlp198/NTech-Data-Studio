using System.Windows;

namespace NTech.DataStudio.Desktop.Services;

public interface IUserPrompt
{
    bool Confirm(string message);
}

public sealed class MessageBoxPrompt : IUserPrompt
{
    public bool Confirm(string message) =>
        MessageBox.Show(message, "NTech Data Studio", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
