// QuickTechSystems.WPF/ViewModels/CustomerViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.ViewModels
{
    public class CustomerViewModel : ViewModelBase
    {
        private readonly ICustomerService _customerService;
        private ObservableCollection<CustomerDTO> _customers;
        private CustomerDTO? _selectedCustomer;
        private bool _isEditing;
        private string _searchText = string.Empty;
        private readonly Action<EntityChangedEvent<CustomerDTO>> _customerChangedHandler;

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

        public ICommand LoadCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand SearchCommand { get; }

        public CustomerViewModel(
            ICustomerService customerService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _customers = new ObservableCollection<CustomerDTO>();
            _customerChangedHandler = HandleCustomerChanged;

            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            AddCommand = new RelayCommand(_ => AddNew());
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync());
            SearchCommand = new AsyncRelayCommand(async _ => await SearchCustomersAsync());

            SubscribeToEvents();
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
                            Customers[index] = evt.Entity;
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

        protected override async Task LoadDataAsync()
        {
            try
            {
                var customers = await _customerService.GetAllAsync();
                Customers = new ObservableCollection<CustomerDTO>(customers);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading customers: {ex.Message}");
                MessageBox.Show($"Error loading customers: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddNew()
        {
            SelectedCustomer = new CustomerDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now
            };
        }

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

                if (SelectedCustomer.CustomerId == 0)
                {
                    await _customerService.CreateAsync(SelectedCustomer);
                }
                else
                {
                    await _customerService.UpdateAsync(SelectedCustomer);
                }

                await LoadDataAsync();
                MessageBox.Show("Customer saved successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving customer: {ex.Message}");
                MessageBox.Show($"Error saving customer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                    try
                    {
                        await _customerService.DeleteAsync(SelectedCustomer.CustomerId);
                        await LoadDataAsync();
                        MessageBox.Show("Customer deleted successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be physically deleted"))
                    {
                        // This exception is expected for soft deletes
                        MessageBox.Show(ex.Message, "Customer Marked as Inactive",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadDataAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting customer: {ex.Message}");

                // Store a local reference to selected customer
                var customerToSoftDelete = SelectedCustomer;

                // Add safety check for null reference
                if (customerToSoftDelete == null || _customerService == null)
                {
                    MessageBox.Show("Unable to process customer - customer information is no longer available.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // For other unexpected exceptions, offer to soft delete
                if (MessageBox.Show(
                    "This customer cannot be deleted. Would you like to mark them as inactive instead?",
                    "Cannot Delete Customer",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Use the local reference instead of potentially changing SelectedCustomer property
                        await _customerService.SoftDeleteAsync(customerToSoftDelete.CustomerId);
                        await LoadDataAsync();
                        MessageBox.Show("Customer has been marked as inactive.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception softDeleteEx)
                    {
                        Debug.WriteLine($"Error soft-deleting customer: {softDeleteEx.Message}");
                        MessageBox.Show($"Error marking customer as inactive: {softDeleteEx.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show($"Error deleting customer: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task SearchCustomersAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    await LoadDataAsync();
                    return;
                }

                var customers = await _customerService.GetByNameAsync(SearchText);
                Customers = new ObservableCollection<CustomerDTO>(customers);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching customers: {ex.Message}");
                MessageBox.Show($"Error searching customers: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}