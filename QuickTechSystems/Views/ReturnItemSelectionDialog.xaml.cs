using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views
{
    public partial class ReturnItemSelectionDialog : Window
    {
        public class ReturnItem : INotifyPropertyChanged
        {
            private int _returnQuantity;
            private string _returnReason = string.Empty;
            private bool _isSelected;

            public string ProductName { get; set; } = string.Empty;
            public string ProductBarcode { get; set; } = string.Empty;
            public int ProductId { get; set; }
            public int OriginalQuantity { get; set; }
            public int ReturnedQuantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal PurchasePrice { get; set; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }

            public int ReturnQuantity
            {
                get => _returnQuantity;
                set
                {
                    int availableToReturn = OriginalQuantity - ReturnedQuantity;
                    if (value > availableToReturn) value = availableToReturn;
                    if (value < 0) value = 0;
                    _returnQuantity = value;
                    OnPropertyChanged(nameof(ReturnQuantity));
                    OnPropertyChanged(nameof(RefundAmount));
                }
            }

            public string ReturnReason
            {
                get => _returnReason;
                set
                {
                    _returnReason = value;
                    OnPropertyChanged(nameof(ReturnReason));
                }
            }

            public decimal RefundAmount => ReturnQuantity * UnitPrice;

            public event PropertyChangedEventHandler? PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public ObservableCollection<ReturnItem> Items { get; set; }
        public List<ReturnItemDTO> SelectedItems { get; private set; } = new();

        public ReturnItemSelectionDialog(TransactionDTO originalTransaction)
        {
            InitializeComponent();
            Items = new ObservableCollection<ReturnItem>(
                originalTransaction.Details.Select(item => new ReturnItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    ProductBarcode = item.ProductBarcode,
                    OriginalQuantity = item.Quantity,
                    ReturnedQuantity = item.ReturnedQuantity,
                    UnitPrice = item.UnitPrice,
                    PurchasePrice = item.PurchasePrice,
                    ReturnQuantity = 0
                }));
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = Items.Where(i => i.IsSelected && i.ReturnQuantity > 0)
                .Select(i => new ReturnItemDTO
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    OriginalQuantity = i.OriginalQuantity,
                    QuantityToReturn = i.ReturnQuantity,
                    UnitPrice = i.UnitPrice,
                    ReturnReason = i.ReturnReason
                })
                .ToList();

            if (!selectedItems.Any())
            {
                MessageBox.Show("Please select at least one item to return",
                    "No Items Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedItems = selectedItems;
            DialogResult = true;
        }
    }
}