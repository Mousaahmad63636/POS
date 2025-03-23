using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media.Imaging;
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
        private decimal _dailySales;
        private decimal _dailyReturns;
        private decimal _netSales;
        private decimal _supplierPayments;
        private decimal _dailyExpenses;
        private decimal _netCashflow;
        private ObservableCollection<ProductDTO> _allProducts = new();
        private string _dropdownSearchText = string.Empty;
        private string _totalAmountLBP = "0 LBP";
        // New properties for loading and validation
        private bool _isLoading;
        private string _loadingMessage = "Processing...";
        private Dictionary<string, string> _validationErrors = new();
        private bool _isProductCardsVisible;
        private DateTime _currentDate = DateTime.Now;
        private bool _isRestaurantMode;
        private CategoryDTO _selectedCategory;
        private string _lookupTransactionId = string.Empty;
        private bool _isEditingTransaction;
        private bool _isSearching;
        private string _searchMessage = string.Empty;
        private bool _isSearchMessageVisible;

        public bool IsSearching
        {
            get => _isSearching;
            set => SetProperty(ref _isSearching, value);
        }

        public string SearchMessage
        {
            get => _searchMessage;
            set => SetProperty(ref _searchMessage, value);
        }

        public bool IsSearchMessageVisible
        {
            get => _isSearchMessageVisible;
            set => SetProperty(ref _isSearchMessageVisible, value);
        }
        public bool IsEditingTransaction
        {
            get => _isEditingTransaction;
            set => SetProperty(ref _isEditingTransaction, value);
        }
        public string LookupTransactionId
        {
            get => _lookupTransactionId;
            set
            {
                // Cleanup any existing timer first
                CleanupLookupDebounceTimer();

                // Validate input is numeric or empty
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (!int.TryParse(value, out int parsedValue))
                    {
                        // Invalid non-numeric input - show alert and reset to latest ID
                        ShowInvalidLookupAlert();
                        return; // Don't set the invalid value
                    }

                    if (parsedValue < 0)
                    {
                        WindowManager.InvokeAsync(() =>
                            MessageBox.Show(
                                "Transaction number cannot be negative.",
                                "Invalid Input",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning));
                        return;
                    }
                }

                // Store whether the value actually changed
                bool valueChanged = _lookupTransactionId != value;

                // Set property
                SetProperty(ref _lookupTransactionId, value);

                // Only trigger lookup if value changed through UI editing (not from code)
                // and we have a valid ID, and we're not already in edit mode
                if (valueChanged && !string.IsNullOrWhiteSpace(value) &&
                    int.TryParse(value, out _) &&
                    !IsEditingTransaction)
                {
                    // Create a new timer with proper disposal
                    _lookupDebounceTimer = new System.Timers.Timer(500); // 500ms delay

                    // Use try-catch for the event handler
                    _lookupDebounceTimer.Elapsed += async (s, e) =>
                    {
                        try
                        {
                            // Stop timer first to prevent re-entry
                            _lookupDebounceTimer?.Stop();

                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    // Check if command can execute
                                    if (LookupTransactionCommand?.CanExecute(null) == true)
                                    {
                                        LookupTransactionCommand.Execute(null);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error executing lookup command: {ex.Message}");
                                    StatusMessage = "Error looking up transaction";
                                    OnPropertyChanged(nameof(StatusMessage));
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in timer callback: {ex.Message}");
                        }
                    };

                    // Start the timer
                    _lookupDebounceTimer.Start();
                }
            }
        }

        // Add a timer field to debounce lookup requests
        private System.Timers.Timer _lookupDebounceTimer;

        // Helper method to clean up timer resources
        private void CleanupLookupDebounceTimer()
        {
            if (_lookupDebounceTimer != null)
            {
                try
                {
                    _lookupDebounceTimer.Stop();
                    _lookupDebounceTimer.Elapsed -= null; // Remove all event handlers
                    _lookupDebounceTimer.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cleaning up timer: {ex.Message}");
                }
                finally
                {
                    _lookupDebounceTimer = null;
                }
            }
        }

        public CategoryDTO SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    FilterProductsByCategory(value);
                }
            }
        }

        private ObservableCollection<CategoryDTO> _productCategories = new ObservableCollection<CategoryDTO>();
        public ObservableCollection<CategoryDTO> ProductCategories
        {
            get => _productCategories;
            set => SetProperty(ref _productCategories, value);
        }
        public bool IsRestaurantMode
        {
            get => _isRestaurantMode;
            set
            {
                Debug.WriteLine($"Setting IsRestaurantMode to {value}");
                if (SetProperty(ref _isRestaurantMode, value))
                {
                    Debug.WriteLine("IsRestaurantMode property changed");
                    OnPropertyChanged(nameof(IsRestaurantMode));

                    // Do not automatically load categories here
                    // The loading should only happen from explicit calls
                }
            }
        }
        public DateTime CurrentDate
        {
            get => _currentDate;
            set => SetProperty(ref _currentDate, value);
        }
        public bool IsProductCardsVisible
        {
            get => _isProductCardsVisible;
            set => SetProperty(ref _isProductCardsVisible, value);
        }
        public ICommand AddToCartCommand { get; private set; }
        private Dictionary<int, BitmapImage> _imageCache = new Dictionary<int, BitmapImage>();
        public ICommand ToggleViewCommand { get; private set; }
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public bool HasErrors => _validationErrors.Any();

        public string TotalAmountLBP
        {
            get => _totalAmountLBP;
            set => SetProperty(ref _totalAmountLBP, value);
        }

        public decimal SupplierPayments
        {
            get => _supplierPayments;
            set => SetProperty(ref _supplierPayments, value);
        }

        public decimal NetCashflow
        {
            get => _netCashflow;
            set => SetProperty(ref _netCashflow, value);
        }

        public decimal DailyExpenses
        {
            get => _dailyExpenses;
            set => SetProperty(ref _dailyExpenses, value);
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

        public decimal DailySales
        {
            get => _dailySales;
            set => SetProperty(ref _dailySales, value);
        }

        public decimal DailyReturns
        {
            get => _dailyReturns;
            set => SetProperty(ref _dailyReturns, value);
        }

        public decimal NetSales
        {
            get => _netSales;
            set => SetProperty(ref _netSales, value);
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
            set
            {
                if (SetProperty(ref _selectedCustomer, value))
                {
                    // Handle loading customer-specific prices safely
                    LoadCustomerPricesSafely(value);
                }
            }
        }

        // Safe method to load customer specific prices
        private void LoadCustomerPricesSafely(CustomerDTO customer)
        {
            // Early exit if no customer
            if (customer == null)
            {
                CustomerSpecificPrices = new Dictionary<int, decimal>();
                return;
            }

            try
            {
                StatusMessage = $"Loading prices for {customer.Name}...";
                OnPropertyChanged(nameof(StatusMessage));

                // Use the ExecuteOperationSafelyAsync method to properly handle async loading
                ExecuteOperationSafelyAsync(async () =>
                {
                    try
                    {
                        await LoadCustomerSpecificPrices();

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                            StatusMessage = "Prices loaded successfully";
                            OnPropertyChanged(nameof(StatusMessage));
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading customer prices: {ex.Message}");

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                            ShowErrorMessageAsync($"Error loading customer prices: {ex.Message}");
                            StatusMessage = "Error loading customer prices";
                            OnPropertyChanged(nameof(StatusMessage));
                        });
                    }
                }, "Loading customer specific prices").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing customer price loading: {ex.Message}");
                CustomerSpecificPrices = new Dictionary<int, decimal>();
                StatusMessage = "Ready";
            }
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

        private bool _isNavigating = false;
        public string CustomerSearchText
        {
            get => _customerSearchText;
            set
            {
                if (SetProperty(ref _customerSearchText, value))
                {
                    // Only trigger search if not navigating between transactions
                    if (!_isNavigating && !_suppressCustomerDropdown)
                    {
                        // Clear existing results first
                        FilteredCustomers.Clear();
                        OnPropertyChanged(nameof(FilteredCustomers));

                        // Then trigger search
                        SearchCustomers();
                    }
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

        // Add this private field near the other fields
        private bool _suppressCustomerDropdown = false;

        public CustomerDTO? SelectedCustomerFromSearch
        {
            get => _selectedCustomerFromSearch;
            set
            {
                if (SetProperty(ref _selectedCustomerFromSearch, value) && value != null)
                {
                    try
                    {
                        // Prevent search from occurring by setting a flag before changing text
                        _suppressCustomerDropdown = true;

                        // Update customer selection first
                        SelectedCustomer = value;

                        // Set search text (this will trigger SearchCustomers)
                        CustomerSearchText = value.Name ?? string.Empty;

                        // Force hide the dropdown
                        IsCustomerSearchVisible = false;

                        // Clear the filtered customer list to prevent popup showing same customer
                        FilteredCustomers.Clear();

                        // Make sure UI is immediately updated
                        OnPropertyChanged(nameof(FilteredCustomers));
                        OnPropertyChanged(nameof(IsCustomerSearchVisible));
                    }
                    finally
                    {
                        // Always reset the suppression flag after a delay
                        Task.Delay(1000).ContinueWith(_ => {
                            _suppressCustomerDropdown = false;
                        });
                    }
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

        public bool IsProductSearchVisible
        {
            get => _isProductSearchVisible;
            set => SetProperty(ref _isProductSearchVisible, value);
        }
    }
}