using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.ViewModels
{
    public class CustomerViewModel : ViewModelBase, IDisposable
    {
        private readonly ICustomerService _customerService;
        private readonly IProductService _productService;
        private ObservableCollection<CustomerDTO> _customers;
        private CustomerDTO? _selectedCustomer;
        private bool _isEditing;
        private string _searchText = string.Empty;
        private Action<EntityChangedEvent<CustomerDTO>> _customerChangedHandler;
        private bool _isProductPricesDialogOpen;
        private ObservableCollection<CustomerProductPriceViewModel> _customerProducts;
        private bool _isSaving;
        private bool _isCustomerPopupOpen;
        private bool _isNewCustomer;
        private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;

        // Concurrency control
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private bool _operationInProgress = false;
        private string _errorMessage = string.Empty;
        private bool _hasErrors;

        #region Properties
        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        public bool IsProductPricesDialogOpen
        {
            get => _isProductPricesDialogOpen;
            set => SetProperty(ref _isProductPricesDialogOpen, value);
        }

        public bool IsCustomerPopupOpen
        {
            get => _isCustomerPopupOpen;
            set => SetProperty(ref _isCustomerPopupOpen, value);
        }

        public bool IsNewCustomer
        {
            get => _isNewCustomer;
            set => SetProperty(ref _isNewCustomer, value);
        }

        public ObservableCollection<CustomerProductPriceViewModel> CustomerProducts
        {
            get => _customerProducts;
            set => SetProperty(ref _customerProducts, value);
        }

        public ObservableCollection<CustomerDTO> Customers
        {
            get => _customers;
            set => SetProperty(ref _customers, value);
        }

        public CustomerDTO? SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                SetProperty(ref _selectedCustomer, value);
                IsEditing = value != null;
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                _ = SearchCustomersAsync();
            }
        }

        public FlowDirection CurrentFlowDirection
        {
            get => _currentFlowDirection;
            set => SetProperty(ref _currentFlowDirection, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool HasErrors
        {
            get => _hasErrors;
            set => SetProperty(ref _hasErrors, value);
        }
        #endregion

        #region Commands
        public ICommand LoadCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand SetProductPricesCommand { get; }
        public ICommand SaveCustomPricesCommand { get; }
        public ICommand CloseProductPricesDialogCommand { get; }
        public ICommand ResetCustomPriceCommand { get; }
        public ICommand ResetAllCustomPricesCommand { get; }
        #endregion

        public CustomerViewModel(
            ICustomerService customerService,
            IProductService productService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _customers = new ObservableCollection<CustomerDTO>();
            _customerProducts = new ObservableCollection<CustomerProductPriceViewModel>();
            _customerChangedHandler = HandleCustomerChanged;

            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync(), _ => !IsSaving);
            AddCommand = new RelayCommand(_ => AddNew(), _ => !IsSaving);
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync(), _ => !IsSaving);
            DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync(), _ => !IsSaving);
            SearchCommand = new AsyncRelayCommand(async _ => await SearchCustomersAsync(), _ => !IsSaving);
            SetProductPricesCommand = new AsyncRelayCommand(async _ => await ShowProductPricesDialog(), _ => !IsSaving && SelectedCustomer != null);
            SaveCustomPricesCommand = new AsyncRelayCommand(async _ => await SaveCustomPrices(), _ => !IsSaving);
            CloseProductPricesDialogCommand = new RelayCommand(_ => CloseProductPricesDialog(), _ => !IsSaving);
            ResetCustomPriceCommand = new RelayCommand(param => ResetCustomPrice(param as CustomerProductPriceViewModel), _ => !IsSaving);
            ResetAllCustomPricesCommand = new RelayCommand(_ => ResetAllCustomPrices(), _ => !IsSaving);

            // Start initial data load
            _ = LoadDataAsync();
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<CustomerDTO>>(_customerChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<CustomerDTO>>(_customerChangedHandler);
        }

        private async void HandleCustomerChanged(EntityChangedEvent<CustomerDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (evt.Action)
                {
                    case "Create":
                        Customers.Add(evt.Entity);
                        break;
                    case "Update":
                        var existingCustomer = Customers.FirstOrDefault(c => c.CustomerId == evt.Entity.CustomerId);
                        if (existingCustomer != null)
                        {
                            var index = Customers.IndexOf(existingCustomer);
                            if (index >= 0)
                            {
                                Customers[index] = evt.Entity;
                            }
                        }
                        break;
                    case "Delete":
                        var customerToRemove = Customers.FirstOrDefault(c => c.CustomerId == evt.Entity.CustomerId);
                        if (customerToRemove != null)
                        {
                            Customers.Remove(customerToRemove);
                        }
                        break;
                }
            });
        }

        #region Database Operations
        // Helper method for safe database operations
        private async Task<T> ExecuteDbOperationSafelyAsync<T>(Func<Task<T>> operation, string operationName = "Database operation")
        {
            Debug.WriteLine($"BEGIN: {operationName}");

            // If an operation is already in progress, wait a bit
            int waitCount = 0;
            while (_operationInProgress)
            {
                waitCount++;
                Debug.WriteLine($"Operation in progress, waiting... (attempt {waitCount})");
                await Task.Delay(100);

                // Safety timeout
                if (waitCount > 50) // 5 seconds max wait
                {
                    Debug.WriteLine("TIMEOUT waiting for operation lock, proceeding anyway");
                    break;
                }
            }

            Debug.WriteLine($"Acquiring operation lock for: {operationName}");
            await _operationLock.WaitAsync();
            _operationInProgress = true;

            try
            {
                Debug.WriteLine($"Executing operation: {operationName}");
                // Add a small delay to ensure any previous operation is fully complete
                await Task.Delay(200);
                var result = await operation();
                Debug.WriteLine($"Operation completed successfully: {operationName}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in {operationName}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await ShowErrorMessageAsync($"Error: {ex.Message}");
                throw;
            }
            finally
            {
                _operationInProgress = false;
                _operationLock.Release();
                Debug.WriteLine($"Released operation lock for: {operationName}");
                Debug.WriteLine($"END: {operationName}");
            }
        }

        // Overload for void operations
        private async Task ExecuteDbOperationSafelyAsync(Func<Task> operation, string operationName = "Database operation")
        {
            await ExecuteDbOperationSafelyAsync<bool>(async () =>
            {
                await operation();
                return true;
            }, operationName);
        }

        private async Task ShowErrorMessageAsync(string message)
        {
            ErrorMessage = message;
            HasErrors = true;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // For critical errors, also show a message box
                if (message.Contains("critical") || message.Contains("exception"))
                {
                    MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });

            // Automatically clear error after delay
            Task.Run(async () =>
            {
                await Task.Delay(5000); // Show error for 5 seconds
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ErrorMessage == message) // Only clear if still the same message
                    {
                        ErrorMessage = string.Empty;
                        HasErrors = false;
                    }
                });
            });
        }
        #endregion

        #region Data Loading
        protected override async Task LoadDataAsync()
        {
            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;
                HasErrors = false;

                // Get customers asynchronously using the safe operation method
                var customers = await ExecuteDbOperationSafelyAsync(async () =>
                {
                    return await _customerService.GetAllAsync();
                }, "Loading customers");

                // Preserve the currently selected customer ID if any
                int? selectedCustomerId = SelectedCustomer?.CustomerId;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Create new collection with fetched data
                    Customers = new ObservableCollection<CustomerDTO>(customers);

                    // If there was a selected customer, try to reselect it in the new collection
                    if (selectedCustomerId.HasValue)
                    {
                        SelectedCustomer = Customers.FirstOrDefault(c => c.CustomerId == selectedCustomerId.Value);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading customers: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                if (ex.Message.Contains("second operation"))
                {
                    await ShowErrorMessageAsync("Database is busy. Please try again in a moment.");
                }
                else
                {
                    await ShowErrorMessageAsync($"Error loading customers: {ex.Message}");
                }
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task SearchCustomersAsync()
        {
            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;
                HasErrors = false;

                var searchTerm = SearchText;

                var customers = await ExecuteDbOperationSafelyAsync(async () =>
                {
                    return string.IsNullOrWhiteSpace(searchTerm)
                        ? await _customerService.GetAllAsync()
                        : await _customerService.GetByNameAsync(searchTerm);
                }, $"Searching customers for '{searchTerm}'");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Customers = new ObservableCollection<CustomerDTO>(customers);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching customers: {ex.Message}");
                await ShowErrorMessageAsync($"Error searching customers: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }
        #endregion

        #region Customer Popup Management
        public void ShowCustomerPopup()
        {
            IsCustomerPopupOpen = true;
        }

        public void CloseCustomerPopup()
        {
            IsCustomerPopupOpen = false;
        }

        private void AddNew()
        {
            SelectedCustomer = new CustomerDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            IsNewCustomer = true;
            ShowCustomerPopup();
        }

        public void EditCustomer(CustomerDTO customer)
        {
            if (customer != null)
            {
                SelectedCustomer = customer;
                IsNewCustomer = false;
                ShowCustomerPopup();
            }
        }
        #endregion

        #region Save and Delete
        private async Task SaveAsync()
        {
            try
            {
                if (SelectedCustomer == null) return;

                if (string.IsNullOrWhiteSpace(SelectedCustomer.Name))
                {
                    MessageBox.Show("Customer name is required.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                IsSaving = true;
                ErrorMessage = string.Empty;
                HasErrors = false;

                // Store the customer ID to ensure it's preserved
                int customerId = SelectedCustomer.CustomerId;

                // Create a copy to avoid issues during async operation
                var customerToSave = new CustomerDTO
                {
                    CustomerId = customerId,
                    Name = SelectedCustomer.Name,
                    Phone = SelectedCustomer.Phone,
                    Email = SelectedCustomer.Email,
                    Address = SelectedCustomer.Address,
                    IsActive = SelectedCustomer.IsActive,
                    CreatedAt = customerId == 0 ? DateTime.Now : SelectedCustomer.CreatedAt,
                    UpdatedAt = customerId != 0 ? DateTime.Now : null
                };

                if (customerToSave.CustomerId == 0)
                {
                    // Create new customer
                    var savedCustomer = await ExecuteDbOperationSafelyAsync(async () =>
                    {
                        return await _customerService.CreateAsync(customerToSave);
                    }, "Creating new customer");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Customers.Add(savedCustomer);
                        SelectedCustomer = savedCustomer; // Update selection to saved customer
                    });
                }
                else
                {
                    // Update existing customer
                    await ExecuteDbOperationSafelyAsync(async () =>
                    {
                        await _customerService.UpdateAsync(customerToSave);
                    }, "Updating customer");

                    // Update local collection with a more reliable method
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Find the index of the customer directly by ID
                        int index = -1;
                        for (int i = 0; i < Customers.Count; i++)
                        {
                            if (Customers[i].CustomerId == customerToSave.CustomerId)
                            {
                                index = i;
                                break;
                            }
                        }

                        if (index != -1)
                        {
                            Customers[index] = customerToSave;
                            SelectedCustomer = customerToSave; // Update current selection
                        }
                        else
                        {
                            // If we can't find the customer in the collection, refresh all data
                            LoadDataAsync();
                        }
                    });
                }

                // Close the popup automatically
                CloseCustomerPopup();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Customer saved successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving customer: {ex.Message}");

                if (ex.Message.Contains("second operation") || ex.Message.Contains("concurrency"))
                {
                    await ShowErrorMessageAsync("Database is busy. Please try again in a moment.");
                    await LoadDataAsync(); // Refresh to get latest data
                }
                else
                {
                    await ShowErrorMessageAsync($"Error saving customer: {ex.Message}");
                }
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task DeleteAsync()
        {
            try
            {
                if (SelectedCustomer == null) return;

                if (MessageBox.Show("Are you sure you want to delete this customer?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    IsSaving = true;
                    ErrorMessage = string.Empty;
                    HasErrors = false;

                    // Store the ID of the customer being deleted for later reference
                    int customerIdToDelete = SelectedCustomer.CustomerId;

                    // Close the popup first to improve UI responsiveness
                    CloseCustomerPopup();

                    try
                    {
                        // Perform the deletion using safe operation
                        await ExecuteDbOperationSafelyAsync(async () =>
                        {
                            await _customerService.DeleteAsync(customerIdToDelete);
                        }, "Deleting customer");

                        // Remove from local collection to immediately update UI
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var customerToRemove = Customers.FirstOrDefault(c => c.CustomerId == customerIdToDelete);
                            if (customerToRemove != null)
                            {
                                Customers.Remove(customerToRemove);
                            }

                            // Clear selection
                            SelectedCustomer = null;
                        });

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show("Customer deleted successfully.", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        });

                        // Reload data to ensure consistency with database
                        await LoadDataAsync();
                    }
                    catch (InvalidOperationException ex)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(ex.Message, "Cannot Delete Customer",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        });

                        var result = MessageBox.Show("Would you like to mark this customer as inactive instead?",
                            "Mark Inactive", MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            // Get the customer from the collection again since we closed the popup
                            SelectedCustomer = Customers.FirstOrDefault(c => c.CustomerId == customerIdToDelete);

                            if (SelectedCustomer != null)
                            {
                                SelectedCustomer.IsActive = false;
                                await SaveAsync();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting customer: {ex.Message}");

                if (ex.Message.Contains("second operation") || ex.Message.Contains("concurrency") ||
                    ex.Message.Contains("affect") || ex.Message.Contains("modified") ||
                    ex.Message.Contains("deleted"))
                {
                    await ShowErrorMessageAsync(
                        "The customer may have been modified or deleted by another user.\n" +
                        "The customer list will be refreshed.");
                }
                else
                {
                    await ShowErrorMessageAsync($"Error deleting customer: {ex.Message}");
                }

                // Always refresh the data after an error to ensure consistency
                await LoadDataAsync();
            }
            finally
            {
                IsSaving = false;
            }
        }
        #endregion

        #region Product Pricing
        private async Task ShowProductPricesDialog()
        {
            if (SelectedCustomer == null) return;

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;
                HasErrors = false;

                // Get all products and customer's custom prices using safe operations
                var products = await ExecuteDbOperationSafelyAsync(async () =>
                {
                    return await _productService.GetAllAsync();
                }, "Loading products");

                var customPrices = await ExecuteDbOperationSafelyAsync(async () =>
                {
                    return await _customerService.GetCustomProductPricesAsync(SelectedCustomer.CustomerId);
                }, "Loading custom prices");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CustomerProducts = new ObservableCollection<CustomerProductPriceViewModel>(
                        products.Select(p =>
                        {
                            var customPrice = customPrices.FirstOrDefault(cp => cp.ProductId == p.ProductId);
                            return new CustomerProductPriceViewModel
                            {
                                ProductId = p.ProductId,
                                ProductName = p.Name,
                                DefaultPrice = p.SalePrice,
                                CustomPrice = customPrice?.Price ?? p.SalePrice
                            };
                        }));

                    IsProductPricesDialogOpen = true;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading product prices: {ex.Message}");

                if (ex.Message.Contains("second operation") || ex.Message.Contains("concurrency"))
                {
                    await ShowErrorMessageAsync("Database is busy. Please try again in a moment.");
                }
                else
                {
                    await ShowErrorMessageAsync($"Error loading product prices: {ex.Message}");
                }
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task SaveCustomPrices()
        {
            if (SelectedCustomer == null) return;

            try
            {
                IsSaving = true;
                ErrorMessage = string.Empty;
                HasErrors = false;

                var customerId = SelectedCustomer.CustomerId;
                var prices = CustomerProducts.Select(cp => new CustomerProductPriceDTO
                {
                    CustomerId = customerId,
                    ProductId = cp.ProductId,
                    Price = cp.CustomPrice
                }).ToList();

                await ExecuteDbOperationSafelyAsync(async () =>
                {
                    await _customerService.SetCustomProductPricesAsync(customerId, prices);
                }, "Saving custom prices");

                IsProductPricesDialogOpen = false;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Custom prices saved successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving custom prices: {ex.Message}");

                if (ex.Message.Contains("second operation") || ex.Message.Contains("concurrency"))
                {
                    await ShowErrorMessageAsync("Database is busy. Please try again in a moment.");
                }
                else
                {
                    await ShowErrorMessageAsync($"Error saving custom prices: {ex.Message}");
                }
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void CloseProductPricesDialog()
        {
            IsProductPricesDialogOpen = false;
        }

        private void ResetCustomPrice(CustomerProductPriceViewModel price)
        {
            if (price != null)
            {
                price.CustomPrice = price.DefaultPrice;
            }
        }

        private void ResetAllCustomPrices()
        {
            if (CustomerProducts != null)
            {
                foreach (var product in CustomerProducts)
                {
                    product.CustomPrice = product.DefaultPrice;
                }
            }
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeFromEvents();
                _operationLock?.Dispose();

                // Clear collections to help with garbage collection
                Customers?.Clear();
                CustomerProducts?.Clear();
            }
        }
        #endregion
    }
}