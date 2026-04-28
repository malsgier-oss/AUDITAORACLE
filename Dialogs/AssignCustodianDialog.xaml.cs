using System.Windows;

namespace WorkAudit.Dialogs;

public partial class AssignCustodianDialog : Window
{
    public Domain.User? SelectedCustodian { get; private set; }
    public bool ClearCustodian { get; private set; }

    public AssignCustodianDialog(int documentCount)
    {
        InitializeComponent();
        InfoText.Text = $"{documentCount} document(s) selected. Select custodian or click Clear to remove.";
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        SelectedCustodian = CustodianCombo.SelectedItem as Domain.User;
        ClearCustodian = false;
        DialogResult = true;
        Close();
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        SelectedCustodian = null;
        ClearCustodian = true;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
