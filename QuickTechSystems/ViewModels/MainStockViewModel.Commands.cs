// Path: QuickTechSystems.WPF.ViewModels/MainStockViewModel.Commands.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.Application.Services;
namespace QuickTechSystems.WPF.ViewModels
{
    public partial class MainStockViewModel
    {
        /// <summary>
        /// Initializes all commands used by the MainStockViewModel
        /// </summary>
        private void InitializeCommands()
        {
            try
            {
                Debug.WriteLine("Initializing MainStockViewModel commands");

                // Basic CRUD operations
                LoadCommand = new AsyncRelayCommand(
                    async _ => await LoadDataAsync(),
                    _ => !IsSaving);

                AddCommand = new RelayCommand(
                    _ => AddNew(),
                    _ => !IsSaving);

                SaveCommand = new AsyncRelayCommand(
                    async _ => await SaveAsync(),
                    _ => !IsSaving && SelectedItem != null);

                DeleteCommand = new AsyncRelayCommand(
                    async _ => await DeleteAsync(),
                    _ => !IsSaving && SelectedItem != null);

                // Barcode operations
                GenerateBarcodeCommand = new RelayCommand(
                    _ => GenerateBarcode(),
                    _ => !IsSaving && SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.Barcode));

                GenerateAutomaticBarcodeCommand = new RelayCommand(
                    _ => GenerateAutomaticBarcode(),
                    _ => !IsSaving && SelectedItem != null);

                GenerateMissingBarcodesCommand = new AsyncRelayCommand(
                    async _ => await GenerateMissingBarcodeImages(),
                    _ => !IsSaving);

                // Stock operations
                UpdateStockCommand = new AsyncRelayCommand(
                    async _ => await UpdateStockAsync(),
                    _ => !IsSaving && SelectedItem != null && StockIncrement > 0);

                // Printing operations
                PrintBarcodeCommand = new AsyncRelayCommand(
                    async _ => await PrintBarcodeAsync(),
                    _ => !IsSaving && SelectedItem != null && SelectedItem.BarcodeImage != null);

                // Image operations
                UploadImageCommand = new RelayCommand(
                    _ => UploadImage(),
                    _ => !IsSaving && SelectedItem != null);

                ClearImageCommand = new RelayCommand(
                    _ => ClearImage(),
                    _ => !IsSaving && SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.ImagePath));

                // Bulk operations
                BulkAddCommand = new AsyncRelayCommand(
                    async _ => {
                        bool success = await ShowBulkAddDialogAndRefresh();
                    },
                    _ => !IsSaving);

                // Transfer operations
                TransferToStoreCommand = new RelayCommand(
                    _ => ShowTransferDialog(),
                    _ => !IsSaving && SelectedItem != null && SelectedItem.CurrentStock > 0);

                SaveTransferCommand = new AsyncRelayCommand(
                    async _ => {
                        try
                        {
                            await TransferToStoreAsync();
                            IsTransferPopupOpen = false;
                            await SafeLoadDataAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in transfer: {ex.Message}");
                            string errorMessage = ex.Message;
                            // Fix any incorrect "Payment failed" error messages
                            if (errorMessage.Contains("Payment failed:"))
                            {
                                errorMessage = errorMessage.Replace("Payment failed:", "Transfer failed:");
                            }
                            ShowTemporaryErrorMessage(errorMessage);
                        }
                    },
                    _ => !IsSaving && SelectedItem != null && SelectedStoreProduct != null && TransferQuantity > 0);

                // Pagination commands
                NextPageCommand = new RelayCommand(
                    _ => CurrentPage++,
                    _ => !IsLastPage && !IsSaving);

                PreviousPageCommand = new RelayCommand(
                    _ => CurrentPage--,
                    _ => !IsFirstPage && !IsSaving);

                GoToPageCommand = new RelayCommand<int>(
                    page => CurrentPage = page,
                    _ => !IsSaving);

                ChangePageSizeCommand = new RelayCommand<int>(
                    size => PageSize = size,
                    _ => !IsSaving);

                Debug.WriteLine("MainStockViewModel commands initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing commands: {ex.Message}");
                throw;
            }
        }
    }
}