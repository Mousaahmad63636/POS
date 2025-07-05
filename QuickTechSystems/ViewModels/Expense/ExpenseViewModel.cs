using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.ViewModels.Expense
{
    public class ExpenseViewModel : ViewModelBase
    {
        private readonly IExpenseService _expenseService;
        private readonly ICategoryService _categoryService;

        private ObservableCollection<ExpenseDTO> _expenses;
        private ObservableCollection<ExpenseDTO> _filteredExpenses;
        private ObservableCollection<string> _categories;
        private ExpenseDTO _selectedExpense;
        private ExpenseDTO _currentExpense;

        private string _searchText = string.Empty;
        private string _selectedCategory = "All";
        private DateTime _startDate = DateTime.Today.AddDays(-30);
        private DateTime _endDate = DateTime.Today;
        private bool _isLoading;
        private bool _isEditMode;

        private decimal _totalAmount;
        private int _totalCount;
        private decimal _averageAmount;

        public ExpenseViewModel(
            IExpenseService expenseService,
            ICategoryService categoryService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _expenseService = expenseService;
            _categoryService = categoryService;

            _expenses = new ObservableCollection<ExpenseDTO>();
            _filteredExpenses = new ObservableCollection<ExpenseDTO>();
            _categories = new ObservableCollection<string>();
            _currentExpense = new ExpenseDTO { Date = DateTime.Today };

            InitializeCommands();
            LoadDataAsync();
        }

        #region Properties

        public ObservableCollection<ExpenseDTO> Expenses
        {
            get => _filteredExpenses;
            set => SetProperty(ref _filteredExpenses, value);
        }

        public ObservableCollection<string> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ExpenseDTO SelectedExpense
        {
            get => _selectedExpense;
            set => SetProperty(ref _selectedExpense, value);
        }

        public ExpenseDTO CurrentExpense
        {
            get => _currentExpense;
            set => SetProperty(ref _currentExpense, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilters();
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                    ApplyFilters();
            }
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value))
                    ApplyFilters();
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value))
                    ApplyFilters();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsEditMode
        {
            get => _isEditMode;
            set => SetProperty(ref _isEditMode, value);
        }

        public decimal TotalAmount
        {
            get => _totalAmount;
            set => SetProperty(ref _totalAmount, value);
        }

        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        public decimal AverageAmount
        {
            get => _averageAmount;
            set => SetProperty(ref _averageAmount, value);
        }

        #endregion

        #region Commands

        public ICommand AddCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        private void InitializeCommands()
        {
            AddCommand = new RelayCommand(ExecuteAdd);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteEdit);
            DeleteCommand = new AsyncRelayCommand(ExecuteDeleteAsync, CanExecuteDelete);
            SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, CanExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);
            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
        }

        #endregion

        #region Command Methods

        private void ExecuteAdd(object parameter)
        {
            CurrentExpense = new ExpenseDTO
            {
                Date = DateTime.Today,
                Category = Categories.FirstOrDefault(c => c != "All") ?? "General"
            };
            IsEditMode = true;
        }

        private void ExecuteEdit(object parameter)
        {
            if (SelectedExpense != null)
            {
                CurrentExpense = new ExpenseDTO
                {
                    ExpenseId = SelectedExpense.ExpenseId,
                    Reason = SelectedExpense.Reason,
                    Amount = SelectedExpense.Amount,
                    Date = SelectedExpense.Date,
                    Notes = SelectedExpense.Notes,
                    Category = SelectedExpense.Category,
                    IsRecurring = SelectedExpense.IsRecurring,
                    CreatedAt = SelectedExpense.CreatedAt,
                    UpdatedAt = SelectedExpense.UpdatedAt
                };
                IsEditMode = true;
            }
        }

        private async Task ExecuteDeleteAsync(object parameter)
        {
            if (SelectedExpense == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete the expense '{SelectedExpense.Reason}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    IsLoading = true;
                    await _expenseService.DeleteAsync(SelectedExpense.ExpenseId);
                    await LoadDataAsync();

                    MessageBox.Show("Expense deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting expense: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private async Task ExecuteSaveAsync(object parameter)
        {
            if (!ValidateCurrentExpense()) return;

            try
            {
                IsLoading = true;

                if (CurrentExpense.ExpenseId == 0)
                {
                    await _expenseService.CreateAsync(CurrentExpense);
                    MessageBox.Show("Expense created successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _expenseService.UpdateAsync(CurrentExpense);
                    MessageBox.Show("Expense updated successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                await LoadDataAsync();
                IsEditMode = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving expense: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExecuteCancel(object parameter)
        {
            IsEditMode = false;
            CurrentExpense = new ExpenseDTO { Date = DateTime.Today };
        }

        private bool CanExecuteEdit(object parameter) => SelectedExpense != null && !IsLoading;
        private bool CanExecuteDelete(object parameter) => SelectedExpense != null && !IsLoading;
        private bool CanExecuteSave(object parameter) => !IsLoading && ValidateCurrentExpense();

        #endregion

        #region Methods

        protected override async Task LoadDataAsync()
        {
            try
            {
                IsLoading = true;

                var expensesTask = _expenseService.GetAllAsync();
                var categoriesTask = LoadCategoriesAsync();

                await Task.WhenAll(expensesTask, categoriesTask);

                _expenses = new ObservableCollection<ExpenseDTO>(
                    (await expensesTask).OrderByDescending(e => e.Date));

                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var expenseCategories = await _categoryService.GetExpenseCategoriesAsync();

                Categories.Clear();
                Categories.Add("All");

                foreach (var category in expenseCategories.Where(c => c.IsActive))
                {
                    Categories.Add(category.Name);
                }

                if (!Categories.Contains("General"))
                    Categories.Add("General");
            }
            catch (Exception ex)
            {
                // Fallback categories
                Categories.Clear();
                var fallbackCategories = new[] { "All", "General", "Office Supplies", "Marketing", "Travel", "Utilities" };
                foreach (var category in fallbackCategories)
                {
                    Categories.Add(category);
                }
            }
        }

        private void ApplyFilters()
        {
            if (_expenses == null) return;

            var filtered = _expenses.Where(e =>
            {
                // Date filter
                if (e.Date < StartDate || e.Date > EndDate)
                    return false;

                // Category filter
                if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All" && e.Category != SelectedCategory)
                    return false;

                // Search filter
                if (!string.IsNullOrEmpty(SearchText))
                {
                    var searchLower = SearchText.ToLower();
                    var reasonMatch = e.Reason?.ToLower().Contains(searchLower) ?? false;
                    var categoryMatch = e.Category?.ToLower().Contains(searchLower) ?? false;
                    var notesMatch = !string.IsNullOrEmpty(e.Notes) && e.Notes.ToLower().Contains(searchLower);

                    if (!reasonMatch && !categoryMatch && !notesMatch)
                        return false;
                }

                return true;
            }).ToList();

            Expenses = new ObservableCollection<ExpenseDTO>(filtered);

            TotalAmount = filtered.Sum(e => e.Amount);
            TotalCount = filtered.Count;
            AverageAmount = TotalCount > 0 ? TotalAmount / TotalCount : 0;
        }

        private bool ValidateCurrentExpense()
        {
            if (CurrentExpense == null) return false;
            if (string.IsNullOrWhiteSpace(CurrentExpense.Reason)) return false;
            if (CurrentExpense.Amount <= 0) return false;
            if (string.IsNullOrWhiteSpace(CurrentExpense.Category)) return false;

            return true;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Expenses?.Clear();
                Categories?.Clear();
            }
            base.Dispose(disposing);
        }
    }
}