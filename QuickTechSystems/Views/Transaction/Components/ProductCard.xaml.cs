using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views.Transaction.Components
{
    public partial class ProductCard : UserControl
    {
        public ProductCard()
        {
            InitializeComponent();

            // Make the entire card clickable
            this.MouseLeftButtonDown += OnCardClicked;
            this.Cursor = Cursors.Hand;
        }

        private void OnCardClicked(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ProductDTO product)
            {
                // Find the view model and add the product directly to the cart
                var viewModel = FindViewModel();
                if (viewModel != null)
                {
                    // Add product to cart with quantity 1 (it will increment if already in cart)
                    viewModel.AddProductToTransaction(product, 1);

                    // Add visual feedback for the click
                    AddVisualFeedback();
                }
            }
        }

        private void AddVisualFeedback()
        {
            // Create a quick animation to show the card was clicked
            var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.6,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(200))
            };

            this.BeginAnimation(UserControl.OpacityProperty, opacityAnimation);
        }

        private TransactionViewModel FindViewModel()
        {
            DependencyObject current = this;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.DataContext is TransactionViewModel vm)
                {
                    return vm;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void Cleanup()
        {
            this.MouseLeftButtonDown -= OnCardClicked;
        }

        ~ProductCard()
        {
            Cleanup();
        }
    }
}