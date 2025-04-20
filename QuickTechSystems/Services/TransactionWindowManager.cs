using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using QuickTechSystems.WPF.Views;

namespace QuickTechSystems.WPF.Services
{
    public class TransactionWindowManager
    {
        private static TransactionWindowManager _instance;
        public static TransactionWindowManager Instance => _instance ??= new TransactionWindowManager();

        private readonly Dictionary<string, TransactionWindow> _openWindows;
        public ReadOnlyCollection<string> OpenTableIds => new ReadOnlyCollection<string>(_openWindows.Keys.ToList());

        private TransactionWindowManager()
        {
            _openWindows = new Dictionary<string, TransactionWindow>();
        }

        public TransactionWindow CreateNewTransactionWindow(string tableIdentifier = null)
        {
            // If no table ID is provided, generate a unique one
            if (string.IsNullOrEmpty(tableIdentifier))
            {
                tableIdentifier = GenerateUniqueTableId();
            }
            else if (_openWindows.ContainsKey(tableIdentifier))
            {
                // If window with this ID already exists, bring it to front
                _openWindows[tableIdentifier].Activate();
                return _openWindows[tableIdentifier];
            }

            // Create a new transaction window
            var window = new TransactionWindow(tableIdentifier);

            // Add to our tracking dictionary
            _openWindows[tableIdentifier] = window;

            // Show the window
            window.Show();

            return window;
        }

        public bool IsTableOpen(string tableIdentifier)
        {
            return _openWindows.ContainsKey(tableIdentifier);
        }

        public void ActivateWindow(string tableIdentifier)
        {
            if (_openWindows.TryGetValue(tableIdentifier, out var window))
            {
                window.Activate();
                window.WindowState = WindowState.Normal;
            }
        }

        public void RemoveWindow(TransactionWindow window)
        {
            var tableId = window.TableIdentifier;
            if (_openWindows.ContainsKey(tableId))
            {
                _openWindows.Remove(tableId);
            }
        }

        private string GenerateUniqueTableId()
        {
            // Start with basic numbering
            int counter = 1;
            string tableId;

            // Keep incrementing until we find an unused ID
            do
            {
                tableId = counter.ToString();
                counter++;
            } while (_openWindows.ContainsKey(tableId));

            return tableId;
        }

        public void CloseAllWindows()
        {
            foreach (var window in _openWindows.Values.ToList())
            {
                window.Close();
            }
        }
    }
}