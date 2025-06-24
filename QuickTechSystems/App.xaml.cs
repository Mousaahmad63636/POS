using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Infrastructure.Data;
using QuickTechSystems.Views;
using QuickTechSystems.WPF.Views;
using QuickTechSystems.WPF.Services;
using System;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Markup;
using QuickTechSystems.Domain.Interfaces;
using QuickTechSystems.Commands;
using QuickTechSystems.ViewModels.Drawer;
using QuickTechSystems.ViewModels.Customer;
using QuickTechSystems.ViewModels.Login;
using QuickTechSystems.ViewModels.Product;
using QuickTechSystems.ViewModels.Supplier;
using QuickTechSystems.ViewModels.Settings;
using QuickTechSystems.ViewModels.Categorie;
using QuickTechSystems.ViewModels.Employee;
using QuickTechSystems.ViewModels.Expense;
using QuickTechSystems.ViewModels.Restaurent;
using QuickTechSystems.ViewModels.Welcome;
using QuickTechSystems.ViewModels.Transaction;
using QuickTechSystems.ViewModels;
using System.Threading;

namespace QuickTechSystems.WPF
{
    public partial class App : System.Windows.Application
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IEventAggregator _eventAggregator;
        private readonly Dictionary<string, object> ApplicationProperties;
        private static readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);

        public IServiceProvider ServiceProvider => _serviceProvider;

        public App()
        {
            InitializeComponent();

            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            ApplicationProperties = new Dictionary<string, object>();

            _configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            _eventAggregator = _serviceProvider.GetRequiredService<IEventAggregator>();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IConfiguration>(_configuration);
            services.AddSingleton<IEventAggregator, EventAggregator>();
            services.AddAutoMapper(typeof(MappingProfile));
            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<LanguageManager>();
            services.AddScoped<IPrinterService, PrinterService>();

            services.AddDbContextPool<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    _configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("QuickTechSystems.Infrastructure"))
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors(),
                poolSize: 128);

            services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(provider =>
                new PooledDbContextFactory<ApplicationDbContext>(
                    new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseSqlServer(_configuration.GetConnectionString("DefaultConnection"),
                            b => b.MigrationsAssembly("QuickTechSystems.Infrastructure"))
                        .EnableSensitiveDataLogging()
                        .EnableDetailedErrors()
                        .Options));

            services.AddScoped<IDbContextScopeService, DbContextScopeService>();
            services.AddTransient<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IBackupService, BackupService>();

            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IBarcodeService, BarcodeService>();
            services.AddScoped<IBusinessSettingsService, BusinessSettingsService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<IDrawerService, DrawerService>();
            services.AddScoped<IEmployeeService, EmployeeService>();
            services.AddScoped<IExpenseService, ExpenseService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<ISupplierService, SupplierService>();
            services.AddScoped<ISystemPreferencesService, SystemPreferencesService>();
            services.AddScoped<ISupplierInvoiceService, SupplierInvoiceService>();
            services.AddScoped<ITransactionService, TransactionService>();

            services.AddScoped<IRestaurantTableService, RestaurantTableService>();
            services.AddScoped<TableManagementViewModel>();
            services.AddTransient<TableManagementView>();

            services.AddScoped<IGenericRepository<SupplierInvoice>>(provider =>
                provider.GetRequiredService<IUnitOfWork>().SupplierInvoices);
            services.AddScoped<IGenericRepository<SupplierInvoiceDetail>>(provider =>
                provider.GetRequiredService<IUnitOfWork>().SupplierInvoiceDetails);
            services.AddScoped<IGenericRepository<Transaction>>(provider =>
                provider.GetRequiredService<IUnitOfWork>().Transactions);

            services.AddScoped<SplashScreenViewModel>();
            services.AddSingleton<IImagePathService, ImagePathService>();
            services.AddTransient<SplashScreenView>();

            services.AddScoped<MainViewModel>();
            services.AddScoped<LoginViewModel>();
            services.AddScoped<CategoryViewModel>();
            services.AddScoped<CustomerViewModel>();
            services.AddScoped<DrawerViewModel>();
            services.AddScoped<EmployeeViewModel>();
            services.AddScoped<ExpenseViewModel>();
            services.AddScoped<ProductViewModel>();
            services.AddScoped<SettingsViewModel>();
            services.AddScoped<SupplierViewModel>();
            services.AddScoped<SystemPreferencesViewModel>();
            services.AddScoped<WelcomeViewModel>();
            services.AddScoped<TransactionHistoryViewModel>();
            services.AddScoped<TransactionDetailsPopupViewModel>();
            services.AddTransient<SupplierInvoiceViewModel>();

            services.AddTransient<MainWindow>();
            services.AddTransient<LoginView>();
            services.AddTransient<CategoryView>();
            services.AddTransient<CustomerView>();
            services.AddTransient<DrawerView>();
            services.AddTransient<EmployeeView>();
            services.AddTransient<ExpenseView>();
            services.AddTransient<ProductView>();
            services.AddTransient<SettingsView>();
            services.AddTransient<SupplierView>();
            services.AddTransient<SystemPreferencesView>();
            services.AddTransient<QuickTechSystems.WPF.Views.WelcomeView>();
            services.AddTransient<TransactionHistoryView>();
            services.AddTransient<QuickTechSystems.Views.TransactionDetailsPopup>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    XmlLanguage.GetLanguage(CultureInfo.InvariantCulture.IetfLanguageTag)));

            base.OnStartup(e);

            await _initializationLock.WaitAsync();
            try
            {
                var splashViewModel = _serviceProvider.GetRequiredService<SplashScreenViewModel>();
                var splashView = _serviceProvider.GetRequiredService<SplashScreenView>();
                splashView.Show();

                var logDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDirectoryPath);
                File.WriteAllText(Path.Combine(logDirectoryPath, "startup.log"), $"Application starting at {DateTime.Now}...");

                splashViewModel.UpdateStatus("Initializing database...");

                await InitializeDatabaseAsync(splashViewModel);
                await LoadApplicationSettingsAsync(splashViewModel);

                splashViewModel.UpdateStatus("Launching application...");
                await Task.Delay(800);

                Dispatcher.Invoke(() =>
                {
                    var loginView = _serviceProvider.GetRequiredService<LoginView>();
                    loginView.Show();
                    splashView.Close();
                });
            }
            catch (Exception startupException)
            {
                await HandleStartupError(startupException);
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private async Task InitializeDatabaseAsync(SplashScreenViewModel splashViewModel)
        {
            using var scope = _serviceProvider.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

            try
            {
                splashViewModel.UpdateStatus("Setting up database infrastructure...");

                using var context = contextFactory.CreateDbContext();
                await context.InitializeDatabaseAsync();

                splashViewModel.UpdateStatus("Creating default admin account...");
                await context.SeedDefaultAdministratorAsync();

                splashViewModel.UpdateStatus("Setting up system preferences...");
                var systemPreferencesService = scope.ServiceProvider.GetRequiredService<ISystemPreferencesService>();
                const string defaultUserId = "default";
                var userPreferencesInitialized = await systemPreferencesService.GetPreferenceValueAsync(defaultUserId, "Initialized", "false");
                if (userPreferencesInitialized != "true")
                {
                    await systemPreferencesService.InitializeUserPreferencesAsync(defaultUserId);
                    await systemPreferencesService.SavePreferenceAsync(defaultUserId, "Initialized", "true");
                }
            }
            catch (Exception dbEx)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Database initialization error: {dbEx.Message}\n\nPlease ensure SQL Server is installed and accessible with the provided credentials.",
                        "Database Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown();
                });
                throw;
            }
        }

        private async Task LoadApplicationSettingsAsync(SplashScreenViewModel splashViewModel)
        {
            using var scope = _serviceProvider.CreateScope();

            try
            {
                splashViewModel.UpdateStatus("Loading business settings...");
                var businessSettingsService = scope.ServiceProvider.GetRequiredService<IBusinessSettingsService>();
                var exchangeRateSetting = await businessSettingsService.GetByKeyAsync("ExchangeRate");
                if (exchangeRateSetting != null && decimal.TryParse(exchangeRateSetting.Value, out decimal exchangeRate))
                {
                    CurrencyHelper.UpdateExchangeRate(exchangeRate);
                }

                var restaurantModeConfiguration = "false";
                var isRestaurantModeEnabled = false;

                try
                {
                    splashViewModel.UpdateStatus("Loading user preferences...");
                    var systemPreferencesService = scope.ServiceProvider.GetRequiredService<ISystemPreferencesService>();
                    restaurantModeConfiguration = await systemPreferencesService.GetPreferenceValueAsync("default", "RestaurantMode", "false");
                    isRestaurantModeEnabled = bool.Parse(restaurantModeConfiguration);
                    Debug.WriteLine($"Loaded RestaurantMode preference: {isRestaurantModeEnabled}");
                }
                catch (Exception preferenceException)
                {
                    Debug.WriteLine($"Error loading restaurant mode preference: {preferenceException.Message}");
                }

                try
                {
                    splashViewModel.UpdateStatus("Setting language preferences...");
                    var systemPreferencesService = scope.ServiceProvider.GetRequiredService<ISystemPreferencesService>();
                    var languageManager = scope.ServiceProvider.GetRequiredService<LanguageManager>();
                    var defaultLanguageConfiguration = await systemPreferencesService.GetPreferenceValueAsync("default", "Language", "en-US");

                    await Dispatcher.Invoke(async () =>
                    {
                        await languageManager.SetLanguage(defaultLanguageConfiguration);
                    });
                }
                catch (Exception languageException)
                {
                    Debug.WriteLine($"Error setting language: {languageException.Message}");
                }

                if (isRestaurantModeEnabled)
                {
                    ApplicationProperties["RestaurantModeEnabled"] = true;
                }
            }
            catch (Exception settingsException)
            {
                var logDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                File.AppendAllText(Path.Combine(logDirectoryPath, "startup.log"), $"\nSettings error: {settingsException.Message}");
                Debug.WriteLine($"Settings error: {settingsException.Message}");
            }
        }

        private async Task HandleStartupError(Exception startupException)
        {
            var errorMessage = $"An error occurred while starting the application: {startupException.Message}\n\n" +
                              "Please ensure:\n" +
                              "1. SQL Server is installed and accessible\n" +
                              "2. .NET 8.0 Desktop Runtime is installed\n" +
                              "3. You have necessary permissions to access the application folder";

            MessageBox.Show(errorMessage, "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);

            try
            {
                var logDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                File.AppendAllText(Path.Combine(logDirectoryPath, "error.log"),
                    $"\n[{DateTime.Now}] Fatal startup error:\n{startupException}\n");
            }
            catch
            {
            }

            Shutdown();
        }

        public void ApplyRestaurantModeSetting()
        {
            if (ApplicationProperties.ContainsKey("RestaurantModeEnabled") && (bool)ApplicationProperties["RestaurantModeEnabled"])
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _eventAggregator.Publish(new ApplicationModeChangedEvent(true));
                        Debug.WriteLine("Published ApplicationModeChangedEvent: true");
                    }
                    catch (Exception eventException)
                    {
                        Debug.WriteLine($"Error publishing restaurant mode event: {eventException.Message}");
                    }
                });
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            if (_serviceProvider is IDisposable disposableServiceProvider)
            {
                disposableServiceProvider.Dispose();
            }

            _initializationLock?.Dispose();
        }
    }
}