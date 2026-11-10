using Microsoft.Win32;

namespace NTech.DataStudio.Desktop.Services;

public interface IFilePicker
{
    /// <summary>The chosen file, or null when the user cancels.</summary>
    /// <param name="filter">Windows dialog filter, for example <c>ONNX (*.onnx)|*.onnx</c>.</param>
    string? Pick(string title, string filter, string? initialFolder = null);
}

public sealed class WpfFilePicker : IFilePicker
{
    public string? Pick(string title, string filter, string? initialFolder = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, InitialDirectory = initialFolder ?? string.Empty };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
