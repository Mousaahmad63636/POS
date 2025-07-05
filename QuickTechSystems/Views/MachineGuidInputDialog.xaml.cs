using System.Windows;

namespace QuickTechSystems.WPF.Views
{
    public partial class MachineGuidInputDialog : Window
    {
        public string EnteredMachineGuid { get; private set; } = "";

        public MachineGuidInputDialog()
        {
            InitializeComponent();
            // Do NOT pre-fill anything; user must enter it manually.
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            EnteredMachineGuid = PasswordBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(EnteredMachineGuid))
            {
                MessageBox.Show("Machine GUID cannot be empty.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Basic GUID format validation
            if (!System.Guid.TryParse(EnteredMachineGuid, out _))
            {
                MessageBox.Show("Please enter a valid Machine GUID format.", "Invalid Format",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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