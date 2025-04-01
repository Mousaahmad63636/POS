// Path: QuickTechSystems.WPF/Services/TransactionWindowManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
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

        public void OpenNewTransactionWindow()
        {
            try
            {
                // Create a scope to ensure isolated service instances
                using var scope = _serviceProvider.CreateScope();

                // Get a new TransactionViewModel from the scope
                var viewModel = scope.ServiceProvider.GetRequiredService<TransactionViewModel>();

                // Initialize the new view model (create a clean transaction)
                viewModel.StartNewTransaction();

                // Create a transaction window
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

                // Show the window
                window.Show();
            }
            catch (Exception ex)
            {
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
                    System.Diagnostics.Debug.WriteLine($"Error closing window: {ex.Message}");
                }
            }

            _activeWindows.Clear();
        }
    }
}