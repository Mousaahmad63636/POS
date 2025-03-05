using QuickTechSystems.Application.Events;

namespace QuickTechSystems.WPF.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;

        public ICommand NavigateCommand { get; }

        public DashboardViewModel(IServiceProvider serviceProvider, IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _serviceProvider = serviceProvider;
            NavigateCommand = new RelayCommand(ExecuteNavigation);
        }

        private void ExecuteNavigation(object? parameter)
        {
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            mainViewModel.NavigateCommand.Execute(parameter);
        }
    }
}