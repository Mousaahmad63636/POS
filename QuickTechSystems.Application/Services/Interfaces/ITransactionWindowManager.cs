// Path: QuickTechSystems.WPF/Services/ITransactionWindowManager.cs
using System.Windows;

namespace QuickTechSystems.WPF.Services
{
    public interface ITransactionWindowManager
    {
        void OpenNewTransactionWindow();
        void CloseAllTransactionWindows();
        int ActiveWindowCount { get; }
    }
}