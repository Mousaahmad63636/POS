using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickTechSystems.Application.DTOs
{
    public class ProductDTO : BaseDTO, INotifyPropertyChanged
    {
        private int _productId;
        private string _barcode = string.Empty;
        private string _name = string.Empty;
        private string? _description;
        private int _categoryId;
        private string _categoryName = string.Empty;
        private int? _supplierId;
        private string _supplierName = string.Empty;
        private decimal _purchasePrice;
        private decimal _salePrice;
        private int _currentStock;
        private int _minimumStock;
        private byte[]? _barcodeImage;
        private bool _isSelected;
        private bool _isSelectedForPrinting;
        private string? _speed;
        private bool _isActive;
        private string? _imagePath;

        private int? _plantsHardscapeId;
        private string _plantsHardscapeName = string.Empty;
        private int? _localImportedId;
        private string _localImportedName = string.Empty;
        private int? _indoorOutdoorId;
        private string _indoorOutdoorName = string.Empty;
        private int? _plantFamilyId;
        private string _plantFamilyName = string.Empty;
        private int? _detailId;
        private string _detailName = string.Empty;

        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                _imagePath = value;
                OnPropertyChanged();
            }
        }

        public int ProductId
        {
            get => _productId;
            set
            {
                _productId = value;
                OnPropertyChanged();
            }
        }

        public string Barcode
        {
            get => _barcode;
            set
            {
                _barcode = value;
                OnPropertyChanged();
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public string? Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                OnPropertyChanged();
            }
        }

        public string? Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
            }
        }

        public int CategoryId
        {
            get => _categoryId;
            set
            {
                _categoryId = value;
                OnPropertyChanged();
            }
        }

        public string CategoryName
        {
            get => _categoryName;
            set
            {
                _categoryName = value;
                OnPropertyChanged();
            }
        }

        public int? SupplierId
        {
            get => _supplierId;
            set
            {
                _supplierId = value;
                OnPropertyChanged();
            }
        }

        public string SupplierName
        {
            get => _supplierName;
            set
            {
                _supplierName = value;
                OnPropertyChanged();
            }
        }

        public decimal PurchasePrice
        {
            get => _purchasePrice;
            set
            {
                _purchasePrice = value;
                OnPropertyChanged();
            }
        }

        public decimal SalePrice
        {
            get => _salePrice;
            set
            {
                _salePrice = value;
                OnPropertyChanged();
            }
        }

        public int CurrentStock
        {
            get => _currentStock;
            set
            {
                _currentStock = value;
                OnPropertyChanged();
            }
        }

        public int MinimumStock
        {
            get => _minimumStock;
            set
            {
                _minimumStock = value;
                OnPropertyChanged();
            }
        }

        public byte[]? BarcodeImage
        {
            get => _barcodeImage;
            set
            {
                _barcodeImage = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelectedForPrinting
        {
            get => _isSelectedForPrinting;
            set
            {
                _isSelectedForPrinting = value;
                OnPropertyChanged();
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }

        public int? PlantsHardscapeId
        {
            get => _plantsHardscapeId;
            set
            {
                _plantsHardscapeId = value;
                OnPropertyChanged();
            }
        }

        public string PlantsHardscapeName
        {
            get => _plantsHardscapeName;
            set
            {
                _plantsHardscapeName = value;
                OnPropertyChanged();
            }
        }

        public int? LocalImportedId
        {
            get => _localImportedId;
            set
            {
                _localImportedId = value;
                OnPropertyChanged();
            }
        }

        public string LocalImportedName
        {
            get => _localImportedName;
            set
            {
                _localImportedName = value;
                OnPropertyChanged();
            }
        }

        public int? IndoorOutdoorId
        {
            get => _indoorOutdoorId;
            set
            {
                _indoorOutdoorId = value;
                OnPropertyChanged();
            }
        }

        public string IndoorOutdoorName
        {
            get => _indoorOutdoorName;
            set
            {
                _indoorOutdoorName = value;
                OnPropertyChanged();
            }
        }

        public int? PlantFamilyId
        {
            get => _plantFamilyId;
            set
            {
                _plantFamilyId = value;
                OnPropertyChanged();
            }
        }

        public string PlantFamilyName
        {
            get => _plantFamilyName;
            set
            {
                _plantFamilyName = value;
                OnPropertyChanged();
            }
        }

        public int? DetailId
        {
            get => _detailId;
            set
            {
                _detailId = value;
                OnPropertyChanged();
            }
        }

        public string DetailName
        {
            get => _detailName;
            set
            {
                _detailName = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return $"{Name} ({Barcode})";
        }
    }
}