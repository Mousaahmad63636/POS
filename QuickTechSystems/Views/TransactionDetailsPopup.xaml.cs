using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace QuickTechSystems.Views
{
    public partial class TransactionDetailsPopup : Window
    {
        public TransactionDetailsPopup()
        {
            InitializeComponent();
        }

        private void EditBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        public void TriggerEditMode(object item, string columnName)
        {
            if (TransactionItemsGrid != null && item != null)
            {
                TransactionItemsGrid.SelectedItem = item;
                TransactionItemsGrid.UpdateLayout();

                var row = TransactionItemsGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row != null)
                {
                    var columnIndex = columnName switch
                    {
                        "Quantity" => 2,
                        "Discount" => 4,
                        _ => -1
                    };

                    if (columnIndex >= 0 && columnIndex < TransactionItemsGrid.Columns.Count)
                    {
                        var cell = DataGridHelper.GetCell(TransactionItemsGrid, row, columnIndex);
                        if (cell != null)
                        {
                            cell.Focus();
                            TransactionItemsGrid.BeginEdit();
                        }
                    }
                }
            }
        }
    }

    public static class DataGridHelper
    {
        public static DataGridCell GetCell(DataGrid dataGrid, DataGridRow row, int columnIndex)
        {
            if (row == null) return null;

            var presenter = GetVisualChild<DataGridCellsPresenter>(row);
            if (presenter == null) return null;

            var cell = presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
            if (cell == null)
            {
                dataGrid.ScrollIntoView(row, dataGrid.Columns[columnIndex]);
                cell = presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
            }
            return cell;
        }

        public static T GetVisualChild<T>(Visual parent) where T : Visual
        {
            T child = default(T);
            int numVisuals = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < numVisuals; i++)
            {
                var visual = (Visual)System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                child = visual as T ?? GetVisualChild<T>(visual);
                if (child != null)
                {
                    break;
                }
            }
            return child;
        }
    }
}