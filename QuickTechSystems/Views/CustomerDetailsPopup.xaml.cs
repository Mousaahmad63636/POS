// Path: QuickTechSystems.WPF.Views/CustomerDetailsPopup.xaml.cs
using System.Windows;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.Views
{
    public partial class CustomerDetailsPopup : UserControl
    {
        public static readonly RoutedEvent CloseRequestedEvent =
            EventManager.RegisterRoutedEvent("CloseRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(CustomerDetailsPopup));

        public static readonly RoutedEvent SaveCompletedEvent =
            EventManager.RegisterRoutedEvent("SaveCompleted", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(CustomerDetailsPopup));

        public event RoutedEventHandler CloseRequested
        {
            add { AddHandler(CloseRequestedEvent, value); }
            remove { RemoveHandler(CloseRequestedEvent, value); }
        }

        public event RoutedEventHandler SaveCompleted
        {
            add { AddHandler(SaveCompletedEvent, value); }
            remove { RemoveHandler(SaveCompletedEvent, value); }
        }

        public CustomerDetailsPopup()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(CloseRequestedEvent));
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Here we'd normally validate inputs

            // Then raise the save event
            RaiseEvent(new RoutedEventArgs(SaveCompletedEvent));
        }
    }
}