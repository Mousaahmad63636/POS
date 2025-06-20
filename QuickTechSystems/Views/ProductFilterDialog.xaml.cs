using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.ViewModels;
using System.Threading.Tasks;

namespace QuickTechSystems.WPF.Views.Dialogs
{
    public partial class ProductFilterDialog : Window
    {
        private readonly ICategoryService _categoryService;
        public ProductFilterModel FilterModel { get; private set; }
        public bool FilterApplied { get; private set; }

        public ProductFilterDialog(ICategoryService categoryService, ProductFilterModel currentFilter = null)
        {
            InitializeComponent();
            _categoryService = categoryService;
            FilterModel = currentFilter ?? new ProductFilterModel();
            FilterApplied = false;

            Loaded += ProductFilterDialog_Loaded;
        }

        private async void ProductFilterDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCategoriesAsync();
            ApplyCurrentFilter();
            UpdateFilterSummary();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var categoryTasks = new[]
                {
                    _categoryService.GetByTypeAsync("Product"),
                    _categoryService.GetByTypeAsync("PlantsHardscape"),
                    _categoryService.GetByTypeAsync("LocalImported"),
                    _categoryService.GetByTypeAsync("IndoorOutdoor"),
                    _categoryService.GetByTypeAsync("PlantFamily"),
                    _categoryService.GetByTypeAsync("Detail")
                };

                var categoryResults = await Task.WhenAll(categoryTasks);

                var allOption = new CategoryDTO { CategoryId = 0, Name = "All" };

                CategoryComboBox.ItemsSource = new[] { allOption }.Concat(categoryResults[0]).ToList();
                PlantsHardscapeComboBox.ItemsSource = new[] { allOption }.Concat(categoryResults[1]).ToList();
                LocalImportedComboBox.ItemsSource = new[] { allOption }.Concat(categoryResults[2]).ToList();
                IndoorOutdoorComboBox.ItemsSource = new[] { allOption }.Concat(categoryResults[3]).ToList();
                PlantFamilyComboBox.ItemsSource = new[] { allOption }.Concat(categoryResults[4]).ToList();
                DetailComboBox.ItemsSource = new[] { allOption }.Concat(categoryResults[5]).ToList();

                CategoryComboBox.SelectedIndex = 0;
                PlantsHardscapeComboBox.SelectedIndex = 0;
                LocalImportedComboBox.SelectedIndex = 0;
                IndoorOutdoorComboBox.SelectedIndex = 0;
                PlantFamilyComboBox.SelectedIndex = 0;
                DetailComboBox.SelectedIndex = 0;

                CategoryComboBox.SelectionChanged += ComboBox_SelectionChanged;
                PlantsHardscapeComboBox.SelectionChanged += ComboBox_SelectionChanged;
                LocalImportedComboBox.SelectionChanged += ComboBox_SelectionChanged;
                IndoorOutdoorComboBox.SelectionChanged += ComboBox_SelectionChanged;
                PlantFamilyComboBox.SelectionChanged += ComboBox_SelectionChanged;
                DetailComboBox.SelectionChanged += ComboBox_SelectionChanged;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error loading categories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFilterSummary();
        }

        private void ApplyCurrentFilter()
        {
            if (FilterModel == null) return;

            SetComboBoxSelection(CategoryComboBox, FilterModel.SelectedCategory);
            SetComboBoxSelection(PlantsHardscapeComboBox, FilterModel.SelectedPlantsHardscape);
            SetComboBoxSelection(LocalImportedComboBox, FilterModel.SelectedLocalImported);
            SetComboBoxSelection(IndoorOutdoorComboBox, FilterModel.SelectedIndoorOutdoor);
            SetComboBoxSelection(PlantFamilyComboBox, FilterModel.SelectedPlantFamily);
            SetComboBoxSelection(DetailComboBox, FilterModel.SelectedDetail);
        }

        private void SetComboBoxSelection(ComboBox comboBox, CategoryDTO category)
        {
            if (category == null || comboBox.ItemsSource == null) return;

            var item = comboBox.ItemsSource.Cast<CategoryDTO>()
                .FirstOrDefault(c => c.CategoryId == category.CategoryId);
            if (item != null)
            {
                comboBox.SelectedItem = item;
            }
        }

        private void UpdateFilterSummary()
        {
            var filters = new List<string>();

            AddFilterSummary(filters, CategoryComboBox, "Category");
            AddFilterSummary(filters, PlantsHardscapeComboBox, "Plants/Hardscape");
            AddFilterSummary(filters, LocalImportedComboBox, "Local/Imported");
            AddFilterSummary(filters, IndoorOutdoorComboBox, "Indoor/Outdoor");
            AddFilterSummary(filters, PlantFamilyComboBox, "Plant Family");
            AddFilterSummary(filters, DetailComboBox, "Detail");

            if (filters.Any())
            {
                FilterSummaryText.Text = $"Active filters: {string.Join(", ", filters)}";
                FilterSummaryText.Visibility = Visibility.Visible;
            }
            else
            {
                FilterSummaryText.Visibility = Visibility.Collapsed;
            }
        }

        private void AddFilterSummary(List<string> filters, ComboBox comboBox, string categoryType)
        {
            if (comboBox.SelectedItem is CategoryDTO selected && selected.CategoryId > 0)
            {
                filters.Add($"{categoryType}: {selected.Name}");
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            CategoryComboBox.SelectedIndex = 0;
            PlantsHardscapeComboBox.SelectedIndex = 0;
            LocalImportedComboBox.SelectedIndex = 0;
            IndoorOutdoorComboBox.SelectedIndex = 0;
            PlantFamilyComboBox.SelectedIndex = 0;
            DetailComboBox.SelectedIndex = 0;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            FilterApplied = false;
            DialogResult = false;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            FilterModel.SelectedCategory = GetSelectedCategory(CategoryComboBox);
            FilterModel.SelectedPlantsHardscape = GetSelectedCategory(PlantsHardscapeComboBox);
            FilterModel.SelectedLocalImported = GetSelectedCategory(LocalImportedComboBox);
            FilterModel.SelectedIndoorOutdoor = GetSelectedCategory(IndoorOutdoorComboBox);
            FilterModel.SelectedPlantFamily = GetSelectedCategory(PlantFamilyComboBox);
            FilterModel.SelectedDetail = GetSelectedCategory(DetailComboBox);

            FilterApplied = true;
            DialogResult = true;
        }

        private CategoryDTO GetSelectedCategory(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is CategoryDTO selected && selected.CategoryId > 0)
            {
                return selected;
            }
            return null;
        }
    }
}