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
    public interface ITransactionWindowManager
    {
        void OpenNewTransactionWindow();
        void CloseAllTransactionWindows();
        int ActiveWindowCount { get; }
    }

    public class TransactionWindowManager : ITransactionWindowManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly List<TransactionWindow> _activeWindows = new();

        public int ActiveWindowCount => _activeWindows.Count;

        public TransactionWindowManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void OpenNewTransactionWindow()
        {
            try
            {
                // Use the service provider to create a new instance of TransactionViewModel
                var viewModel = _serviceProvider.GetRequiredService<TransactionViewModel>();

                // Create a new transaction window with the view model
                var window = new TransactionWindow(viewModel);

                // Handle window closed event to remove from active windows
                window.Closed += (sender, args) =>
                {
                    if (sender is TransactionWindow closedWindow)
                    {
                        _activeWindows.Remove(closedWindow);
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