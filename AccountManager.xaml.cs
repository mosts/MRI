using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace MRI
{
    public partial class AccountManager : Window
    {
        private static readonly string AccountsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.txt");

        public AccountManager()
        {
            InitializeComponent();
            LoadAccountsFromFile();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                File.WriteAllText(AccountsFilePath, AccountsTextBox.Text);
                StatusLabel.Content = "Accounts saved successfully!";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error saving: {ex.Message}";
            }
        }

        private void LoadBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadAccountsFromFile();
        }

        private void LoadAccountsFromFile()
        {
            try
            {
                if (File.Exists(AccountsFilePath))
                {
                    AccountsTextBox.Text = File.ReadAllText(AccountsFilePath);
                    StatusLabel.Content = "Accounts loaded successfully!";
                }
                else
                {
                    StatusLabel.Content = "No saved accounts found.";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Content = $"Error loading: {ex.Message}";
            }
        }

        public static string[] GetAccounts()
        {
            if (File.Exists(AccountsFilePath))
            {
                return File.ReadAllLines(AccountsFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains(':'))
                    .ToArray();
            }
            return Array.Empty<string>();
        }
    }
}
