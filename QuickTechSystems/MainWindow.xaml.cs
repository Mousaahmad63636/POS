using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<MainViewModel>();
        }

        private void ToggleNavButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (ToggleButton)sender;
            NavColumn.Width = button.IsChecked == true ?
                new GridLength(250) : new GridLength(0);
        }
        private void TestOverlay_Click(object sender, RoutedEventArgs e)
        {
            // Show overlay directly to test
            var overlay = (Grid)FindName("PART_GlobalProductOverlay");
            var content = (ContentControl)FindName("PART_ProductEditorContent");

            if (overlay != null && content != null)
            {
                var editorControl = new Controls.ProductEditorControl();

                // Get ProductViewModel from DI container
                var productViewModel = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<ViewModels.ProductViewModel>();
                editorControl.DataContext = productViewModel;

                content.Content = editorControl;
                overlay.Visibility = Visibility.Visible;

                MessageBox.Show("Overlay shown directly from MainWindow");
            }
            else
            {
                MessageBox.Show("Could not find overlay controls");
            }
        }
    }
}