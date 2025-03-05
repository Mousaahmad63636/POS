// QuickTechSystems/Views/BulkProductDialog.xaml.cs
using System.Windows;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class BulkProductDialog : Window
    {
        private bool _isShown;

        public BulkProductDialog()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (!_isShown)
            {
                _isShown = true;
                // If no owner is set, try to set it to the main window
                if (Owner == null && System.Windows.Application.Current?.MainWindow != this)
                {
                    Owner = System.Windows.Application.Current.MainWindow;
                }
            }
        }

        public new bool? ShowDialog()
        {
            // Ensure we have an owner before showing the dialog
            if (Owner == null && System.Windows.Application.Current?.MainWindow != null && System.Windows.Application.Current.MainWindow != this)
            {
                Owner = System.Windows.Application.Current.MainWindow;
            }
            return base.ShowDialog();
        }
    }
}