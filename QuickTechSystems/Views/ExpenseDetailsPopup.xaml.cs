﻿// File: QuickTechSystems.WPF.Views.ExpenseDetailsPopup.xaml.cs
using System.Windows;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.Views
{
    public partial class ExpenseDetailsPopup : UserControl
    {
        public event RoutedEventHandler CloseRequested;
        public event RoutedEventHandler SaveCompleted;

        public ExpenseDetailsPopup()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // This will be called after the Save command is executed
            SaveCompleted?.Invoke(this, new RoutedEventArgs());
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            // This will be called after the Delete command is executed
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }
    }
}