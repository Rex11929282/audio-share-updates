using System.Windows;
using AudioShare.Core;

namespace AudioShare.App;

public partial class ShareConfirmationWindow : Window
{
    public ShareConfirmationWindow(Window owner, ShareConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        InitializeComponent();
        Owner = owner;
        DataContext = confirmation;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
