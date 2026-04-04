using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoView.Dialogs;

public enum DeleteType
{
    Jpg,
    Raw,
    All
}

public sealed partial class DeleteConfirmDialog : ContentDialog
{
    public DeleteType DeleteType { get; }
    public int FileCount { get; }
    public bool IsDeleting { get; private set; }

    public DeleteConfirmDialog(DeleteType deleteType, int fileCount)
    {
        DeleteType = deleteType;
        FileCount = fileCount;
        InitializeComponent();
        
        UpdateContent();
    }

    private void UpdateContent()
    {
        var typeText = DeleteType switch
        {
            DeleteType.Jpg => "JPG 文件",
            DeleteType.Raw => "RAW 文件",
            DeleteType.All => "所有标记文件",
            _ => "文件"
        };

        Title = $"确认删除 {typeText}";
        ContentText.Text = $"将删除 {FileCount} 个文件到回收站，此操作不可撤销。";
    }

    public void StartProgress()
    {
        IsDeleting = true;
        ProgressPanel.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
    }

    public void SetProgress(int current, int total)
    {
        DeleteProgressBar.Value = current;
        DeleteProgressBar.Maximum = total;
        ProgressText.Text = $"正在删除: {current}/{total}";
    }

    public void SetComplete()
    {
        IsDeleting = false;
        ProgressText.Text = "删除完成";
        DeleteProgressBar.Value = DeleteProgressBar.Maximum;
    }
}
