using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickTechSystems.Application.DTOs
{
    public class TransactionDetailDTO : INotifyPropertyChanged
    {
        private int _transactionDetailId;
        private int _transactionId;
        private int _productId;
        private string _productName = string.Empty;
        private string _productBarcode = string.Empty;
        private int _quantity;
        private decimal _unitPrice;
        private decimal _purchasePrice;
        private decimal _discount;
        private decimal _total;
        private int _returnedQuantity;
        private bool _isReturned;
        private DateTime? _returnDate;
        private string? _returnReason;
        private bool _isSelected;
        private int _quantityToReturn;
        private bool _isSelectedForReturn;
        private int _categoryId;

        public int TransactionDetailId
        {
            get => _transactionDetailId;
            set
            {
                if (_transactionDetailId != value)
                {
                    _transactionDetailId = value;
                    OnPropertyChanged();
                }
            }
        }
        public int CategoryId
        {
            get => _categoryId;
            set
            {
                if (_categoryId != value)
                {
                    _categoryId = value;
                    OnPropertyChanged();
                }
            }
        }
        public int TransactionId
        {
            get => _transactionId;
            set
            {
                if (_transactionId != value)
                {
                    _transactionId = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ProductId
        {
            get => _productId;
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ProductName
        {
            get => _productName;
            set
            {
                if (_productName != value)
                {
                    _productName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ProductBarcode
        {
            get => _productBarcode;
            set
            {
                if (_productBarcode != value)
                {
                    _productBarcode = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    UpdateTotal(); // This will recalculate the total
                }
            }
        }
    
        

        public decimal UnitPrice
        {
            get => _unitPrice;
            set
            {
                if (_unitPrice != value)
                {
                    _unitPrice = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Total));  // Update Total when UnitPrice changes
                }
            }
        }

        public decimal PurchasePrice
        {
            get => _purchasePrice;
            set
            {
                if (_purchasePrice != value)
                {
                    _purchasePrice = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal Discount
        {
            get => _discount;
            set
            {
                if (_discount != value)
                {
                    _discount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Total));  // Update Total when Discount changes
                }
            }
        }

        public decimal Total
        {
            get => _total;
            set
            {
                if (_total != value)
                {
                    _total = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ReturnedQuantity
        {
            get => _returnedQuantity;
            set
            {
                if (_returnedQuantity != value)
                {
                    _returnedQuantity = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsReturned
        {
            get => _isReturned;
            set
            {
                if (_isReturned != value)
                {
                    _isReturned = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime? ReturnDate
        {
            get => _returnDate;
            set
            {
                if (_returnDate != value)
                {
                    _returnDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? ReturnReason
        {
            get => _returnReason;
            set
            {
                if (_returnReason != value)
                {
                    _returnReason = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public int QuantityToReturn
        {
            get => _quantityToReturn;
            set
            {
                if (_quantityToReturn != value)
                {
                    _quantityToReturn = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RefundAmount));  // Update RefundAmount when QuantityToReturn changes
                }
            }
        }

        public decimal RefundAmount => QuantityToReturn * UnitPrice;

        public bool IsSelectedForReturn
        {
            get => _isSelectedForReturn;
            set
            {
                if (_isSelectedForReturn != value)
                {
                    _isSelectedForReturn = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Helper method to calculate total
        public void UpdateTotal()
        {
            Total = (Quantity * UnitPrice) - Discount;
        }

        // Constructor
        public TransactionDetailDTO()
        {
            // Initialize any default values here
            ProductName = string.Empty;
            ProductBarcode = string.Empty;
            Quantity = 1;
            IsSelected = false;
            IsSelectedForReturn = false;
            QuantityToReturn = 0;
        }
    }
}