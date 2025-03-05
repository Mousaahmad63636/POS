using System.Text.RegularExpressions;

namespace QuickTechSystems.WPF.Views
{
    public partial class MonthlySubscriptionView : UserControl
    {
        public MonthlySubscriptionView()
        {
            InitializeComponent();
        }
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[0-9]*\.?[0-9]*$");
            e.Handled = !regex.IsMatch(e.Text);
        }
        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            // You can add row loading logic here if needed
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }
    }
}