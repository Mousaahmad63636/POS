using System;
using System.Windows;
using System.Windows.Controls;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.WPF.Views
{
    public partial class CategoryDetailsPopup : UserControl
    {
        public event RoutedEventHandler CloseRequested;
        public event RoutedEventHandler SaveCompleted;

        public CategoryDTO Category
        {
            get { return (CategoryDTO)DataContext; }
            set { DataContext = value; }
        }

        public string CategoryType { get; set; }

        public CategoryDetailsPopup()
        {
            InitializeComponent();
        }

        public void SetMode(string categoryType, bool isNew)
        {
            CategoryType = categoryType;

            HeaderText.Text = isNew ?
                $"Add New {categoryType} Category" :
                $"Edit {categoryType} Category";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCompleted?.Invoke(this, new RoutedEventArgs());
        }
    }
}