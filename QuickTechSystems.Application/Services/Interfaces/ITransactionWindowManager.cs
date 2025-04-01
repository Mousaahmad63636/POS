// Path: QuickTechSystems.Application/Services/Interfaces/ITransactionWindowManager.cs
namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface ITransactionWindowManager
    {
        void OpenNewTransactionWindow();
        void CloseAllTransactionWindows();
        int ActiveWindowCount { get; }
    }
}