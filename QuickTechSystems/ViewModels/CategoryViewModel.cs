using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Services;

namespace QuickTechSystems.WPF.ViewModels
{
    public class CategoryViewModel : ViewModelBase
    {
        private readonly ICategoryService _categoryService;
        private readonly IGlobalOverlayService _overlayService;
        private ObservableCollection<CategoryDTO> _categories;
        private CategoryDTO? _selectedCategory;
        private bool _isEditing;
        private bool _isAddMode;
        private string _formTitle = "Add New Category";
        private Action<EntityChangedEvent<CategoryDTO>> _categoryChangedHandler;

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public CategoryDTO? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    IsEditing = value != null;
                }
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public bool IsAddMode
        {
            get => _isAddMode;
            set => SetProperty(ref _isAddMode, value);
        }

        public string FormTitle
        {
            get => _formTitle;
            set => SetProperty(ref _formTitle, value);
        }

        public ICommand LoadCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CancelCommand { get; }

        public CategoryViewModel(ICategoryService categoryService, IEventAggregator eventAggregator, IGlobalOverlayService overlayService)
            : base(eventAggregator)
        {
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
            _categories = new ObservableCollection<CategoryDTO>();
            _categoryChangedHandler = HandleCategoryChanged;

            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            AddCommand = new RelayCommand(_ => AddNew());
            EditCommand = new RelayCommand(param => Edit((CategoryDTO)param));
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            DeleteCommand = new AsyncRelayCommand(async _ => await DeleteAsync());
            CancelCommand = new RelayCommand(_ => Cancel());

            _ = LoadDataAsync();
        }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<CategoryDTO>>(_categoryChangedHandler);
        }

        private async void HandleCategoryChanged(EntityChangedEvent<CategoryDTO> evt)
        {
            try
            {
                Debug.WriteLine($"CategoryViewModel: Handling category change: {evt.Action}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (evt.Action)
                    {
                        case "Create":
                            if (!Categories.Any(c => c.CategoryId == evt.Entity.CategoryId))
                            {
                                Categories.Add(evt.Entity);
                                Debug.WriteLine($"Added new category {evt.Entity.Name}");
                            }
                            break;
                        case "Update":
                            var existingIndex = Categories.ToList().FindIndex(c => c.CategoryId == evt.Entity.CategoryId);
                            if (existingIndex != -1)
                            {
                                Categories[existingIndex] = evt.Entity;
                                Debug.WriteLine($"Updated category {evt.Entity.Name}");
                            }
                            break;
                        case "Delete":
                            var categoryToRemove = Categories.FirstOrDefault(c => c.CategoryId == evt.Entity.CategoryId);
                            if (categoryToRemove != null)
                            {
                                Categories.Remove(categoryToRemove);
                                Debug.WriteLine($"Removed category {categoryToRemove.Name}");
                            }
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CategoryViewModel: Error handling category change: {ex.Message}");
            }
        }

        protected override async Task LoadDataAsync()
        {
            try
            {
                var categories = await _categoryService.GetAllAsync();
                Categories = new ObservableCollection<CategoryDTO>(categories);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading categories: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddNew()
        {
            SelectedCategory = new CategoryDTO
            {
                IsActive = true
            };
            IsAddMode = true;
            _overlayService.ShowCategoryEditor(this);
        }

        private void Edit(CategoryDTO category)
        {
            SelectedCategory = category;
            IsAddMode = false;
            _overlayService.ShowCategoryEditor(this);
        }

        private async Task SaveAsync()
        {
            try
            {
                Debug.WriteLine("Starting category save operation");
                if (SelectedCategory == null) return;

                if (string.IsNullOrWhiteSpace(SelectedCategory.Name))
                {
                    MessageBox.Show("Category name is required.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (SelectedCategory.CategoryId == 0)
                {
                    var result = await _categoryService.CreateAsync(SelectedCategory);
                    Categories.Add(result);
                    Debug.WriteLine($"Publishing category created event for ID: {result.CategoryId}");
                    _eventAggregator.Publish(new EntityChangedEvent<CategoryDTO>("Create", result));
                }
                else
                {
                    Debug.WriteLine("Updating existing category");
                    await _categoryService.UpdateAsync(SelectedCategory);
                    var index = Categories.IndexOf(Categories.First(c => c.CategoryId == SelectedCategory.CategoryId));
                    Categories[index] = SelectedCategory;
                    _eventAggregator.Publish(new EntityChangedEvent<CategoryDTO>("Update", SelectedCategory));
                    Debug.WriteLine("Category update event published");
                }

                await LoadDataAsync();
                _overlayService.HideCategoryEditor();
                SelectedCategory = null;
                IsAddMode = false;
                Debug.WriteLine("Category data reloaded");
                MessageBox.Show("Category saved successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save error: {ex.Message}");
                MessageBox.Show($"Error saving category: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel()
        {
            _overlayService.HideCategoryEditor();
            if (IsAddMode)
            {
                SelectedCategory = null;
            }
            IsAddMode = false;
        }

        private async Task DeleteAsync()
        {
            try
            {
                if (SelectedCategory == null) return;

                if (MessageBox.Show("Are you sure you want to delete this category?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _categoryService.DeleteAsync(SelectedCategory.CategoryId);
                        await LoadDataAsync();
                        _overlayService.HideCategoryEditor();
                        SelectedCategory = null;
                        MessageBox.Show("Category deleted successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (InvalidOperationException ex)
                    {
                        MessageBox.Show(ex.Message, "Cannot Delete Category",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting category: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}