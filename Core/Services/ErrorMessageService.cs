using System.Windows;

namespace WorkAudit.Core.Services
{
    public interface IErrorMessageService
    {
        void ShowError(string title, string message);
        void ShowWarning(string title, string message);
        void ShowInfo(string title, string message);
        void ShowSuccess(string title, string message);
        MessageBoxResult ShowConfirmation(string title, string message);
        MessageBoxResult ShowYesNoCancel(string title, string message);
    }

    public class ErrorMessageService : IErrorMessageService
    {
        public void ShowError(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        public void ShowWarning(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        public void ShowInfo(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public void ShowSuccess(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public MessageBoxResult ShowConfirmation(string title, string message)
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            });
        }

        public MessageBoxResult ShowYesNoCancel(string title, string message)
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            });
        }
    }
}
