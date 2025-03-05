using AutoMapper;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Domain.Entities;

namespace QuickTechSystems.Application.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Product, ProductDTO>()
                .ForMember(dest => dest.CategoryName,
                    opt => opt.MapFrom(src => src.Category != null ? src.Category.Name : string.Empty))
                .ForMember(dest => dest.SupplierName,
                    opt => opt.MapFrom(src => src.Supplier != null ? src.Supplier.Name : string.Empty));
            CreateMap<ProductDTO, Product>();

            CreateMap<Category, CategoryDTO>()
                .ForMember(dest => dest.ProductCount,
                    opt => opt.MapFrom(src => src.Products.Count));
            CreateMap<CategoryDTO, Category>();

            CreateMap<Customer, CustomerDTO>()
                .ForMember(dest => dest.TransactionCount,
                    opt => opt.MapFrom(src => src.Transactions.Count));
            CreateMap<CustomerDTO, Customer>();

            CreateMap<Transaction, TransactionDTO>()
                .ForMember(dest => dest.CustomerName,
                    opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : string.Empty))
                .ForMember(dest => dest.Details,
                    opt => opt.MapFrom(src => src.TransactionDetails));
            CreateMap<TransactionDTO, Transaction>()
                .ForMember(dest => dest.TransactionDetails,
                    opt => opt.MapFrom(src => src.Details));

            CreateMap<TransactionDetail, TransactionDetailDTO>()
    .ForMember(dest => dest.ProductName,
        opt => opt.MapFrom(src => src.Product != null ? src.Product.Name : string.Empty))
    .ForMember(dest => dest.ProductBarcode,
        opt => opt.MapFrom(src => src.Product != null ? src.Product.Barcode : string.Empty))
    .ForMember(dest => dest.PurchasePrice,
        opt => opt.MapFrom(src => src.Product != null ? src.Product.PurchasePrice : 0))
    .ForMember(dest => dest.CategoryId,
        opt => opt.MapFrom(src => src.Product != null ? src.Product.CategoryId : 0));
            CreateMap<TransactionDetailDTO, TransactionDetail>()
                .ForMember(dest => dest.Product, opt => opt.Ignore());

            CreateMap<BusinessSetting, BusinessSettingDTO>();
            CreateMap<BusinessSettingDTO, BusinessSetting>();

            CreateMap<SystemPreference, SystemPreferenceDTO>();
            CreateMap<SystemPreferenceDTO, SystemPreference>();
            CreateMap<Expense, ExpenseDTO>();
            CreateMap<ExpenseDTO, Expense>();
            CreateMap<Supplier, SupplierDTO>()
                .ForMember(dest => dest.ProductCount,
                    opt => opt.MapFrom(src => src.Products.Count))
                .ForMember(dest => dest.TransactionCount,
                    opt => opt.MapFrom(src => src.Transactions.Count));
            CreateMap<SupplierDTO, Supplier>();

            CreateMap<SupplierTransaction, SupplierTransactionDTO>()
                .ForMember(dest => dest.SupplierName,
                    opt => opt.MapFrom(src => src.Supplier != null ? src.Supplier.Name : string.Empty));
            CreateMap<SupplierTransactionDTO, SupplierTransaction>();

            CreateMap<InventoryHistory, InventoryHistoryDTO>()
                .ForMember(dest => dest.ProductName,
                    opt => opt.MapFrom(src => src.Product != null ? src.Product.Name : string.Empty));
            CreateMap<InventoryHistoryDTO, InventoryHistory>();

            CreateMap<Drawer, DrawerDTO>()
              .ForMember(dest => dest.ExpectedBalance,
                  opt => opt.MapFrom(src => src.OpeningBalance + src.CashIn - src.CashOut))
              .ForMember(dest => dest.Difference,
                  opt => opt.MapFrom(src => src.CurrentBalance - (src.OpeningBalance + src.CashIn - src.CashOut)));

            CreateMap<DrawerHistoryEntry, DrawerTransactionDTO>()
    .ForMember(dest => dest.ActionType, opt => opt.MapFrom(src => src.ActionType))
    .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
    .ForMember(dest => dest.ResultingBalance, opt => opt.MapFrom(src => src.ResultingBalance))
    .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount))
    .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(src => src.Timestamp));

            CreateMap<DrawerDTO, Drawer>();

            CreateMap<Employee, EmployeeDTO>();
            CreateMap<EmployeeDTO, Employee>();

            // If you have drawer transactions
            CreateMap<DrawerTransaction, DrawerTransactionDTO>();
            CreateMap<DrawerTransactionDTO, DrawerTransaction>();
            CreateMap<CustomerPayment, CustomerPaymentDTO>()
               .ForMember(dest => dest.CustomerName,
                   opt => opt.MapFrom(src => src.Customer.Name));
            CreateMap<CustomerPaymentDTO, CustomerPayment>();
            CreateMap<Customer, CustomerDTO>()
        .ForMember(dest => dest.TransactionCount,
            opt => opt.MapFrom(src => src.Transactions.Count))
        .ForMember(dest => dest.Balance,
            opt => opt.MapFrom(src => src.Balance));
            CreateMap<CustomerDTO, Customer>();



            // New mappings for subscription settings
            CreateMap<MonthlySubscriptionSettings, MonthlySubscriptionSettingsDTO>();
            CreateMap<MonthlySubscriptionSettingsDTO, MonthlySubscriptionSettings>();

            // New mapping for subscription types
            CreateMap<SubscriptionType, SubscriptionTypeDTO>()
                .ForMember(dest => dest.CustomerCount,
                    opt => opt.MapFrom(src => src.CustomerSubscriptions.Count));
            CreateMap<SubscriptionTypeDTO, SubscriptionType>();

            // Updated CustomerSubscription mapping
            CreateMap<CustomerSubscription, CustomerSubscriptionDTO>()
                .ForMember(dest => dest.PaymentCount,
                    opt => opt.MapFrom(src => src.Payments.Count))
                .ForMember(dest => dest.TotalPayments,
                    opt => opt.MapFrom(src => src.Payments.Sum(p => p.Amount)))
                .ForMember(dest => dest.SubscriptionTypeName,
                    opt => opt.MapFrom(src => src.SubscriptionType != null ? src.SubscriptionType.Name : string.Empty))
                .ForMember(dest => dest.AdditionalCharge,
                    opt => opt.MapFrom(src => src.SubscriptionType != null ? src.SubscriptionType.AdditionalCharge : 0));
            CreateMap<CustomerSubscriptionDTO, CustomerSubscription>();

            // Keep existing payment mapping
            CreateMap<SubscriptionPayment, SubscriptionPaymentDTO>()
                .ForMember(dest => dest.CustomerName,
                    opt => opt.MapFrom(src => src.CustomerSubscription.Name));
            CreateMap<SubscriptionPaymentDTO, SubscriptionPayment>();

            // Keep existing counter history mapping
            CreateMap<CounterHistory, CounterHistoryDTO>()
                .ForMember(dest => dest.CustomerName,
                    opt => opt.MapFrom(src => src.CustomerSubscription.Name));
            CreateMap<CounterHistoryDTO, CounterHistory>();

            CreateMap<CounterHistory, CounterHistoryDTO>()
    .ForMember(dest => dest.CustomerName,
        opt => opt.MapFrom(src => src.CustomerSubscription.Name))
    .ForMember(dest => dest.AdditionalFees,
        opt => opt.MapFrom(src => src.AdditionalFees));

            CreateMap<CounterHistoryDTO, CounterHistory>()
                .ForMember(dest => dest.AdditionalFees,
                    opt => opt.MapFrom(src => src.AdditionalFees));
        }
    }
}