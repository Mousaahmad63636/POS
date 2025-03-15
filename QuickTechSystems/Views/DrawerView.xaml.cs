using System.Windows.Controls;

namespace QuickTechSystems.WPF.Views
{
    public partial class DrawerView : UserControl
    {
        public DrawerView()
        {
            InitializeComponent();
            this.Loaded += DrawerView_Loaded;
        }

        private async void DrawerView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is DrawerViewModel viewModel)
            {
                await viewModel.RefreshDrawerDataAsync();
                viewModel.LoadFinancialDataCommand.Execute(null);
            }
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var row = e.Row;
            var item = row.DataContext as DrawerTransactionDTO;
            if (item != null)
            {
                switch (item.ActionType.ToLower())
                {
                    case "cash sale":
                    case "debt payment":
                        row.Background = (System.Windows.Media.Brush)FindResource("SuccessColor");
                        break;
                    case "return":
                    case "expense":
                    case "supplier payment":
                        row.Background = (System.Windows.Media.Brush)FindResource("DangerColor");
                        break;
                }
            }
        }
    }
}