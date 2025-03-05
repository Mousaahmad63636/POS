using System.Windows.Controls;

namespace QuickTechSystems.WPF.Controls
{
    public partial class EmployeeEditorControl : UserControl
    {
        public EmployeeEditorControl()
        {
            InitializeComponent();
        }

        // Add these public methods to directly access the password values
        public string GetNewEmployeePassword()
        {
            return NewEmployeePasswordBox?.Password ?? string.Empty;
        }

        public string GetResetPassword()
        {
            return ResetPasswordBox?.Password ?? string.Empty;
        }

        public string GetConfirmPassword()
        {
            return ConfirmPasswordBox?.Password ?? string.Empty;
        }
    }
}