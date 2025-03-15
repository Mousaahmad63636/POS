using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace QuickTechSystems.WPF.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly DispatcherTimer _timer;

        public DashboardView()
        {
            InitializeComponent();

            // Initialize and configure timer
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Initial time update
            UpdateDateTime();

            // Register for cleanup
            this.Unloaded += UserControl_Unloaded;
            this.Loaded += UserControl_Loaded;
            this.SizeChanged += OnControlSizeChanged;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateDateTime();
        }

        private void UpdateDateTime()
        {
            try
            {
                var now = DateTime.Now;
                DateDisplay.Text = now.ToString("dddd, MMMM d, yyyy");
                TimeDisplay.Text = now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating date/time: {ex.Message}");
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }

            AdjustLayoutForSize();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
            _timer.Tick -= Timer_Tick;
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustLayoutForSize();
        }

        private void AdjustLayoutForSize()
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow == null) return;

            // Get actual window dimensions
            double windowWidth = parentWindow.ActualWidth;

            if (windowWidth >= 1920) // Large screens
            {
                RootGrid.Margin = new Thickness(24);
            }
            else if (windowWidth >= 1366) // Medium screens
            {
                RootGrid.Margin = new Thickness(16);
            }
            else if (windowWidth >= 800) // Small screens
            {
                RootGrid.Margin = new Thickness(8);
            }
            else // Very small screens
            {
                RootGrid.Margin = new Thickness(4);
            }
        }
    }
}