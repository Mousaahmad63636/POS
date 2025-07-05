using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Views
{
    /// <summary>
    /// Interaction logic for ExpenseView.xaml
    /// </summary>
    public partial class ExpenseView : UserControl
    {
        public ExpenseView()
        {
            InitializeComponent();
        }
    }

    /// <summary>
    /// Converter to convert expense ID to appropriate title text
    /// </summary>
    public class ExpenseIdToTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int expenseId)
            {
                return expenseId == 0 ? "💸 Add New Expense" : "✏️ Edit Expense";
            }
            return "📝 Expense Details";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}