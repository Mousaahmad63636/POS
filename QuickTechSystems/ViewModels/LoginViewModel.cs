using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services;
using QuickTechSystems.WPF;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using System.Windows.Controls;  // For PasswordBox
using Microsoft.Extensions.DependencyInjection;  // For GetRequiredService

namespace QuickTechSystems.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IAuthService _authService;
        private string _username = string.Empty;
        private readonly IDrawerService _drawerService;
        private string _errorMessage = string.Empty;

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public ICommand LoginCommand { get; }

        public LoginViewModel(
            IAuthService authService,
            IDrawerService drawerService,
            IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _drawerService = drawerService;
            _authService = authService;
            LoginCommand = new AsyncRelayCommand(ExecuteLoginAsync);
        }

        private async Task CheckDrawerStatus(EmployeeDTO employee, PasswordBox passwordBox)
        {
            try
            {
                var drawerService = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<IDrawerService>();
                var currentDrawer = await drawerService.GetCurrentDrawerAsync();

                if (currentDrawer == null)
                {
                    // No open drawer, show prompt
                    var window = new CashDrawerPromptWindow();
                    var viewModel = new CashDrawerPromptViewModel(
                        drawerService,
                        window,
                        _eventAggregator);
                    window.DataContext = viewModel;

                    var result = window.ShowDialog();
                    if (result != true)
                    {
                        // User cancelled drawer opening
                        ErrorMessage = "Drawer must be opened to continue";
                        return;
                    }
                }

                // Proceed to main window
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    Window.GetWindow(passwordBox)?.Close();
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error checking drawer status: {ex.Message}";
            }
        }
        private async Task CheckDrawerStatusAndProceed(PasswordBox passwordBox, EmployeeDTO employee)
        {
            try
            {
                var currentDrawer = await _drawerService.GetCurrentDrawerAsync();

                if (currentDrawer == null)
                {
                    var window = new CashDrawerPromptWindow();
                    var viewModel = new CashDrawerPromptViewModel(
                        _drawerService,
                        window,
                        _eventAggregator);
                    window.DataContext = viewModel;

                    var result = window.ShowDialog();
                    if (result != true)
                    {
                        ErrorMessage = "Drawer must be opened to continue";
                        return;
                    }
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    Window.GetWindow(passwordBox)?.Close();
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error checking drawer status: {ex.Message}";
            }
        }

        private async Task ExecuteLoginAsync(object? parameter)
        {
            if (parameter is not PasswordBox passwordBox) return;

            try
            {
                ErrorMessage = string.Empty;
                var employee = await _authService.LoginAsync(Username, passwordBox.Password);

                if (employee == null)
                {
                    ErrorMessage = "Invalid username or password";
                    return;
                }

                App.Current.Properties["CurrentUser"] = employee;
                await CheckDrawerStatusAndProceed(passwordBox, employee);
            }
            catch (Exception ex)
            {
                ErrorMessage = "An error occurred during login. Please try again.";
                Debug.WriteLine($"Login error: {ex.Message}");
            }
        }
      
    }
}