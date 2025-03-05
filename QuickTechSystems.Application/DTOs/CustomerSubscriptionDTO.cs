using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickTechSystems.Application.DTOs
{
    public class CustomerSubscriptionDTO : BaseDTO, INotifyPropertyChanged
    {
        private int _id;
        private string _name = string.Empty;
        private string _phoneNumber = string.Empty;
        private decimal _oldCounter;
        private decimal _newCounter;
        private decimal _billAmount;
        private DateTime _lastBillDate;
        private int _paymentCount;
        private decimal _totalPayments;
        private decimal _pricePerUnit = 1;
        private string _subscriptionTypeName = string.Empty;
        private decimal _additionalCharge;
        private int? _subscriptionTypeId;
        private decimal _totalBillWithCharges;

        public CustomerSubscriptionDTO()
        {
            LastBillDate = DateTime.Now;
            OldCounter = 0;
            NewCounter = 0;
            BillAmount = 0;
            PricePerUnit = 1;
        }

        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string PhoneNumber
        {
            get => _phoneNumber;
            set => SetProperty(ref _phoneNumber, value);
        }

        public decimal OldCounter
        {
            get => _oldCounter;
            set => SetProperty(ref _oldCounter, value);
        }

        public decimal NewCounter
        {
            get => _newCounter;
            set => SetProperty(ref _newCounter, value);
        }

        public decimal BillAmount
        {
            get => _billAmount;
            set
            {
                if (SetProperty(ref _billAmount, value))
                {
                    UpdateTotalBill();
                }
            }
        }

        public DateTime LastBillDate
        {
            get => _lastBillDate;
            set => SetProperty(ref _lastBillDate, value);
        }

        public int PaymentCount
        {
            get => _paymentCount;
            set => SetProperty(ref _paymentCount, value);
        }

        public decimal TotalPayments
        {
            get => _totalPayments;
            set => SetProperty(ref _totalPayments, value);
        }

        public decimal PricePerUnit
        {
            get => _pricePerUnit;
            set => SetProperty(ref _pricePerUnit, value);
        }

        public string SubscriptionTypeName
        {
            get => _subscriptionTypeName;
            set => SetProperty(ref _subscriptionTypeName, value);
        }

        public decimal AdditionalCharge
        {
            get => _additionalCharge;
            set
            {
                if (SetProperty(ref _additionalCharge, value))
                {
                    UpdateTotalBill();
                }
            }
        }

        public int? SubscriptionTypeId
        {
            get => _subscriptionTypeId;
            set => SetProperty(ref _subscriptionTypeId, value);
        }

        public decimal TotalBillWithCharges
        {
            get => _totalBillWithCharges;
            set => SetProperty(ref _totalBillWithCharges, value);
        }

        public bool IsValid(out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                errorMessage = "Customer name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(PhoneNumber))
            {
                errorMessage = "Phone number is required.";
                return false;
            }

            if (PricePerUnit <= 0)
            {
                errorMessage = "Price per unit must be greater than zero.";
                return false;
            }

            if (SubscriptionTypeId == null)
            {
                errorMessage = "Please select a subscription type.";
                return false;
            }

            if (NewCounter < OldCounter)
            {
                errorMessage = "New counter reading cannot be less than old counter reading.";
                return false;
            }

            if (LastBillDate > DateTime.Now)
            {
                errorMessage = "Last bill date cannot be in the future.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private void UpdateTotalBill()
        {
            TotalBillWithCharges = BillAmount + AdditionalCharge;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

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
    }
}