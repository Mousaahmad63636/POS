using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickTechSystems.Application.DTOs
{
    public class SupplierInvoiceDetailDTO : INotifyPropertyChanged
    {
        private int _supplierInvoiceDetailId;
        private int _supplierInvoiceId;
        private int _productId;
        private string _productName = string.Empty;
        private string _productBarcode = string.Empty;
        private decimal _quantity;
        private decimal _purchasePrice;
        private decimal _totalPrice;
        private string _boxBarcode = string.Empty;
        private int _numberOfBoxes;
        private int _itemsPerBox = 1;
        private decimal _boxPurchasePrice;
        private decimal _boxSalePrice;
        private decimal _currentStock;
        private decimal _storehouse;
        private decimal _salePrice;
        private decimal _wholesalePrice;
        private decimal _boxWholesalePrice;
        private int _minimumStock;
        private string _categoryName = string.Empty;
        private string _supplierName = string.Empty;

        public int SupplierInvoiceDetailId
        {
            get => _supplierInvoiceDetailId;
            set => SetProperty(ref _supplierInvoiceDetailId, value);
        }

        public int SupplierInvoiceId
        {
            get => _supplierInvoiceId;
            set => SetProperty(ref _supplierInvoiceId, value);
        }

        public int ProductId
        {
            get => _productId;
            set => SetProperty(ref _productId, value);
        }

        public string ProductName
        {
            get => _productName;
            set => SetProperty(ref _productName, value);
        }

        public string ProductBarcode
        {
            get => _productBarcode;
            set => SetProperty(ref _productBarcode, value);
        }

        public decimal Quantity
        {
            get => _quantity;
            set
            {
                if (SetProperty(ref _quantity, value))
                {
                    UpdateTotalPrice();
                }
            }
        }

        public decimal PurchasePrice
        {
            get => _purchasePrice;
            set
            {
                if (SetProperty(ref _purchasePrice, value))
                {
                    UpdateTotalPrice();
                }
            }
        }

        public decimal TotalPrice
        {
            get => _totalPrice;
            set => SetProperty(ref _totalPrice, value);
        }

        public string BoxBarcode
        {
            get => _boxBarcode;
            set => SetProperty(ref _boxBarcode, value);
        }

        public int NumberOfBoxes
        {
            get => _numberOfBoxes;
            set => SetProperty(ref _numberOfBoxes, value);
        }

        public int ItemsPerBox
        {
            get => _itemsPerBox;
            set => SetProperty(ref _itemsPerBox, value);
        }

        public decimal BoxPurchasePrice
        {
            get => _boxPurchasePrice;
            set => SetProperty(ref _boxPurchasePrice, value);
        }

        public decimal BoxSalePrice
        {
            get => _boxSalePrice;
            set => SetProperty(ref _boxSalePrice, value);
        }

        public decimal CurrentStock
        {
            get => _currentStock;
            set => SetProperty(ref _currentStock, value);
        }

        public decimal Storehouse
        {
            get => _storehouse;
            set => SetProperty(ref _storehouse, value);
        }

        public decimal SalePrice
        {
            get => _salePrice;
            set => SetProperty(ref _salePrice, value);
        }

        public decimal WholesalePrice
        {
            get => _wholesalePrice;
            set => SetProperty(ref _wholesalePrice, value);
        }

        public decimal BoxWholesalePrice
        {
            get => _boxWholesalePrice;
            set => SetProperty(ref _boxWholesalePrice, value);
        }

        public int MinimumStock
        {
            get => _minimumStock;
            set => SetProperty(ref _minimumStock, value);
        }

        public string CategoryName
        {
            get => _categoryName;
            set => SetProperty(ref _categoryName, value);
        }

        public string SupplierName
        {
            get => _supplierName;
            set => SetProperty(ref _supplierName, value);
        }

        // Calculated properties
        public decimal AvailableBoxes => ItemsPerBox > 0 ? Math.Floor(Storehouse / ItemsPerBox) : 0;
        public decimal ItemPurchasePrice => ItemsPerBox > 0 && ItemsPerBox != 1 ? BoxPurchasePrice / ItemsPerBox : PurchasePrice;
        public decimal ItemWholesalePrice => ItemsPerBox > 0 && ItemsPerBox != 1 ? BoxWholesalePrice / ItemsPerBox : WholesalePrice;

        public string StockStatus => CurrentStock <= MinimumStock ? "Low Stock" : "Normal";
        public string StorehouseStatus => Storehouse <= 0 ? "Empty" : "Available";

        public decimal TotalInventory => CurrentStock + Storehouse;
        public decimal EquivalentBoxes => ItemsPerBox > 0 ? Math.Floor(TotalInventory / ItemsPerBox) : 0;
        public string QuantityBreakdown => $"Stock: {CurrentStock}, Warehouse: {Storehouse}";

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void UpdateTotalPrice()
        {
            TotalPrice = Quantity * PurchasePrice;
        }
    }
}