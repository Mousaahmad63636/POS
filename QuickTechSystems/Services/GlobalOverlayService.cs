using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.Services
{
    public interface IGlobalOverlayService
    {
        void ShowProductEditor(object viewModel);
        void HideProductEditor();
        void ShowCategoryEditor(object viewModel);
        void HideCategoryEditor();
        void ShowEmployeeEditor(object viewModel);
        void HideEmployeeEditor();
        void ShowCustomerEditor(object viewModel);
        void HideCustomerEditor();
    }

    public class GlobalOverlayService : IGlobalOverlayService
    {
        public void ShowProductEditor(object viewModel)
        {
            try
            {
                Debug.WriteLine("ShowProductEditor called");

                // Try to find MainWindow more reliably
                var mainWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                if (mainWindow == null)
                {
                    Debug.WriteLine("ERROR: MainWindow not found");
                    MessageBox.Show("Could not find MainWindow instance", "Error");
                    return;
                }

                var editorControl = new Controls.ProductEditorControl();
                // Important: Set DataContext AFTER creating the control
                editorControl.DataContext = viewModel;

                var overlay = mainWindow.FindName("PART_GlobalProductOverlay") as Grid;
                var content = mainWindow.FindName("PART_ProductEditorContent") as ContentControl;

                if (overlay == null || content == null)
                {
                    Debug.WriteLine("ERROR: Overlay or Content control not found");
                    MessageBox.Show("Overlay controls not found in MainWindow", "Error");
                    return;
                }

                content.Content = editorControl;
                overlay.Visibility = Visibility.Visible;

                Debug.WriteLine("Overlay should be visible now");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowProductEditor: {ex.Message}");
                MessageBox.Show($"Error showing product editor: {ex.Message}", "Error");
            }
        }
        public void ShowCustomerEditor(object viewModel)
        {
            try
            {
                Debug.WriteLine("ShowCustomerEditor called");

                // Try to find MainWindow
                var mainWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                if (mainWindow == null)
                {
                    Debug.WriteLine("ERROR: MainWindow not found");
                    MessageBox.Show("Could not find MainWindow instance", "Error");
                    return;
                }

                var editorControl = new Controls.CustomerEditorControl();
                // Set DataContext AFTER creating the control
                editorControl.DataContext = viewModel;

                var overlay = mainWindow.FindName("PART_GlobalProductOverlay") as Grid;
                var content = mainWindow.FindName("PART_ProductEditorContent") as ContentControl;

                if (overlay == null || content == null)
                {
                    Debug.WriteLine("ERROR: Overlay or Content control not found");
                    MessageBox.Show("Overlay controls not found in MainWindow", "Error");
                    return;
                }

                content.Content = editorControl;
                overlay.Visibility = Visibility.Visible;

                Debug.WriteLine("Customer overlay should be visible now");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowCustomerEditor: {ex.Message}");
                MessageBox.Show($"Error showing customer editor: {ex.Message}", "Error");
            }
        }
        public void HideCustomerEditor()
        {
            // We can reuse the same method as HideProductEditor since they use the same overlay
            HideProductEditor();
        }
        public void HideProductEditor()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWindow == null) return;

                var overlay = mainWindow.FindName("PART_GlobalProductOverlay") as Grid;
                var content = mainWindow.FindName("PART_ProductEditorContent") as ContentControl;

                if (overlay != null)
                    overlay.Visibility = Visibility.Collapsed;

                if (content != null)
                    content.Content = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HideProductEditor: {ex.Message}");
            }
        }

        public void ShowCategoryEditor(object viewModel)
        {
            try
            {
                Debug.WriteLine("ShowCategoryEditor called");

                // Try to find MainWindow more reliably
                var mainWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                if (mainWindow == null)
                {
                    Debug.WriteLine("ERROR: MainWindow not found");
                    MessageBox.Show("Could not find MainWindow instance", "Error");
                    return;
                }

                var editorControl = new Controls.CategoryEditorControl();
                // Important: Set DataContext AFTER creating the control
                editorControl.DataContext = viewModel;

                var overlay = mainWindow.FindName("PART_GlobalProductOverlay") as Grid;
                var content = mainWindow.FindName("PART_ProductEditorContent") as ContentControl;

                if (overlay == null || content == null)
                {
                    Debug.WriteLine("ERROR: Overlay or Content control not found");
                    MessageBox.Show("Overlay controls not found in MainWindow", "Error");
                    return;
                }

                content.Content = editorControl;
                overlay.Visibility = Visibility.Visible;

                Debug.WriteLine("Category overlay should be visible now");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowCategoryEditor: {ex.Message}");
                MessageBox.Show($"Error showing category editor: {ex.Message}", "Error");
            }
        }

        public void HideCategoryEditor()
        {
            // We can reuse the same method as HideProductEditor since they use the same overlay
            HideProductEditor();
        }

        public void ShowEmployeeEditor(object viewModel)
        {
            try
            {
                Debug.WriteLine("ShowEmployeeEditor called");

                // Try to find MainWindow more reliably
                var mainWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                if (mainWindow == null)
                {
                    Debug.WriteLine("ERROR: MainWindow not found");
                    MessageBox.Show("Could not find MainWindow instance", "Error");
                    return;
                }

                var editorControl = new Controls.EmployeeEditorControl();
                // Important: Set DataContext AFTER creating the control
                editorControl.DataContext = viewModel;

                var overlay = mainWindow.FindName("PART_GlobalProductOverlay") as Grid;
                var content = mainWindow.FindName("PART_ProductEditorContent") as ContentControl;

                if (overlay == null || content == null)
                {
                    Debug.WriteLine("ERROR: Overlay or Content control not found");
                    MessageBox.Show("Overlay controls not found in MainWindow", "Error");
                    return;
                }

                content.Content = editorControl;
                overlay.Visibility = Visibility.Visible;

                Debug.WriteLine("Employee overlay should be visible now");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowEmployeeEditor: {ex.Message}");
                MessageBox.Show($"Error showing employee editor: {ex.Message}", "Error");
            }
        }

        public void HideEmployeeEditor()
        {
            // We can reuse the same method as HideProductEditor since they use the same overlay
            HideProductEditor();
        }
    }
}