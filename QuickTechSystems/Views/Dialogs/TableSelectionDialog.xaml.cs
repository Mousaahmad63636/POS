using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.Views.Dialogs
{
    public partial class TableSelectionDialog : Window, INotifyPropertyChanged
    {
        private ObservableCollection<RestaurantTableDTO> _tables;
        private RestaurantTableDTO _selectedTable;

        public ObservableCollection<RestaurantTableDTO> Tables
        {
            get => _tables;
            set
            {
                _tables = value;
                OnPropertyChanged();
            }
        }

        public RestaurantTableDTO SelectedTable
        {
            get => _selectedTable;
            set
            {
                _selectedTable = value;
                OnPropertyChanged();
            }
        }

        public ICommand SelectTableCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<RestaurantTableDTO> TableSelected;
        public event EventHandler NewTransactionRequested;

        public TableSelectionDialog(ObservableCollection<RestaurantTableDTO> tables)
        {
            InitializeComponent();
            DataContext = this;

            Tables = tables;
            SelectTableCommand = new RelayCommand<RestaurantTableDTO>(OnTableSelected);
        }

        private void OnTableSelected(RestaurantTableDTO table)
        {
            if (table == null) return;

            SelectedTable = table;
            TableSelected?.Invoke(this, table);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void NewTransactionButton_Click(object sender, RoutedEventArgs e)
        {
            NewTransactionRequested?.Invoke(this, EventArgs.Empty);
            DialogResult = false;
            Close();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}