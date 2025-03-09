using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.WPF.ViewModels;
using System.Windows.Controls;
using System.Linq;

namespace QuickTechSystems.WPF
{
    public partial class MainWindow : Window
    {
        private const double EXPANDED_NAV_WIDTH = 280;
        private const double COLLAPSED_NAV_WIDTH = 0;
        private const double ANIMATION_DURATION = 0.3;
        private bool isSidebarVisible = true;
        private DateTime lastButtonClickTime = DateTime.MinValue;
        private const int BUTTON_COOLDOWN_MS = 300;
        private Window? floatingButton;
        private Grid? mainGrid;
        private ToggleButton? toggleButton;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<MainViewModel>();
            InitializeControls();
            SetupWindow();
        }

        private void InitializeControls()
        {
            mainGrid = this.FindName("MainGrid") as Grid;
            toggleButton = this.FindName("ToggleNavButton") as ToggleButton;

            if (mainGrid == null || toggleButton == null)
            {
                throw new Exception("Critical UI elements not found");
            }
        }

        private void SetupWindow()
        {
            // Set window size based on screen resolution
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            // Adjust for different screen sizes
            if (screenWidth >= 1920) // Large screens
            {
                Width = 1400;
                Height = 900;
            }
            else if (screenWidth >= 1366) // Medium screens
            {
                Width = 1200;
                Height = 800;
            }
            else // Small screens
            {
                Width = 1000;
                Height = 700;
            }

            // If the screen is small, adjust window to fit screen
            if (SystemParameters.PrimaryScreenHeight < 800)
            {
                Height = SystemParameters.PrimaryScreenHeight * 0.9;
                Width = SystemParameters.PrimaryScreenWidth * 0.9;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (mainGrid?.ColumnDefinitions.Count > 0)
            {
                var navColumn = mainGrid.ColumnDefinitions[0];
                navColumn.Width = new GridLength(EXPANDED_NAV_WIDTH);
                UpdateLayout();
            }
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (mainGrid != null)
            {
                mainGrid.Margin = WindowState == WindowState.Maximized ?
                    new Thickness(8) : new Thickness(0);
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState != WindowState.Maximized)
            {
                if (ActualWidth < MinWidth) Width = MinWidth;
                if (ActualHeight < MinHeight) Height = MinHeight;
            }
        }

        private void ToggleNavButton_Click(object sender, RoutedEventArgs e)
        {
            if ((DateTime.Now - lastButtonClickTime).TotalMilliseconds < BUTTON_COOLDOWN_MS)
                return;

            lastButtonClickTime = DateTime.Now;

            try
            {
                var button = (ToggleButton)sender;
                ToggleSidebar(button);
                AnimateButtonRotation(button);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error toggling navigation: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleSidebar(ToggleButton button)
        {
            if (mainGrid?.ColumnDefinitions.Count == 0) return;

            var navColumn = mainGrid.ColumnDefinitions[0];
            var navPanel = mainGrid.Children.OfType<Border>().FirstOrDefault(b => b.Name == "NavigationPanel");

            if (navPanel == null) return;

            if (isSidebarVisible)
            {
                // Hiding sidebar
                var hideAnimation = new GridLengthAnimation
                {
                    From = navColumn.Width,
                    To = new GridLength(COLLAPSED_NAV_WIDTH),
                    Duration = TimeSpan.FromSeconds(ANIMATION_DURATION)
                };

                hideAnimation.Completed += (s, e) =>
                {
                    navPanel.Visibility = Visibility.Collapsed;
                    button.Visibility = Visibility.Hidden;
                    CreateFloatingButton(button);
                };

                navColumn.BeginAnimation(ColumnDefinition.WidthProperty, hideAnimation);
            }
            else
            {
                // Showing sidebar
                navPanel.Visibility = Visibility.Visible;
                button.Visibility = Visibility.Visible;

                var showAnimation = new GridLengthAnimation
                {
                    From = navColumn.Width,
                    To = new GridLength(EXPANDED_NAV_WIDTH),
                    Duration = TimeSpan.FromSeconds(ANIMATION_DURATION)
                };

                navColumn.BeginAnimation(ColumnDefinition.WidthProperty, showAnimation);

                if (floatingButton != null)
                {
                    floatingButton.Close();
                    floatingButton = null;
                }
            }

            isSidebarVisible = !isSidebarVisible;
        }

        private void CreateFloatingButton(ToggleButton originalButton)
        {
            var buttonPos = originalButton.PointToScreen(new Point(0, 0));

            floatingButton = new Window
            {
                Width = 44,
                Height = 44,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Left = buttonPos.X,
                Top = buttonPos.Y,
                ResizeMode = ResizeMode.NoResize
            };

            var newButton = new ToggleButton
            {
                Content = "≡",
                Width = 44,
                Height = 44,
                FontSize = 24,
                Background = (SolidColorBrush)Resources["SidebarBackground"],
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Template = originalButton.Template
            };

            newButton.Click += (s, e) =>
            {
                if ((DateTime.Now - lastButtonClickTime).TotalMilliseconds < BUTTON_COOLDOWN_MS)
                    return;

                lastButtonClickTime = DateTime.Now;

                // Simply call ToggleSidebar directly
                ToggleSidebar(originalButton);
            };

            floatingButton.Content = newButton;
            floatingButton.Show();
        }

        private void AnimateButtonRotation(ToggleButton button)
        {
            var rotateAnimation = new DoubleAnimation
            {
                From = button.IsChecked == true ? 0 : 180,
                To = button.IsChecked == true ? 180 : 0,
                Duration = TimeSpan.FromSeconds(ANIMATION_DURATION)
            };

            if (button.Template.FindName("rotation", button) is RotateTransform transform)
            {
                transform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            floatingButton?.Close();
            base.OnClosed(e);
        }
    }

    public class GridLengthAnimation : AnimationTimeline
    {
        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore()
        {
            return new GridLengthAnimation();
        }

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register("From", typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register("To", typeof(GridLength), typeof(GridLengthAnimation));

        public override object GetCurrentValue(object defaultOriginValue,
            object defaultDestinationValue,
            AnimationClock animationClock)
        {
            if (animationClock.CurrentProgress == null)
                return new GridLength(0);

            var fromValue = From.Value;
            var toValue = To.Value;
            var progress = animationClock.CurrentProgress.Value;

            var currentValue = fromValue + ((toValue - fromValue) * progress);
            return new GridLength(currentValue);
        }
    }
}