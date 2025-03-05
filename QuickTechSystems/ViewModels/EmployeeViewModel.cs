using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.Windows.Input;
using QuickTechSystems.WPF.Commands;
using System.Windows;
using System.Windows.Controls;
using QuickTechSystems.WPF.Views;
using QuickTechSystems.WPF.Services;
using System.Diagnostics;
using QuickTechSystems.WPF.Controls;
using QuickTechSystems.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace QuickTechSystems.WPF.ViewModels
{
    public class EmployeeViewModel : ViewModelBase
    {
        private readonly IEmployeeService _employeeService;
        private readonly IGlobalOverlayService _overlayService;
        private readonly IUnitOfWork _unitOfWork;
        private ObservableCollection<EmployeeDTO> _employees;
        private EmployeeDTO? _selectedEmployee;
        private EmployeeDTO? _currentEmployee;
        private bool _isNewEmployee;
        private bool _isResetingPassword;
        private Action<EntityChangedEvent<EmployeeDTO>> _employeeChangedHandler;

        public ObservableCollection<EmployeeDTO> Employees
        {
            get => _employees;
            set => SetProperty(ref _employees, value);
        }

        public EmployeeDTO? SelectedEmployee
        {
            get => _selectedEmployee;
            set
            {
                if (SetProperty(ref _selectedEmployee, value) && value != null)
                {
                    CurrentEmployee = new EmployeeDTO
                    {
                        EmployeeId = value.EmployeeId,
                        Username = value.Username,
                        FirstName = value.FirstName,
                        LastName = value.LastName,
                        Role = value.Role,
                        IsActive = value.IsActive,
                        PasswordHash = value.PasswordHash,
                        CreatedAt = value.CreatedAt,
                        UpdatedAt = value.UpdatedAt,
                        LastLogin = value.LastLogin
                    };
                    IsNewEmployee = false;
                    IsResetingPassword = false;
                }
            }
        }

        public EmployeeDTO? CurrentEmployee
        {
            get => _currentEmployee;
            set => SetProperty(ref _currentEmployee, value);
        }

        public bool IsNewEmployee
        {
            get => _isNewEmployee;
            set => SetProperty(ref _isNewEmployee, value);
        }

        public bool IsResetingPassword
        {
            get => _isResetingPassword;
            set => SetProperty(ref _isResetingPassword, value);
        }

        public ObservableCollection<string> Roles { get; } = new()
        {
            "Admin",
            "Manager",
            "Cashier"
        };

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public EmployeeViewModel(
            IEmployeeService employeeService,
            IEventAggregator eventAggregator,
            IGlobalOverlayService overlayService,
            IUnitOfWork unitOfWork)
            : base(eventAggregator)
        {
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _employees = new ObservableCollection<EmployeeDTO>();
            _employeeChangedHandler = HandleEmployeeChanged;

            AddCommand = new RelayCommand(_ => AddNew());
            EditCommand = new RelayCommand(param => Edit((EmployeeDTO)param));
            SaveCommand = new AsyncRelayCommand(async param => await SaveAsync(param));
            CancelCommand = new RelayCommand(_ => Cancel());

            _ = LoadDataAsync();
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<EmployeeDTO>>(_employeeChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<EmployeeDTO>>(_employeeChangedHandler);
        }

        private async void HandleEmployeeChanged(EntityChangedEvent<EmployeeDTO> evt)
        {
            try
            {
                Debug.WriteLine($"EmployeeViewModel: Handling employee change: {evt.Action}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (evt.Action)
                    {
                        case "Create":
                            if (!Employees.Any(e => e.EmployeeId == evt.Entity.EmployeeId))
                            {
                                Employees.Add(evt.Entity);
                                Debug.WriteLine($"Added new employee {evt.Entity.Username}");
                            }
                            break;
                        case "Update":
                            var existingIndex = Employees.ToList().FindIndex(e => e.EmployeeId == evt.Entity.EmployeeId);
                            if (existingIndex != -1)
                            {
                                Employees[existingIndex] = evt.Entity;
                                Debug.WriteLine($"Updated employee {evt.Entity.Username}");
                            }
                            break;
                        case "Delete":
                            var employeeToRemove = Employees.FirstOrDefault(e => e.EmployeeId == evt.Entity.EmployeeId);
                            if (employeeToRemove != null)
                            {
                                Employees.Remove(employeeToRemove);
                                Debug.WriteLine($"Removed employee {employeeToRemove.Username}");
                            }
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EmployeeViewModel: Error handling employee change: {ex.Message}");
            }
        }

        private void AddNew()
        {
            CurrentEmployee = new EmployeeDTO
            {
                IsActive = true,
                Role = "Cashier",
                CreatedAt = DateTime.Now
            };
            IsNewEmployee = true;
            IsResetingPassword = false;  // Ensure password section is hidden
            _overlayService.ShowEmployeeEditor(this);
        }

        private void Edit(EmployeeDTO employee)
        {
            SelectedEmployee = employee;
            IsNewEmployee = false;
            IsResetingPassword = false;  // Ensure password reset is only triggered manually
            _overlayService.ShowEmployeeEditor(this);
        }

        private async Task SaveAsync(object parameter)
        {
            try
            {
                if (CurrentEmployee == null) return;

                // Basic validation
                if (string.IsNullOrWhiteSpace(CurrentEmployee.Username) ||
                    string.IsNullOrWhiteSpace(CurrentEmployee.FirstName) ||
                    string.IsNullOrWhiteSpace(CurrentEmployee.LastName))
                {
                    MessageBox.Show("Please fill in all required fields", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string newEmployeePassword = string.Empty;
                string resetPassword = string.Empty;
                string confirmPassword = string.Empty;

                // Get password values directly from the control
                if (parameter is EmployeeEditorControl control)
                {
                    newEmployeePassword = control.GetNewEmployeePassword();
                    resetPassword = control.GetResetPassword();
                    confirmPassword = control.GetConfirmPassword();

                    Debug.WriteLine($"Passwords retrieved - New: {newEmployeePassword.Length} chars, " +
                        $"Reset: {resetPassword.Length} chars, Confirm: {confirmPassword.Length} chars");
                }
                else
                {
                    Debug.WriteLine($"Parameter is not an EmployeeEditorControl: {parameter?.GetType().Name ?? "null"}");
                }

                if (IsNewEmployee)
                {
                    // Handle new employee creation
                    if (string.IsNullOrWhiteSpace(newEmployeePassword))
                    {
                        MessageBox.Show("Please enter a password for the new employee", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    Debug.WriteLine($"Creating new employee with password length: {newEmployeePassword.Length}");
                    CurrentEmployee.PasswordHash = HashPassword(newEmployeePassword);
                    var result = await _employeeService.CreateAsync(CurrentEmployee);
                    _eventAggregator.Publish(new EntityChangedEvent<EmployeeDTO>("Create", result));
                    MessageBox.Show("Employee created successfully", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Handle existing employee update
                    if (IsResetingPassword)
                    {
                        // Handle password reset
                        if (string.IsNullOrWhiteSpace(resetPassword))
                        {
                            MessageBox.Show("Please enter a new password", "Validation Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        if (resetPassword != confirmPassword)
                        {
                            MessageBox.Show("Passwords do not match", "Validation Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        Debug.WriteLine($"Resetting password for employee ID: {CurrentEmployee.EmployeeId} with password length: {resetPassword.Length}");

                        try
                        {
                            // Directly update password in database
                            string hashedPassword = HashPassword(resetPassword);
                            string sql = "UPDATE Employees SET PasswordHash = @hash, UpdatedAt = @now WHERE EmployeeId = @id";
                            var parameters = new object[] {
                                new Microsoft.Data.SqlClient.SqlParameter("@hash", hashedPassword),
                                new Microsoft.Data.SqlClient.SqlParameter("@now", DateTime.Now),
                                new Microsoft.Data.SqlClient.SqlParameter("@id", CurrentEmployee.EmployeeId)
                            };

                            // Execute direct SQL update
                            int rowsAffected = await _unitOfWork.Context.Database.ExecuteSqlRawAsync(sql, parameters);
                            Debug.WriteLine($"Password reset SQL executed, rows affected: {rowsAffected}");

                            if (rowsAffected == 0)
                            {
                                throw new InvalidOperationException($"Failed to update password for employee ID {CurrentEmployee.EmployeeId}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error resetting password: {ex.Message}");
                            MessageBox.Show($"Error resetting password: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }

                    // Update other employee information
                    Debug.WriteLine($"Updating general employee information for ID: {CurrentEmployee.EmployeeId}");
                    CurrentEmployee.UpdatedAt = DateTime.Now;

                    try
                    {
                        await _employeeService.UpdateAsync(CurrentEmployee.EmployeeId, CurrentEmployee);
                        Debug.WriteLine("Employee update service call completed");
                        _eventAggregator.Publish(new EntityChangedEvent<EmployeeDTO>("Update", CurrentEmployee));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating employee: {ex.Message}");
                        MessageBox.Show($"Error updating employee: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string successMessage = IsResetingPassword ?
                        "Employee updated and password reset successfully" :
                        "Employee updated successfully";

                    MessageBox.Show(successMessage, "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                await LoadDataAsync();
                _overlayService.HideEmployeeEditor();
                IsNewEmployee = false;
                IsResetingPassword = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SaveAsync: {ex.Message}\nStack trace: {ex.StackTrace}");
                MessageBox.Show($"Error saving employee: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel()
        {
            _overlayService.HideEmployeeEditor();
            if (IsNewEmployee)
            {
                CurrentEmployee = null;
            }
            IsNewEmployee = false;
            IsResetingPassword = false;
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        protected override async Task LoadDataAsync()
        {
            var employees = await _employeeService.GetAllAsync();
            Employees = new ObservableCollection<EmployeeDTO>(employees);
        }
    }
}