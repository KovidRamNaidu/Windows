using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SnapPickWin.Models;
using SnapPickWin.ViewModels;
using SnapPickWin.Services;

namespace SnapPickWin
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            
            var firebaseService = new FirebaseService();
            var projectManager = new ProjectManager();
            _viewModel = new MainViewModel(firebaseService, projectManager);
            
            DataContext = _viewModel;
        }

        // MARK: - Login Handlers

        private async void OnLoginClick(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var stack = (StackPanel)btn.Parent;
            var grid = (Grid)stack.Parent;
            var borderPanel = (StackPanel)grid.Parent;
            
            var emailBox = borderPanel.Children[1] as StackPanel;
            var emailText = (emailBox?.Children[1] as TextBox)?.Text;

            var passwordBox = borderPanel.Children[2] as StackPanel;
            var passwordText = (passwordBox?.Children[1] as PasswordBox)?.Password;

            if (string.IsNullOrEmpty(emailText) || string.IsNullOrEmpty(passwordText))
            {
                MessageBox.Show("Please enter email and password.", "Login", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                await _viewModel.LoginCommand.ExecuteAsync(Tuple.Create(emailText, passwordText));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Login Failed", MessageBoxButton.OK, MessageBoxIcon.Error);
            }
        }

        private async void OnRegisterClick(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var stack = (StackPanel)btn.Parent;
            var grid = (Grid)stack.Parent;
            var borderPanel = (StackPanel)grid.Parent;
            
            var emailBox = borderPanel.Children[1] as StackPanel;
            var emailText = (emailBox?.Children[1] as TextBox)?.Text;

            var passwordBox = borderPanel.Children[2] as StackPanel;
            var passwordText = (passwordBox?.Children[1] as PasswordBox)?.Password;

            if (string.IsNullOrEmpty(emailText) || string.IsNullOrEmpty(passwordText))
            {
                MessageBox.Show("Please enter email and password.", "Registration", MessageBoxButton.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                await _viewModel.RegisterCommand.ExecuteAsync(Tuple.Create(emailText, passwordText));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Registration Failed", MessageBoxButton.OK, MessageBoxIcon.Error);
            }
        }

        // MARK: - Project Handlers

        private void OnCreateProjectClick(object sender, RoutedEventArgs e)
        {
            string title = ShowInputBox("Enter Project Name:", "New Project Workspace");
            if (!string.IsNullOrWhiteSpace(title))
            {
                _viewModel.CreateProjectCommand.Execute(title);
            }
        }

        private void OnDeleteProjectClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var project = (SnapPickProject)button.DataContext;
            
            var result = MessageBox.Show(
                $"Are you sure you want to delete project space \"{project.Title}\"?\nThis will permanently delete all local cache folders.",
                "Delete Project Space",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.DeleteProjectCommand.Execute(project);
            }
        }

        private void OnOpenProjectClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var project = (SnapPickProject)button.DataContext;
            _viewModel.SelectedProject = project;
        }

        // MARK: - Gallery Handlers

        private void OnImportFolderClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Photos Folder to Import (Subfolders become Categories)",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                var galleryVm = _viewModel.CurrentView as GalleryViewModel;
                galleryVm?.ImportPhotosCommand.Execute(dialog.FolderName);
            }
        }

        private async void OnExportSelectedClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Directory to Export Client Selections",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                var galleryVm = _viewModel.CurrentView as GalleryViewModel;
                if (galleryVm != null)
                {
                    await galleryVm.ExportSelectedPhotosCommand.ExecuteAsync(dialog.FolderName);
                    MessageBox.Show("Selected photos exported successfully!", "Export Selections", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private async void OnResetSelectionsDialogClick(object sender, RoutedEventArgs e)
        {
            var galleryVm = _viewModel.CurrentView as GalleryViewModel;
            if (galleryVm == null) return;

            var result = MessageBox.Show(
                "Do you want to clear existing client selections?\n\n- Click YES to delete selections from Firestore and reopen a clean link.\n- Click NO to keep selections and allow the client to edit choices.\n- Click CANCEL to abort.",
                "Reset selections & Reopen Link",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                await galleryVm.ResetSelectionsCommand.ExecuteAsync(true);
            }
            else if (result == MessageBoxResult.No)
            {
                await galleryVm.ResetSelectionsCommand.ExecuteAsync(false);
            }
        }

        // Simple helper to simulate InputBox in WPF
        private static string ShowInputBox(string prompt, string title)
        {
            var inputWindow = new Window
            {
                Title = title,
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 34))
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = prompt,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(label, 0);

            var textBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Margin = new Thickness(0, 0, 5, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => { inputWindow.DialogResult = true; };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                IsCancel = true
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            inputWindow.Content = grid;

            if (inputWindow.ShowDialog() == true)
            {
                return textBox.Text;
            }
            return string.Empty;
        }
    }
}
