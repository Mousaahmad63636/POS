using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class DrawerViewModel
    {
        public async Task RefreshDrawerDataAsync()
        {
            try
            {
                IsProcessing = true;

                CurrentDrawer = await _drawerService.GetCurrentDrawerAsync();
                if (CurrentDrawer == null)
                {
                    DrawerHistory.Clear();
                    UpdateStatus();
                    return;
                }

                await LoadDrawerHistoryAsync();
                await LoadFinancialOverviewAsync();
                UpdateStatus();
                UpdateTotals();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing drawer data: {ex.Message}");
                await ShowErrorMessageAsync("Error refreshing drawer data");
            }
            finally
            {
                IsProcessing = false;
            }
        }
    }
}