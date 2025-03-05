using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views
{
    public partial class TransactionDetailWindow : Window
    {
        public TransactionDetailWindow(TransactionDTO transaction)
        {
            InitializeComponent();

            if (transaction == null)
            {
                MessageBox.Show("No transaction data available.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            try
            {
                DataContext = transaction;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading transaction details: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }
    }
}
