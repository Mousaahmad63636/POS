using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.WPF.Views;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class DrawerViewModel
    {
        public ICommand OpenDrawerCommand { get; }
        public ICommand AddCashCommand { get; }
        public ICommand RemoveCashCommand { get; }
        public ICommand CloseDrawerCommand { get; }
        public ICommand PrintReportCommand { get; }
        public ICommand LoadFinancialDataCommand { get; }

        // In DrawerViewModel.cs
        private async Task OpenDrawerAsync()
        {
            try
            {
                var currentUser = System.Windows.Application.Current.Properties["CurrentUser"] as EmployeeDTO;
                if (currentUser == null)
                {
                    await ShowErrorMessageAsync("No user is currently logged in");
                    return;
                }

                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Open Drawer", "Enter opening balance:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal amount))
                {
                    CurrentDrawer = await _drawerService.OpenDrawerAsync(
                        amount,
                        currentUser.EmployeeId.ToString(),
                        $"{currentUser.FirstName} {currentUser.LastName}"
                    );
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();
                    MessageBox.Show("Drawer opened successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error opening drawer: {ex.Message}");
            }
        }

        private async Task AddCashAsync()
        {
            try
            {
                IsProcessing = true;
                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Add Cash", "Enter amount to add:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal amount))
                {
                    CurrentDrawer = await _drawerService.AddCashTransactionAsync(amount, true);
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();
                    MessageBox.Show($"Successfully added {amount:C2}", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error adding cash: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task RemoveCashAsync()
        {
            try
            {
                IsProcessing = true;
                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Remove Cash", "Enter amount to remove:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal amount))
                {
                    if (amount > CurrentDrawer?.CurrentBalance)
                    {
                        MessageBox.Show("Amount exceeds current balance.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    CurrentDrawer = await _drawerService.AddCashTransactionAsync(amount, false);
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();
                    MessageBox.Show($"Successfully removed {amount:C2}", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error removing cash: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task CloseDrawerAsync()
        {
            try
            {
                IsProcessing = true;
                var window = _windowService.GetCurrentWindow();
                var dialog = new InputDialog("Close Drawer", "Enter final cash amount:")
                {
                    Owner = window
                };

                if (dialog.ShowDialog() == true && decimal.TryParse(dialog.Input, out decimal finalAmount))
                {
                    string notes = await ShowNotesInputDialog();

                    // Store the current balance before closing
                    decimal currentBalance = CurrentDrawer?.CurrentBalance ?? 0;

                    // Close the drawer
                    CurrentDrawer = await _drawerService.CloseDrawerAsync(finalAmount, notes);
                    await LoadDrawerHistoryAsync();
                    UpdateStatus();

                    // Calculate difference manually in case CurrentDrawer.Difference is null
                    decimal difference = finalAmount - currentBalance;

                    var message = difference == 0
                        ? "Drawer closed successfully with no discrepancy."
                        : $"Drawer closed with a {(difference > 0 ? "surplus" : "shortage")} of {Math.Abs(difference):C2}";

                    MessageBox.Show(window, message, "Drawer Closed",
                        MessageBoxButton.OK,
                        difference == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in CloseDrawerAsync: {ex}");
                await ShowErrorMessageAsync($"Error closing drawer: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task<string> ShowNotesInputDialog()
        {
            var window = _windowService.GetCurrentWindow();
            var dialog = new InputDialog("Closing Notes", "Enter any notes for closing:")
            {
                Owner = window
            };

            return dialog.ShowDialog() == true ? dialog.Input : string.Empty;
        }
    }
}