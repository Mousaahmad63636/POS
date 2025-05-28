// Path: QuickTechSystems.WPF/Services/ITransactionWindowManager.cs
namespace QuickTechSystems.WPF.Services
{
    public interface ITransactionWindowManager
    {
        void OpenNewTransactionWindow();
        void CloseAllTransactionWindows();
        int ActiveWindowCount { get; }
    }
}