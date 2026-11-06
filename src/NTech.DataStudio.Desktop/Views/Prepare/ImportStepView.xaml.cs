using System.Windows;
using System.Windows.Controls;
using NTech.DataStudio.Desktop.ViewModels.Prepare;

namespace NTech.DataStudio.Desktop.Views.Prepare;

public partial class ImportStepView : UserControl
{
    public ImportStepView()
    {
        InitializeComponent();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;

        var folder = Directory.Exists(paths[0]) ? paths[0] : Path.GetDirectoryName(paths[0]);
        if (folder is not null && DataContext is ImportStepViewModel model)
            model.UseDroppedFolder(folder);
    }
}