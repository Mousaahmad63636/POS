﻿using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickTechSystems.ViewModels;
using QuickTechSystems.WPF;

namespace QuickTechSystems.Views
{
    public partial class LoginView : Window
    {
        public LoginView()
        {
            InitializeComponent();
            DataContext = ((App)System.Windows.Application.Current).ServiceProvider.GetService<LoginViewModel>();

            // Set window size based on screen resolution
            AdjustWindowSize();
        }

        private void AdjustWindowSize()
        {
            // Set window size based on screen dimensions
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            // Adjust for different screen sizes
            if (screenWidth >= 1920) // Large screens
            {
                this.Width = 500;
                this.Height = 700;
            }
            else if (screenWidth >= 1366) // Medium screens
            {
                this.Width = 450;
                this.Height = 650;
            }
            else // Small screens
            {
                this.Width = 400;
                this.Height = 600;
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}