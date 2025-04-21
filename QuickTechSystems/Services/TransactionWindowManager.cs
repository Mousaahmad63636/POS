// Path: QuickTechSystems.WPF/Services/TransactionWindowManager.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Views;

namespace QuickTechSystems.WPF.Services
{
    public class TransactionWindowManager : ITransactionWindowManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly List<Window> _activeWindows = new();

        public int ActiveWindowCount => _activeWindows.Count;

        public TransactionWindowManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async void OpenNewTransactionWindow()
        {
            try
            {
                // Create a scope to ensure isolated service instances
                using var scope = _serviceProvider.CreateScope();

                // Get a new TransactionViewModel from the scope
                var viewModel = scope.ServiceProvider.GetRequiredService<TransactionViewModel>();

                // Create a transaction window but don't show it yet
                var window = new TransactionWindow
                {
                    DataContext = viewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                // Handle window closing to clean up resources
                window.Closed += (sender, args) =>
                {
                    if (sender is Window closedWindow)
                    {
                        _activeWindows.Remove(closedWindow);

                        // Explicitly dispose the ViewModel when window is closed
                        if (closedWindow.DataContext is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                };

                // Add to active windows list
                _activeWindows.Add(window);

                // Show the window first - this is important for UI updates to work properly
                window.Show();

                // Initialize the view model AFTER showing the window but BEFORE user can interact
                // This ensures the UI thread is available to process updates
                window.IsEnabled = false; // Temporarily disable interaction
                try
                {
                    // Call initialization explicitly
                    await viewModel.InitializeForNewWindowAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error initializing view model: {ex.Message}");
                    MessageBox.Show(
                        $"Error initializing transaction window: {ex.Message}",
                        "Initialization Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    // Re-enable interaction with the window
                    window.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening new transaction window: {ex.Message}");
                MessageBox.Show(
                    $"Error opening new transaction window: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void CloseAllTransactionWindows()
        {
            // Create a copy of the list to avoid modification issues during enumeration
            var windowsToClose = _activeWindows.ToList();

            foreach (var window in windowsToClose)
            {
                try
                {
                    // Dispose ViewModel before closing window
                    if (window.DataContext is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }

                    window.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing window: {ex.Message}");
                }
            }

            _activeWindows.Clear();
        }
    }
}