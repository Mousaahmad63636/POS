using System;
using System.Collections.ObjectModel;
using System.Security.Policy;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.IdentityModel.Tokens;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.WPF.Commands;
using static System.Runtime.InteropServices.JavaScript.JSType;
using ZXing.QrCode.Internal;
using QuickTechSystems.Domain.Interfaces.Repositories;
using QuickTechSystems.Application.Helpers;
using System.Diagnostics;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class TransactionViewModel : ViewModelBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITransactionService _transactionService;
        private readonly ICustomerService _customerService;
        private readonly IProductService _productService;
        private readonly IDrawerService _drawerService;

        private readonly Action<EntityChangedEvent<TransactionDTO>> _transactionChangedHandler;
        private Action<EntityChangedEvent<ProductDTO>> _productChangedHandler;

        public TransactionViewModel(
   IUnitOfWork unitOfWork,
    ITransactionService transactionService,
    ICustomerService customerService,
    IProductService productService,
    IDrawerService drawerService,
    IBusinessSettingsService businessSettingsService,
    IEventAggregator eventAggregator)
    : base(eventAggregator)
        {
            _unitOfWork = unitOfWork;
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _transactionChangedHandler = HandleTransactionChanged;
            _drawerService = drawerService;
            _ = InitializeProductsAsync();
            _productChangedHandler = HandleProductChanged;
            _ = LoadExchangeRate(businessSettingsService);
            InitializeCommands();
            InitializeCollections();
            StartNewTransaction();
            _filteredProducts = new ObservableCollection<ProductDTO>();
            CloseDrawerCommand = new AsyncRelayCommand(async _ => await CloseDrawerAsync());
            ProcessBarcodeCommand = new AsyncRelayCommand(async _ => await ProcessBarcodeInput());
        }
        private async Task LoadExchangeRate(IBusinessSettingsService businessSettingsService)
        {
            var rateSetting = await businessSettingsService.GetByKeyAsync("ExchangeRate");
            if (rateSetting != null && decimal.TryParse(rateSetting.Value, out decimal rate))
            {
                CurrencyHelper.UpdateExchangeRate(rate);
            }
        }

        private async Task InitializeProductsAsync()
        {
            try
            {
                var products = await _productService.GetAllAsync();
                AllProducts = new ObservableCollection<ProductDTO>(products);

                // Initialize FilteredProducts with only Internet category products
                var internetProducts = products
                    .Where(p => p.CategoryName?.Contains("Internet", StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                FilteredProducts = new ObservableCollection<ProductDTO>(internetProducts);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading products: {ex.Message}");
                // Handle error appropriately
            }
        }
        private async Task CloseDrawerAsync()
        {
            try
            {
                var dialog = new InputDialog("Close Drawer", "Enter final cash amount:")
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal finalAmount))
                {
                    await _drawerService.CloseDrawerAsync(finalAmount, "Closed by user at end of shift");

                    MessageBox.Show("Drawer closed successfully. Please log out.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error closing drawer: {ex.Message}");
            }
        }

        public DateTime CurrentDate => DateTime.Now;
    protected override void SubscribeToEvents()
    {
        _eventAggregator.Subscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Subscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
            _eventAggregator.Subscribe<LowStockWarningEvent>(HandleLowStockWarning);
        }
    private async Task ProcessSaleAsync()
    {
        try
        {
            if (CurrentTransaction?.Details == null || !CurrentTransaction.Details.Any())
            {
                await ShowErrorMessageAsync("No items in transaction");
                return;
            }
            CurrentTransaction.TransactionDate = DateTime.Now;
            CurrentTransaction.CustomerId = SelectedCustomer?.CustomerId;
            CurrentTransaction.CustomerName = SelectedCustomer?.Name ?? "Walk-in Customer";
            CurrentTransaction.TotalAmount = TotalAmount;
            CurrentTransaction.Status = TransactionStatus.Completed;
            CurrentTransaction.TransactionType = TransactionType.Sale;
            var result = await _transactionService.ProcessSaleAsync(CurrentTransaction);
            if (result != null)
            {
                    await PrintReceipt();
                    StartNewTransaction();
                    MessageBox.Show("Transaction completed successfully", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error processing sale: {ex.Message}");
            }
        }
        private void HandleLowStockWarning(LowStockWarningEvent evt)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"Warning: {evt.Product.Name} stock is now {evt.Product.CurrentStock}, " +
                    $"which is below the minimum stock of {evt.MinimumStock}.",
                    "Low Stock Alert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            });
        }
        private async void HandleProductChanged(EntityChangedEvent<ProductDTO> evt)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    // Refresh all products
                    var products = await _productService.GetAllAsync();
                    AllProducts = new ObservableCollection<ProductDTO>(products);

                    // Update filtered products for dropdown
                    var internetProducts = products
                        .Where(p => p.CategoryName?.Contains("Internet", StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                    FilteredProducts = new ObservableCollection<ProductDTO>(internetProducts);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling product change: {ex.Message}");
            }
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<TransactionDTO>>(_transactionChangedHandler);
            _eventAggregator.Unsubscribe<EntityChangedEvent<ProductDTO>>(_productChangedHandler);
        }

        private async void HandleTransactionChanged(EntityChangedEvent<TransactionDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (evt.Action)
                {
                    case "Create":
                        StartNewTransaction();
                        break;
                    case "Update":
                        // Update handling if needed
                        break;
                    case "Delete":
                        // Delete handling if needed
                        break;
                }
            });
        }
    }
}