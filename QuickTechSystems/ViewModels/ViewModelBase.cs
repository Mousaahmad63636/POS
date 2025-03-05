// QuickTechSystems.WPF/ViewModels/ViewModelBase.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.Events;

namespace QuickTechSystems.WPF.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        protected readonly IEventAggregator _eventAggregator;
        private bool _disposed;

        public event PropertyChangedEventHandler? PropertyChanged;
        // In ViewModelBase.cs
        private FlowDirection _currentFlowDirection = FlowDirection.LeftToRight;

        public FlowDirection CurrentFlowDirection
        {
            get => _currentFlowDirection;
            set => SetProperty(ref _currentFlowDirection, value);
        }
        protected ViewModelBase(IEventAggregator eventAggregator)
        {
            Debug.WriteLine($"Initializing {GetType().Name}");
            _eventAggregator = eventAggregator;
            SubscribeToEvents();
        }
        protected async Task ShowSuccessMessage(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    message,
                    "Success",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
        }
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void SubscribeToEvents()
        {
            // Override in derived classes to subscribe to specific events
        }

        protected virtual void UnsubscribeFromEvents()
        {
            // Override in derived classes to unsubscribe from specific events
        }

        protected virtual async Task LoadDataAsync()
        {
            try
            {
                await Task.Run(async () =>
                {
                    await LoadDataImplementationAsync();
                });
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync(ex.Message);
            }
        }

        protected virtual async Task LoadDataImplementationAsync()
        {
            await Task.CompletedTask;
        }

        protected async Task ShowErrorMessageAsync(string message)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        protected async Task RunCommandAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync(ex.Message);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                UnsubscribeFromEvents();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}