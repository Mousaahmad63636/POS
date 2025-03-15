// QuickTechSystems/Views/BulkProductDialog.xaml.cs
using System.Diagnostics;
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
                if (Owner == null)
                {
                    try
                    {
                        var mainWindow = System.Windows.Application.Current.MainWindow;
                        if (mainWindow != null && mainWindow != this && mainWindow.IsLoaded)
                        {
                            Owner = mainWindow;
                        }
                        else
                        {
                            // Try to find another suitable owner window
                            var ownerWindow = System.Windows.Application.Current.Windows.OfType<Window>()
                                .FirstOrDefault(w => w != this && w.IsVisible);
                            if (ownerWindow != null)
                            {
                                Owner = ownerWindow;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error setting window owner: {ex}");
                    }
                }
            }
        }

        public new bool? ShowDialog()
        {
            try
            {
                // Ensure we have an owner before showing the dialog
                if (Owner == null)
                {
                    var mainWindow = System.Windows.Application.Current.MainWindow;
                    if (mainWindow != null && mainWindow != this && mainWindow.IsLoaded)
                    {
                        Owner = mainWindow;
                    }
                    else
                    {
                        // Try to find another suitable owner window
                        var ownerWindow = System.Windows.Application.Current.Windows.OfType<Window>()
                            .FirstOrDefault(w => w != this && w.IsVisible);
                        if (ownerWindow != null)
                        {
                            Owner = ownerWindow;
                        }
                    }
                }

                if (WindowStartupLocation == WindowStartupLocation.CenterOwner && Owner == null)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                return base.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing dialog: {ex}");
                MessageBox.Show(
                    $"Error showing bulk product dialog: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }
    }
}