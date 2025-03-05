using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickTechSystems.Application.DTOs
{
    public class CounterHistoryDTO : INotifyPropertyChanged
    {
        private decimal _oldCounter;
        private decimal _newCounter;
        private decimal _billAmount;
        private decimal _unitsUsed;

        public int Id { get; set; }
        public int CustomerSubscriptionId { get; set; }
        public string CustomerName { get; set; } = string.Empty;

        public decimal OldCounter
        {
            get => _oldCounter;
            set
            {
                if (_oldCounter != value)
                {
                    _oldCounter = value;
                    OnPropertyChanged();
                    UpdateCalculations();
                }
            }
        }

        public decimal NewCounter
        {
            get => _newCounter;
            set
            {
                if (_newCounter != value)
                {
                    _newCounter = value;
                    OnPropertyChanged();
                    UpdateCalculations();
                }
            }
        }

        public decimal BillAmount
        {
            get => _billAmount;
            set
            {
                if (_billAmount != value)
                {
                    _billAmount = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime RecordDate { get; set; }
        public decimal PricePerUnit { get; set; }
        public decimal AdditionalFees { get; set; }

        public decimal UnitsUsed
        {
            get => _unitsUsed;
            set
            {
                if (_unitsUsed != value)
                {
                    _unitsUsed = value;
                    OnPropertyChanged();
                }
            }
        }

        private void UpdateCalculations()
        {
            UnitsUsed = NewCounter - OldCounter;
            BillAmount = UnitsUsed * PricePerUnit + AdditionalFees;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}