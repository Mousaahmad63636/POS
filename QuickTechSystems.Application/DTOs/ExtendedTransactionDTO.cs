using System.Collections.ObjectModel;
using QuickTechSystems.Domain.Entities;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickTechSystems.Application.DTOs
{
    public class ExtendedTransactionDTO : BaseDTO, INotifyPropertyChanged
    {
        private int _transactionId;
        private int? _customerId;
        private string _customerName = string.Empty;
        private decimal _totalAmount;
        private decimal _paidAmount;
        private DateTime _transactionDate;
        private TransactionType _transactionType;
        private TransactionStatus _status;
        private string _paymentMethod = string.Empty;
        private string _cashierId = string.Empty;
        private string _cashierName = string.Empty;
        private ObservableCollection<TransactionDetailDTO> _details = new ObservableCollection<TransactionDetailDTO>();
        private string _cashierRole = string.Empty;
        private bool _isExpanded;

        public int TransactionId
        {
            get => _transactionId;
            set => SetProperty(ref _transactionId, value);
        }

        public int? CustomerId
        {
            get => _customerId;
            set => SetProperty(ref _customerId, value);
        }

        public string CustomerName
        {
            get => _customerName;
            set => SetProperty(ref _customerName, value);
        }

        public decimal TotalAmount
        {
            get => _totalAmount;
            set => SetProperty(ref _totalAmount, value);
        }

        public decimal PaidAmount
        {
            get => _paidAmount;
            set => SetProperty(ref _paidAmount, value);
        }

        public DateTime TransactionDate
        {
            get => _transactionDate;
            set => SetProperty(ref _transactionDate, value);
        }

        public TransactionType TransactionType
        {
            get => _transactionType;
            set => SetProperty(ref _transactionType, value);
        }

        public TransactionStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string PaymentMethod
        {
            get => _paymentMethod;
            set => SetProperty(ref _paymentMethod, value);
        }

        public string CashierId
        {
            get => _cashierId;
            set => SetProperty(ref _cashierId, value);
        }

        public string CashierName
        {
            get => _cashierName;
            set => SetProperty(ref _cashierName, value);
        }

        public ObservableCollection<TransactionDetailDTO> Details
        {
            get => _details;
            set => SetProperty(ref _details, value);
        }

        public string CashierRole
        {
            get => _cashierRole;
            set => SetProperty(ref _cashierRole, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
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
        public string DisplayTransactionType
        {
            get
            {
                if (TransactionType == TransactionType.Sale)
                {
                    return string.Equals(PaymentMethod, "debt", StringComparison.OrdinalIgnoreCase) ? "Debt" : "Sale";
                }
                return TransactionType.ToString();
            }
        }
        public static explicit operator ExtendedTransactionDTO(TransactionDTO dto)
        {
            return new ExtendedTransactionDTO
            {
                TransactionId = dto.TransactionId,
                CustomerId = dto.CustomerId,
                CustomerName = dto.CustomerName,
                TotalAmount = dto.TotalAmount,
                PaidAmount = dto.PaidAmount,
                TransactionDate = dto.TransactionDate,
                TransactionType = dto.TransactionType,
                Status = dto.Status,
                PaymentMethod = dto.PaymentMethod,
                CashierId = dto.CashierId,
                CashierName = dto.CashierName,
                Details = dto.Details,
                CashierRole = dto.CashierRole,
                CreatedAt = dto.CreatedAt,
                UpdatedAt = dto.UpdatedAt,
                IsActive = dto.IsActive,
                IsExpanded = false
            };
        }

        public static explicit operator TransactionDTO(ExtendedTransactionDTO dto)
        {
            return new TransactionDTO
            {
                TransactionId = dto.TransactionId,
                CustomerId = dto.CustomerId,
                CustomerName = dto.CustomerName,
                TotalAmount = dto.TotalAmount,
                PaidAmount = dto.PaidAmount,
                TransactionDate = dto.TransactionDate,
                TransactionType = dto.TransactionType,
                Status = dto.Status,
                PaymentMethod = dto.PaymentMethod,
                CashierId = dto.CashierId,
                CashierName = dto.CashierName,
                Details = dto.Details,
                CashierRole = dto.CashierRole,
                CreatedAt = dto.CreatedAt,
                UpdatedAt = dto.UpdatedAt,
                IsActive = dto.IsActive
            };
        }
    }
}