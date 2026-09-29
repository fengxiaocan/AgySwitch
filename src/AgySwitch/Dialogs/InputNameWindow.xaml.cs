using System.Windows;

namespace AgySwitch.Dialogs;

public partial class InputNameWindow : Window
{
    public InputNameWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        PromptText.Text = prompt;
        NameTextBox.Text = initialValue;
        NameTextBox.SelectAll();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public string Value => NameTextBox.Text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show(this, "账户名称不能为空。", "需要账户名称", MessageBoxButton.OK, MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
