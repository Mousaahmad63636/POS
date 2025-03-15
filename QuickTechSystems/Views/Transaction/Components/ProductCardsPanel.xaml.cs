using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class ProductCardsPanel : UserControl
    {
        public ProductCardsPanel()
        {
            InitializeComponent();
        }

        private void BarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is TransactionViewModel viewModel)
                {
                    viewModel.ProcessBarcodeCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void Product_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProductDTO product)
            {
                // Get the view model
                var viewModel = this.DataContext as TransactionViewModel;
                if (viewModel != null)
                {
                    // Add product directly to cart (with quantity 1)
                    // The AddProductToTransaction method will handle incrementing quantity if it's already in the cart
                    viewModel.AddProductToTransaction(product, 1);

                    // Provide visual feedback
                    AddClickFeedback(fe);
                }
            }
        }

        private void AddClickFeedback(FrameworkElement element)
        {
            // Create a quick animation for feedback
            var scaleTransform = new ScaleTransform(1, 1);
            element.RenderTransform = scaleTransform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            var animationDown = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.95,
                Duration = new Duration(TimeSpan.FromMilliseconds(100))
            };

            var animationUp = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.95,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(100)),
                BeginTime = TimeSpan.FromMilliseconds(100)
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animationDown);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animationDown);

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animationUp);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animationUp);
        }
    }
}