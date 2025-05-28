// Application/DTOs/CustomerProductPriceViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickTechSystems.Application.DTOs
{
    public class CustomerProductPriceViewModel : INotifyPropertyChanged
    {
        private int _productId;
        private string _productName;
        private decimal _defaultPrice;
        private decimal _customPrice;

        public int ProductId
        {
            get => _productId;
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ProductName
        {
            get => _productName;
            set
            {
                if (_productName != value)
                {
                    _productName = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal DefaultPrice
        {
            get => _defaultPrice;
            set
            {
                if (_defaultPrice != value)
                {
                    _defaultPrice = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal CustomPrice
        {
            get => _customPrice;
            set
            {
                if (_customPrice != value)
                {
                    _customPrice = value;
                    OnPropertyChanged();

                    // When custom price changes, notify that related properties might have changed
                    // Uncomment if you have calculated properties that depend on CustomPrice
                    // OnPropertyChanged(nameof(PriceChangePercentage)); 
                }
            }
        }

        // Optional calculated property example: 
        // public decimal PriceChangePercentage => DefaultPrice > 0 ? (CustomPrice - DefaultPrice) / DefaultPrice * 100 : 0;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}