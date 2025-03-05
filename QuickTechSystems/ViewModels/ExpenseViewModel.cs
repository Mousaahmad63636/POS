using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.Events;

namespace QuickTechSystems.WPF.ViewModels
{
    public class ExpenseViewModel : ViewModelBase
    {
        private readonly IExpenseService _expenseService;
        private ObservableCollection<ExpenseDTO> _expenses;
        private ExpenseDTO? _selectedExpense;
        private ExpenseDTO _currentExpense;
        private Action<EntityChangedEvent<ExpenseDTO>> _expenseChangedHandler;
        private ObservableCollection<string> _categories;
        private string _selectedCategory;
        private DateTime _filterStartDate = DateTime.Today;
        private ObservableCollection<CategorySummary> _categorySummaries;

        public ExpenseViewModel(
            IExpenseService expenseService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _expenseService = expenseService;
            _expenses = new ObservableCollection<ExpenseDTO>();
            _currentExpense = new ExpenseDTO { Date = DateTime.Today };
            _expenseChangedHandler = HandleExpenseChanged;
            _categorySummaries = new ObservableCollection<CategorySummary>();
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            DeleteCommand = new AsyncRelayCommand(async param => await DeleteAsync(param as ExpenseDTO));
            EditCommand = new RelayCommand(param => EditExpense(param as ExpenseDTO));
            ClearCommand = new RelayCommand(_ => ClearForm());

            Categories = new ObservableCollection<string>
        {
            "Internet Expenses",
            "Internet Salaries",
            "Internet Account",
            "Motor Expenses",
            "Motor Salaries"
        };
            ApplyFilterCommand = new RelayCommand(_ => ApplyFilter());

            InitializeCategories();
            _ = LoadDataAsync();
        }
        public ObservableCollection<string> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        public DateTime FilterStartDate
        {
            get => _filterStartDate;
            set => SetProperty(ref _filterStartDate, value);
        }

        public ObservableCollection<CategorySummary> CategorySummaries
        {
            get => _categorySummaries;
            set => SetProperty(ref _categorySummaries, value);
        }
        public ObservableCollection<ExpenseDTO> Expenses
        {
            get => _expenses;
            set => SetProperty(ref _expenses, value);
        }

        public ExpenseDTO? SelectedExpense
        {
            get => _selectedExpense;
            set => SetProperty(ref _selectedExpense, value);
        }

        public ExpenseDTO CurrentExpense
        {
            get => _currentExpense;
            set => SetProperty(ref _currentExpense, value);
        }
        public class CategorySummary
        {
            public string CategoryName { get; set; } = string.Empty;
            public int Count { get; set; }
            public decimal TotalAmount { get; set; }
            public bool IsTotal { get; set; }
        }



        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ApplyFilterCommand { get; }

        protected override void SubscribeToEvents()
        {
            _eventAggregator.Subscribe<EntityChangedEvent<ExpenseDTO>>(_expenseChangedHandler);
        }

        protected override void UnsubscribeFromEvents()
        {
            _eventAggregator.Unsubscribe<EntityChangedEvent<ExpenseDTO>>(_expenseChangedHandler);
        }

        private async void HandleExpenseChanged(EntityChangedEvent<ExpenseDTO> evt)
        {
            await LoadDataAsync();
        }

        protected override async Task LoadDataAsync()
        {
            try
            {
                var expenses = await _expenseService.GetAllAsync();
                Expenses = new ObservableCollection<ExpenseDTO>(expenses.OrderByDescending(e => e.Date));
                ApplyFilter(); // Apply the filter after loading expenses
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading expenses: {ex.Message}");
            }
        }
        private void ApplyFilter()
        {
            var filtered = Expenses.Where(e =>
                (string.IsNullOrEmpty(SelectedCategory) || e.Category == SelectedCategory) &&
                e.Date >= FilterStartDate).ToList();

            var summaries = filtered
                .GroupBy(e => e.Category)
                .Select(g => new CategorySummary
                {
                    CategoryName = g.Key,
                    Count = g.Count(),
                    TotalAmount = g.Sum(e => e.Amount)
                })
                .OrderBy(s => s.CategoryName)
                .ToList();

            // Add total row
            summaries.Add(new CategorySummary
            {
                CategoryName = "Total",
                Count = summaries.Sum(s => s.Count),
                TotalAmount = summaries.Sum(s => s.TotalAmount),
                IsTotal = true
            });

            CategorySummaries = new ObservableCollection<CategorySummary>(summaries);
        }
        private async Task SaveAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CurrentExpense.Reason))
                {
                    MessageBox.Show("Please enter a reason for the expense.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CurrentExpense.Amount <= 0)
                {
                    MessageBox.Show("Please enter a valid amount.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CurrentExpense.ExpenseId == 0)
                {
                    await _expenseService.CreateAsync(CurrentExpense);
                }
                else
                {
                    await _expenseService.UpdateAsync(CurrentExpense);
                }
                if (string.IsNullOrWhiteSpace(CurrentExpense.Category) || !Categories.Contains(CurrentExpense.Category))
                {
                    MessageBox.Show("Please select a valid category.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await LoadDataAsync();
                ClearForm();
                MessageBox.Show("Expense saved successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error saving expense: {ex.Message}");
            }
        }

        private async Task DeleteAsync(ExpenseDTO? expense)
        {
            if (expense == null) return;

            try
            {
                if (MessageBox.Show("Are you sure you want to delete this expense?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await _expenseService.DeleteAsync(expense.ExpenseId);
                    await LoadDataAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error deleting expense: {ex.Message}");
            }
        }

        private void EditExpense(ExpenseDTO? expense)
        {
            if (expense == null) return;
            CurrentExpense = new ExpenseDTO
            {
                ExpenseId = expense.ExpenseId,
                Reason = expense.Reason,
                Amount = expense.Amount,
                Date = expense.Date,
                Notes = expense.Notes,
                Category = expense.Category,
                IsRecurring = expense.IsRecurring,
                CreatedAt = expense.CreatedAt,
                UpdatedAt = DateTime.Now
            };
        }

        private void ClearForm()
        {
            CurrentExpense = new ExpenseDTO
            {
                Date = DateTime.Today,
                IsRecurring = false,
                Category = Categories.FirstOrDefault() ?? "Internet Expenses" // Set default category
            };
        }

        private void InitializeCategories()
        {
            if (!Categories.Contains("Other"))
            {
                Categories.Add("Other");
            }
        }
    }
}