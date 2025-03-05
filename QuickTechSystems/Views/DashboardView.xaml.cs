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

            // Initialize timer
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            // Corrected event handler name from *timer.Tick to _timer.Tick
            _timer.Tick += Timer_Tick;

            _timer.Start();

            // Initial update
            UpdateDateTime();

            // Add unloaded event handler
            this.Unloaded += UserControl_Unloaded;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateDateTime();
        }

        private void UpdateDateTime()
        {
            var now = DateTime.Now;
            DateDisplay.Text = now.ToString("dddd, MMMM d, yyyy");
            TimeDisplay.Text = now.ToString("HH:mm:ss");
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
        }
    }
}