using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapPickWin.Models;
using SnapPickWin.Services;

namespace SnapPickWin.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly FirebaseService _firebaseService;
        private readonly ProjectManager _projectManager;

        private object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private bool _isAuthenticated;
        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            set => SetProperty(ref _isAuthenticated, value);
        }

        private string _userEmail = string.Empty;
        public string UserEmail
        {
            get => _userEmail;
            set => SetProperty(ref _userEmail, value);
        }

        public ObservableCollection<SnapPickProject> Projects { get; } = new();

        private SnapPickProject? _selectedProject;
        public SnapPickProject? SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (SetProperty(ref _selectedProject, value) && value != null)
                {
                    // Navigate to project workspace
                    OpenProjectWorkspace(value);
                }
            }
        }

        public IAsyncRelayCommand LoginCommand { get; }
        public IAsyncRelayCommand RegisterCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand CreateProjectCommand { get; }
        public ICommand DeleteProjectCommand { get; }
        public ICommand NavigateToProjectsCommand { get; }

        public MainViewModel(FirebaseService firebaseService, ProjectManager projectManager)
        {
            _firebaseService = firebaseService;
            _projectManager = projectManager;
            _currentView = new object(); // Will be set to login or projects view

            IsAuthenticated = _firebaseService.IsAuthenticated;
            UserEmail = _firebaseService.Email ?? string.Empty;

            LoginCommand = new AsyncRelayCommand<Tuple<string, string>>(LoginAsync);
            RegisterCommand = new AsyncRelayCommand<Tuple<string, string>>(RegisterAsync);
            LogoutCommand = new RelayCommand(Logout);
            CreateProjectCommand = new RelayCommand<string>(CreateProject);
            DeleteProjectCommand = new RelayCommand<SnapPickProject>(DeleteProject);
            NavigateToProjectsCommand = new RelayCommand(LoadProjectsScreen);

            if (IsAuthenticated)
            {
                LoadProjectsScreen();
            }
            else
            {
                CurrentView = "LoginScreen"; // Simple string view token for UI routing
            }
        }

        private async Task LoginAsync(Tuple<string, string>? credentials)
        {
            if (credentials == null) return;
            string email = credentials.Item1;
            string password = credentials.Item2;

            try
            {
                await _firebaseService.SignInAsync(email, password);
                IsAuthenticated = _firebaseService.IsAuthenticated;
                UserEmail = _firebaseService.Email ?? string.Empty;
                LoadProjectsScreen();
            }
            catch (Exception ex)
            {
                throw new Exception($"Login Failed: {ex.Message}");
            }
        }

        private async Task RegisterAsync(Tuple<string, string>? credentials)
        {
            if (credentials == null) return;
            string email = credentials.Item1;
            string password = credentials.Item2;

            try
            {
                await _firebaseService.SignUpAsync(email, password);
                IsAuthenticated = _firebaseService.IsAuthenticated;
                UserEmail = _firebaseService.Email ?? string.Empty;
                LoadProjectsScreen();
            }
            catch (Exception ex)
            {
                throw new Exception($"Signup Failed: {ex.Message}");
            }
        }

        private void Logout()
        {
            _firebaseService.Logout();
            IsAuthenticated = false;
            UserEmail = string.Empty;
            CurrentView = "LoginScreen";
        }

        private void LoadProjectsScreen()
        {
            _projectManager.LoadProjects();
            Projects.Clear();
            foreach (var p in _projectManager.Projects)
            {
                Projects.Add(p);
            }
            CurrentView = "ProjectsScreen";
        }

        private void CreateProject(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            var project = _projectManager.CreateProject(title);
            Projects.Insert(0, project);
            SelectedProject = project;
        }

        private void DeleteProject(SnapPickProject? project)
        {
            if (project == null) return;
            _projectManager.DeleteProject(project);
            Projects.Remove(project);
            if (SelectedProject == project)
            {
                SelectedProject = null;
                LoadProjectsScreen();
            }
        }

        private void OpenProjectWorkspace(SnapPickProject project)
        {
            CurrentView = new GalleryViewModel(project, _firebaseService, _projectManager, this);
        }
    }
}
