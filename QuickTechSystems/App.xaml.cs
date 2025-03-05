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
using QuickTechSystems.ViewModels;
using QuickTechSystems.Views;
using QuickTechSystems.WPF.ViewModels;
using QuickTechSystems.WPF.Views;
using QuickTechSystems.Infrastructure.Services;
using System.Windows;
using System.IO;
using QuickTechSystems.Application.Helpers;
using QuickTechSystems.Helpers;
using QuickTechSystems.WPF.Services;



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
            services.AddSingleton<IGlobalOverlayService, GlobalOverlayService>();

            // Database
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    _configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("QuickTechSystems.Infrastructure"))
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors());

            // Repositories
            services.AddTransient<MonthlySubscriptionViewModel>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            // In ConfigureServices method
            services.AddScoped<IBackupService, BackupService>();
            // Application Services
            services.AddScoped<IBarcodeService, BarcodeService>();
            services.AddScoped<IBusinessSettingsService, BusinessSettingsService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<ISupplierService, SupplierService>();
            services.AddScoped<ISystemPreferencesService, SystemPreferencesService>();
            services.AddScoped<ITransactionService, TransactionService>();
            services.AddScoped<CustomerDebtViewModel>();
            // In ConfigureServices method
            services.AddScoped<ICustomerSubscriptionService, CustomerSubscriptionService>();
            services.AddScoped<DashboardViewModel>();
            services.AddTransient<BulkProductViewModel>();
            services.AddTransient<QuantityDialog>();
            // View Models
            // In ConfigureServices method
            services.AddSingleton<IGlobalOverlayService, GlobalOverlayService>();
            services.AddScoped<CategoryViewModel>();
            services.AddScoped<MainViewModel>();
            services.AddScoped<CategoryViewModel>();
            services.AddScoped<CustomerViewModel>();
            services.AddScoped<ProductViewModel>();
            services.AddScoped<SettingsViewModel>();
            // In ConfigureServices method
            services.AddSingleton<IGlobalOverlayService, GlobalOverlayService>();
            services.AddScoped<EmployeeViewModel>();
            // In ConfigureServices method
            services.AddSingleton<QuickTechSystems.WPF.Services.IGlobalOverlayService, QuickTechSystems.WPF.Services.GlobalOverlayService>();
            services.AddScoped<SupplierViewModel>();
            services.AddScoped<SystemPreferencesViewModel>();
            services.AddScoped<TransactionHistoryViewModel>();
            services.AddScoped<TransactionViewModel>();
            services.AddScoped<ProfitViewModel>();
            services.AddScoped<IExpenseService, ExpenseService>();
            services.AddScoped<IDrawerService, DrawerService>();
            // Views
            services.AddScoped<IMonthlySubscriptionSettingsService, MonthlySubscriptionSettingsService>();
            services.AddScoped<ISubscriptionTypeService, SubscriptionTypeService>();
            services.AddTransient<MonthlySubscriptionView>();
            services.AddTransient<MainWindow>();
            services.AddTransient<CategoryView>();
            services.AddTransient<CustomerView>();
            services.AddTransient<ProductView>();
            services.AddTransient<SettingsView>();
            services.AddTransient<SupplierView>();
            services.AddTransient<SystemPreferencesView>();
            services.AddTransient<TransactionHistoryView>();
            services.AddTransient<TransactionView>();
            services.AddTransient<CustomerDebtView>();
            services.AddScoped<ExpenseViewModel>();
            services.AddScoped<DrawerViewModel>();
            services.AddScoped<MonthlySubscriptionViewModel>();


            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IEmployeeService, EmployeeService>();
            services.AddScoped<LoginViewModel>();
            services.AddScoped<EmployeeViewModel>();
            services.AddTransient<LoginView>();
            services.AddTransient<EmployeeView>();



            services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlServer(
            _configuration.GetConnectionString("DefaultConnection"),
            b => b.MigrationsAssembly("QuickTechSystems.Infrastructure"))
        .EnableSensitiveDataLogging()
        .EnableDetailedErrors());



            services.AddSingleton<LanguageManager>();
            services.AddScoped<IDrawerService, DrawerService>();
            services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlServer(
        _configuration.GetConnectionString("DefaultConnection"))
        );
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Create a log directory if it doesn't exist
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logPath);
                File.WriteAllText(Path.Combine(logPath, "startup.log"), $"Application starting at {DateTime.Now}...");

                using (var scope = _serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var businessSettingsService = scope.ServiceProvider.GetRequiredService<IBusinessSettingsService>();
                    var systemPreferencesService = scope.ServiceProvider.GetRequiredService<ISystemPreferencesService>();
                    var languageManager = scope.ServiceProvider.GetRequiredService<LanguageManager>();

                    // Ensure database exists and is up to date
                    try
                    {
                        File.AppendAllText(Path.Combine(logPath, "startup.log"), "\nChecking database...");
                        await context.Database.EnsureCreatedAsync();

                        // Initialize database if needed
                        DatabaseInitializer.Initialize(context);
                        DatabaseInitializer.SeedDefaultAdmin(context);
                        File.AppendAllText(Path.Combine(logPath, "startup.log"), "\nDatabase initialized successfully.");
                    }
                    catch (Exception dbEx)
                    {
                        File.AppendAllText(Path.Combine(logPath, "startup.log"), $"\nDatabase error: {dbEx.Message}");
                        MessageBox.Show($"Database initialization error: {dbEx.Message}\n\nPlease ensure SQL Server LocalDB is installed.",
                            "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Shutdown();
                        return;
                    }

                    try
                    {
                        // Load exchange rate
                        var rateSetting = await businessSettingsService.GetByKeyAsync("ExchangeRate");
                        if (rateSetting != null && decimal.TryParse(rateSetting.Value, out decimal rate))
                        {
                            CurrencyHelper.UpdateExchangeRate(rate);
                        }

                        // Load default language
                        var defaultLanguage = await systemPreferencesService.GetPreferenceValueAsync("default", "Language", "en-US");
                        await languageManager.SetLanguage(defaultLanguage);

                        File.AppendAllText(Path.Combine(logPath, "startup.log"), "\nSettings loaded successfully.");
                    }
                    catch (Exception settingsEx)
                    {
                        File.AppendAllText(Path.Combine(logPath, "startup.log"), $"\nSettings error: {settingsEx.Message}");
                        // Continue with default settings if there's an error
                    }
                }

                var loginView = _serviceProvider.GetRequiredService<LoginView>();
                loginView.Show();

                File.AppendAllText(Path.Combine(logPath, "startup.log"), "\nApplication started successfully.");
            }
            catch (Exception ex)
            {
                var errorMessage = $"An error occurred while starting the application: {ex.Message}\n\n" +
                                  "Please ensure:\n" +
                                  "1. SQL Server LocalDB is installed\n" +
                                  "2. .NET 8.0 Desktop Runtime is installed\n" +
                                  "3. You have necessary permissions to access the application folder";

                MessageBox.Show(errorMessage, "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Log the full error details
                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    File.AppendAllText(Path.Combine(logPath, "error.log"),
                        $"\n[{DateTime.Now}] Fatal startup error:\n{ex}\n");
                }
                catch
                {
                    // If we can't even log the error, just shutdown
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