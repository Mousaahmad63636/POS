// QuickTechSystems.WPF/App.xaml.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Interfaces.Repositories;
using QuickTechSystems.Infrastructure.Data;
using QuickTechSystems.Infrastructure.Repositories;
using QuickTechSystems.Infrastructure.Services;
using QuickTechSystems.ViewModels;
using QuickTechSystems.Views;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Views;
using QuickTechSystems.WPF.Services;
using System;
using System.IO;
using System.Windows;
using QuickTechSystems.Application.Helpers;
using QuickTechSystems.Helpers;
using Microsoft.EntityFrameworkCore.Infrastructure;
using QuickTechSystems.Application.Interfaces;

namespace QuickTechSystems.WPF
{
    public partial class App : System.Windows.Application
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        public IServiceProvider ServiceProvider => _serviceProvider;

        public App()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Core Services
            services.AddSingleton<IEventAggregator, EventAggregator>();
            services.AddAutoMapper(typeof(MappingProfile));
            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<LanguageManager>();
            services.AddScoped<IActivityLogger, ActivityLogger>();


            // Database Context - Using DbContextPool for better management
            services.AddDbContextPool<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    _configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("QuickTechSystems.Infrastructure"))
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors());

            // DbContext Factory
            services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(provider =>
                new PooledDbContextFactory<ApplicationDbContext>(
                    new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseSqlServer(_configuration.GetConnectionString("DefaultConnection"),
                            b => b.MigrationsAssembly("QuickTechSystems.Infrastructure"))
                        .EnableSensitiveDataLogging()
                        .EnableDetailedErrors()
                        .Options));

            // Database Scope Service - Changed to Scoped to resolve DI issue
            services.AddScoped<IDbContextScopeService, DbContextScopeService>();

            // Repositories and Unit of Work
            services.AddTransient<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IBackupService, BackupService>();

            // Application Services
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IBarcodeService, BarcodeService>();
            services.AddScoped<IBusinessSettingsService, BusinessSettingsService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<ICustomerDebtService, CustomerDebtService>();
            services.AddScoped<IDrawerService, DrawerService>();
            services.AddScoped<IEmployeeService, EmployeeService>();
            services.AddScoped<IExpenseService, ExpenseService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IQuoteService, QuoteService>();
            services.AddScoped<ISupplierService, SupplierService>();
            services.AddScoped<ISystemPreferencesService, SystemPreferencesService>();
            services.AddScoped<ITransactionService, TransactionService>();
            services.AddScoped<ILowStockHistoryService, LowStockHistoryService>();

            // View Models
            services.AddScoped<MainViewModel>();
            services.AddScoped<LoginViewModel>();
            services.AddScoped<DashboardViewModel>();
            services.AddScoped<CategoryViewModel>();
            services.AddScoped<CustomerViewModel>();
            services.AddScoped<CustomerDebtViewModel>();
            services.AddScoped<DrawerViewModel>();
            services.AddScoped<EmployeeViewModel>();
            services.AddScoped<ExpenseViewModel>();
            services.AddScoped<ProductViewModel>();
            services.AddScoped<ProfitViewModel>();
            services.AddScoped<QuoteViewModel>();
            services.AddScoped<SettingsViewModel>();
            services.AddScoped<SupplierViewModel>();
            services.AddScoped<SystemPreferencesViewModel>();
            services.AddScoped<TransactionHistoryViewModel>();
            services.AddScoped<TransactionViewModel>();
            services.AddTransient<BulkProductViewModel>();
            services.AddScoped<IDamagedGoodsService, DamagedGoodsService>();
            services.AddScoped<LowStockHistoryViewModel>();

            // Views
            services.AddTransient<MainWindow>();
            services.AddTransient<LoginView>();
            services.AddTransient<CategoryView>();
            services.AddTransient<CustomerView>();
            services.AddTransient<CustomerDebtView>();
            services.AddTransient<DrawerView>();
            services.AddTransient<EmployeeView>();
            services.AddTransient<ExpenseView>();
            services.AddTransient<ProductView>();
            services.AddTransient<ProfitView>();
            services.AddTransient<QuoteView>();
            services.AddTransient<SettingsView>();
            services.AddTransient<SupplierView>();
            services.AddTransient<SystemPreferencesView>();
            services.AddTransient<TransactionHistoryView>();
            services.AddTransient<TransactionView>();
            services.AddTransient<QuantityDialog>();
            services.AddScoped<DamagedGoodsViewModel>();

        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logPath);
                File.WriteAllText(Path.Combine(logPath, "startup.log"), $"Application starting at {DateTime.Now}...");

                using (var scope = _serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var businessSettingsService = scope.ServiceProvider.GetRequiredService<IBusinessSettingsService>();
                    var systemPreferencesService = scope.ServiceProvider.GetRequiredService<ISystemPreferencesService>();
                    var languageManager = scope.ServiceProvider.GetRequiredService<LanguageManager>();

                    try
                    {
                        await context.Database.EnsureCreatedAsync();
                        DatabaseInitializer.Initialize(context);
                        DatabaseInitializer.SeedDefaultAdmin(context);

                        // Ensure default preferences are initialized
                        const string userId = "default";
                        var hasPreferences = await systemPreferencesService.GetPreferenceValueAsync(userId, "Initialized", "false");
                        if (hasPreferences != "true")
                        {
                            await systemPreferencesService.InitializeUserPreferencesAsync(userId);
                            await systemPreferencesService.SavePreferenceAsync(userId, "Initialized", "true");
                        }
                    }
                    catch (Exception dbEx)
                    {
                        MessageBox.Show(
                            $"Database initialization error: {dbEx.Message}\n\nPlease ensure SQL Server is installed and accessible with the provided credentials.",
                            "Database Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        Shutdown();
                        return;
                    }

                    try
                    {
                        var rateSetting = await businessSettingsService.GetByKeyAsync("ExchangeRate");
                        if (rateSetting != null && decimal.TryParse(rateSetting.Value, out decimal rate))
                        {
                            CurrencyHelper.UpdateExchangeRate(rate);
                        }

                        var defaultLanguage = await systemPreferencesService.GetPreferenceValueAsync("default", "Language", "en-US");
                        await languageManager.SetLanguage(defaultLanguage);
                    }
                    catch (Exception settingsEx)
                    {
                        File.AppendAllText(Path.Combine(logPath, "startup.log"), $"\nSettings error: {settingsEx.Message}");
                    }
                }

                var loginView = _serviceProvider.GetRequiredService<LoginView>();
                loginView.Show();
            }
            catch (Exception ex)
            {
                var errorMessage = $"An error occurred while starting the application: {ex.Message}\n\n" +
                                  "Please ensure:\n" +
                                  "1. SQL Server is installed and accessible\n" +
                                  "2. .NET 8.0 Desktop Runtime is installed\n" +
                                  "3. You have necessary permissions to access the application folder";

                MessageBox.Show(errorMessage, "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);

                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    File.AppendAllText(Path.Combine(logPath, "error.log"),
                        $"\n[{DateTime.Now}] Fatal startup error:\n{ex}\n");
                }
                catch
                {
                    // If we can't log the error, just shutdown
                }

                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}