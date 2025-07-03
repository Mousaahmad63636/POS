using System.Windows;

namespace QuickTechSystems.WPF.Views
{
    public partial class MacInputDialog : Window
    {
        public string EnteredMacAddress { get; private set; } = "";

        public MacInputDialog()
        {
            InitializeComponent();
            // Do NOT pre-fill anything; user must enter it manually.
        }


        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            EnteredMacAddress = PasswordBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(EnteredMacAddress))
            {
                MessageBox.Show("MAC address cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
