using System.Collections.ObjectModel;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel
    {
        private string _statusMessage = string.Empty;
        private string _connectionStatus = "Connected";
        private string _cashierName = "Default Cashier";
        private string _terminalNumber = "001";
        private string _currentTransactionNumber = string.Empty;
        private TransactionDTO? _currentTransaction;
        private CustomerDTO? _selectedCustomer;
        private decimal _paymentAmount;
        private decimal _changeDue;
        private int _itemCount;
        private decimal _subTotal;
        private decimal _taxAmount;
        private decimal _discountAmount;
        private decimal _totalAmount;
        private string _barcodeText = string.Empty;
        private string _productSearchText = string.Empty;
        private string _customerSearchText = string.Empty;
        private bool _isCustomerSearchVisible;
        private ObservableCollection<CustomerDTO> _filteredCustomers = new();
        private CustomerDTO? _selectedCustomerFromSearch;
        private ObservableCollection<TransactionDTO> _heldTransactions = new();
        private ObservableCollection<ProductDTO> _filteredProducts = new();
        private bool _isProductSearchVisible;
        private ProductDTO? _selectedSearchProduct;
        private ProductDTO _selectedDropdownProduct;



        private ObservableCollection<ProductDTO> _allProducts = new();
        
       
        private string _dropdownSearchText = string.Empty;

        private string _totalAmountLBP = "0 LBP";

        public string TotalAmountLBP
        {
            get => _totalAmountLBP;
            set => SetProperty(ref _totalAmountLBP, value);
        }
        public ProductDTO? SelectedSearchProduct
        {
            get => _selectedSearchProduct;
            set
            {
                if (SetProperty(ref _selectedSearchProduct, value) && value != null)
                {
                    AddProductToTransaction(value);
                    ProductSearchText = string.Empty;
                    IsProductSearchVisible = false;
                }
            }
        }

        public ObservableCollection<ProductDTO> AllProducts
        {
            get => _allProducts;
            set => SetProperty(ref _allProducts, value);
        }
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public string CashierName
        {
            get => _cashierName;
            set => SetProperty(ref _cashierName, value);
        }

        public string TerminalNumber
        {
            get => _terminalNumber;
            set => SetProperty(ref _terminalNumber, value);
        }

        public string CurrentTransactionNumber
        {
            get => _currentTransactionNumber;
            set => SetProperty(ref _currentTransactionNumber, value);
        }

        public TransactionDTO? CurrentTransaction
        {
            get => _currentTransaction;
            set => SetProperty(ref _currentTransaction, value);
        }

        public CustomerDTO? SelectedCustomer
        {
            get => _selectedCustomer;
            set => SetProperty(ref _selectedCustomer, value);
        }

        public decimal PaymentAmount
        {
            get => _paymentAmount;
            set
            {
                if (SetProperty(ref _paymentAmount, value))
                {
                    UpdateChangeDue();
                }
            }
        }

        public decimal ChangeDue
        {
            get => _changeDue;
            set => SetProperty(ref _changeDue, value);
        }

        public int ItemCount
        {
            get => _itemCount;
            set => SetProperty(ref _itemCount, value);
        }

        public decimal SubTotal
        {
            get => _subTotal;
            set => SetProperty(ref _subTotal, value);
        }

        public decimal TaxAmount
        {
            get => _taxAmount;
            set => SetProperty(ref _taxAmount, value);
        }

        public decimal DiscountAmount
        {
            get => _discountAmount;
            set => SetProperty(ref _discountAmount, value);
        }

        public decimal TotalAmount
        {
            get => _totalAmount;
            set => SetProperty(ref _totalAmount, value);
        }

        public string BarcodeText
        {
            get => _barcodeText;
            set => SetProperty(ref _barcodeText, value);
        }

        public string ProductSearchText
        {
            get => _productSearchText;
            set
            {
                if (SetProperty(ref _productSearchText, value))
                {
                    SearchProducts();
                }
            }
        }

        public string CustomerSearchText
        {
            get => _customerSearchText;
            set
            {
                if (SetProperty(ref _customerSearchText, value))
                {
                    SearchCustomers();
                }
            }
        }

        public bool IsCustomerSearchVisible
        {
            get => _isCustomerSearchVisible;
            set => SetProperty(ref _isCustomerSearchVisible, value);
        }

        public ObservableCollection<CustomerDTO> FilteredCustomers
        {
            get => _filteredCustomers;
            set => SetProperty(ref _filteredCustomers, value);
        }

        public CustomerDTO? SelectedCustomerFromSearch
        {
            get => _selectedCustomerFromSearch;
            set
            {
                if (SetProperty(ref _selectedCustomerFromSearch, value) && value != null)
                {
                    SelectedCustomer = value;
                    CustomerSearchText = value.Name;
                    IsCustomerSearchVisible = false;
                }
            }
        }

        public ObservableCollection<TransactionDTO> HeldTransactions
        {
            get => _heldTransactions;
            set => SetProperty(ref _heldTransactions, value);
        }

        public ObservableCollection<ProductDTO> FilteredProducts
        {
            get => _filteredProducts;
            set => SetProperty(ref _filteredProducts, value);
        }

        public ProductDTO? SelectedDropdownProduct
        {
            get => _selectedDropdownProduct;
            set
            {
                if (SetProperty(ref _selectedDropdownProduct, value) && value != null)
                {
                    AddProductToTransaction(value);
                    _selectedDropdownProduct = null;
                    OnPropertyChanged(nameof(SelectedDropdownProduct));
                }
            }
        }
        public string DropdownSearchText
        {
            get => _dropdownSearchText;
            set
            {
                if (SetProperty(ref _dropdownSearchText, value))
                {
                    FilterProductsForDropdown(value);
                }
            }
        }
        private void FilterProductsForDropdown(string searchText)
        {
            // First, filter to only show Internet category products
            var internetProducts = _allProducts
                .Where(p => p.CategoryName?.Contains("Internet", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                FilteredProducts = new ObservableCollection<ProductDTO>(internetProducts);
                return;
            }

            var filteredList = internetProducts
                .Where(p => p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                            p.Barcode.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            FilteredProducts = new ObservableCollection<ProductDTO>(filteredList);
        }
        public bool IsProductSearchVisible
        {
            get => _isProductSearchVisible;
            set => SetProperty(ref _isProductSearchVisible, value);
        }
    }
}