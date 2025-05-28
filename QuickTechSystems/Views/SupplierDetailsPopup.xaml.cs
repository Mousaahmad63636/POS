using System.Windows;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.Views
{
    public partial class SupplierDetailsPopup : UserControl
    {
        public event RoutedEventHandler CloseRequested;
        public event RoutedEventHandler SaveCompleted;

        public SupplierDetailsPopup()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCompleted?.Invoke(this, new RoutedEventArgs());
        }
    }
}