using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapPickWin.Models;
using SnapPickWin.Services;

namespace SnapPickWin.ViewModels
{
    public class GalleryViewModel : ObservableObject
    {
        private readonly FirebaseService _firebaseService;
        private readonly ProjectManager _projectManager;
        private readonly MainViewModel _mainViewModel;
        private CancellationTokenSource? _uploadCts;

        private SnapPickProject _project;
        public SnapPickProject Project
        {
            get => _project;
            set => SetProperty(ref _project, value);
        }

        public string Title => Project.Title;

        public string Status => Project.Status.ToUpper();

        private string _shareLink = string.Empty;
        public string ShareLink
        {
            get => _shareLink;
            set => SetProperty(ref _shareLink, value);
        }

        private bool _isUploading;
        public bool IsUploading
        {
            get => _isUploading;
            set
            {
                if (SetProperty(ref _isUploading, value))
                {
                    OnPropertyChanged(nameof(CanUpload));
                    OnPropertyChanged(nameof(CanImport));
                }
            }
        }

        public bool CanUpload => !IsUploading && Project.Status == "draft" && Project.Categories.Count > 0;
        public bool CanImport => !IsUploading && Project.Status == "draft";

        private double _uploadProgress;
        public double UploadProgress
        {
            get => _uploadProgress;
            set => SetProperty(ref _uploadProgress, value);
        }

        private string _uploadStatusText = string.Empty;
        public string UploadStatusText
        {
            get => _uploadStatusText;
            set => SetProperty(ref _uploadStatusText, value);
        }

        private bool _syncOverlayVisible;
        public bool SyncOverlayVisible
        {
            get => _syncOverlayVisible;
            set => SetProperty(ref _syncOverlayVisible, value);
        }

        private PhotoCategory? _selectedCategory;
        public PhotoCategory? SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        private bool _exportCopyMode = true;
        public bool ExportCopyMode
        {
            get => _exportCopyMode;
            set => SetProperty(ref _exportCopyMode, value);
        }

        public ObservableCollection<PhotoCategory> Categories { get; } = new();

        public ICommand ImportPhotosCommand { get; }
        public ICommand StartUploadPipelineCommand { get; }
        public ICommand CancelUploadCommand { get; }
        public ICommand FetchSelectionsCommand { get; }
        public ICommand ResetSelectionsCommand { get; }
        public ICommand ExportSelectedPhotosCommand { get; }
        public ICommand NavigateBackCommand { get; }

        public GalleryViewModel(
            SnapPickProject project, 
            FirebaseService firebaseService, 
            ProjectManager projectManager,
            MainViewModel mainViewModel)
        {
            _project = project;
            _firebaseService = firebaseService;
            _projectManager = projectManager;
            _mainViewModel = mainViewModel;

            RefreshCategories();

            if (!string.IsNullOrEmpty(Project.ShareToken))
            {
                ShareLink = $"https://snappick.web.app/view/{Project.ShareToken}";
            }

            ImportPhotosCommand = new RelayCommand<string>(ImportPhotos);
            StartUploadPipelineCommand = new AsyncRelayCommand(StartUploadPipelineAsync);
            CancelUploadCommand = new RelayCommand(CancelUpload);
            FetchSelectionsCommand = new AsyncRelayCommand(FetchSelectionsAsync);
            ResetSelectionsCommand = new AsyncRelayCommand<bool>(ResetSelectionsAsync);
            ExportSelectedPhotosCommand = new AsyncRelayCommand<string>(ExportSelectedPhotosAsync);
            NavigateBackCommand = new RelayCommand(NavigateBack);

            // Periodically check for selections if active
            if (Project.Status == "active")
            {
                _ = AutoPollSelectionsAsync();
            }
        }

        private void RefreshCategories()
        {
            Categories.Clear();
            foreach (var cat in Project.Categories)
            {
                Categories.Add(cat);
            }
            SelectedCategory = Categories.FirstOrDefault();
            OnPropertyChanged(nameof(CanUpload));
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(Status));
        }

        private void ImportPhotos(string? rootPath)
        {
            if (string.IsNullOrEmpty(rootPath)) return;
            _projectManager.ImportPhotosFromFolder(Project, rootPath);
            RefreshCategories();
        }

        private async Task StartUploadPipelineAsync()
        {
            if (Project.Categories.Count == 0) return;

            IsUploading = true;
            UploadProgress = 0;
            _uploadCts = new CancellationTokenSource();
            var token = _uploadCts.Token;

            // 1. Generate unique share token
            string shareToken = GenerateRandomToken(12);
            Project.ShareToken = shareToken;
            ShareLink = $"https://snappick.web.app/view/{shareToken}";
            Project.Status = "uploading";
            _projectManager.SaveProject(Project);
            OnPropertyChanged(nameof(Status));

            try
            {
                // Write active structure to Firestore projects collection
                await _firebaseService.WriteProjectAsync(Project, "uploading", shareToken);

                // Write categories
                foreach (var category in Project.Categories)
                {
                    token.ThrowIfCancellationRequested();
                    await _firebaseService.WriteCategoryAsync(Project.Id, category);
                }

                // Capped concurrent photo uploads: 4 parallel streams (replicates Swift TaskGroup)
                var allPhotos = Project.AllCategoryRefs;
                int totalPhotos = allPhotos.Count;
                int completedPhotos = 0;
                var semaphore = new SemaphoreSlim(4);

                var uploadTasks = allPhotos.Select(async (photo, index) =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        
                        // Generate WebP thumbnail locally (max dimension 1200px, quality loop down to 55% for <300KB)
                        string? thumbPath = ImageProcessor.GenerateThumbnail(photo.OriginalPath);
                        if (thumbPath == null)
                        {
                            throw new Exception($"Failed to generate thumbnail for {photo.OriginalPath}");
                        }

                        token.ThrowIfCancellationRequested();

                        // Write photo documents to Firestore and upload to Storage
                        await _firebaseService.WritePhotoAsync(Project.Id, photo.CategoryID ?? "", photo, thumbPath, index + 1);

                        // Save generated local thumbnail path
                        photo.ThumbnailPath = thumbPath;
                        
                        // Increment completed count thread-safely
                        int currentCompleted = Interlocked.Increment(ref completedPhotos);
                        
                        // Update progress bar
                        double progressPercentage = (double)currentCompleted / totalPhotos * 100;
                        UploadProgress = progressPercentage;
                        UploadStatusText = $"Uploading photo {currentCompleted} of {totalPhotos} ({Math.Round(progressPercentage)}%)";
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(uploadTasks);
                token.ThrowIfCancellationRequested();

                // Upload complete! Mark project active
                Project.Status = "active";
                Project.UploadedAt = DateTime.UtcNow;
                _projectManager.SaveProject(Project);
                
                await _firebaseService.WriteProjectAsync(Project, "active", shareToken);
                
                IsUploading = false;
                UploadStatusText = "Upload complete! Share link is now active.";
                OnPropertyChanged(nameof(Status));

                // Start polling background check
                _ = AutoPollSelectionsAsync();
            }
            catch (OperationCanceledException)
            {
                UploadStatusText = "Upload cancelled.";
                ResetToDraft();
            }
            catch (Exception ex)
            {
                UploadStatusText = $"Upload failed: {ex.Message}";
                ResetToDraft();
            }
            finally
            {
                IsUploading = false;
            }
        }

        private void CancelUpload()
        {
            _uploadCts?.Cancel();
        }

        private void ResetToDraft()
        {
            Project.Status = "draft";
            Project.ShareToken = null;
            ShareLink = string.Empty;
            _projectManager.SaveProject(Project);
            OnPropertyChanged(nameof(Status));
        }

        private async Task FetchSelectionsAsync()
        {
            if (string.IsNullOrEmpty(Project.ShareToken)) return;

            SyncOverlayVisible = true;
            try
            {
                var selection = await _firebaseService.FetchSelectionsAsync(Project.Id);
                if (selection != null)
                {
                    Project.Selection = selection;
                    if (selection.SubmittedAt != null)
                    {
                        Project.Status = "submitted";
                    }
                    _projectManager.SaveProject(Project);
                    OnPropertyChanged(nameof(Status));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GalleryViewModel: Failed to fetch selections: {ex.Message}");
            }
            finally
            {
                SyncOverlayVisible = false;
            }
        }

        private async Task ResetSelectionsAsync(bool clearSelections)
        {
            SyncOverlayVisible = true;
            try
            {
                if (clearSelections)
                {
                    // 1. Delete selections document from Firestore
                    await _firebaseService.DeleteSelectionsAsync(Project.Id);
                    Project.Selection = null;
                }

                // 2. Generate a new share token
                string newShareToken = GenerateRandomToken(12);
                Project.ShareToken = newShareToken;
                ShareLink = $"https://snappick.web.app/view/{newShareToken}";
                Project.Status = "active";
                _projectManager.SaveProject(Project);

                // 3. Update Firestore Project document with new token and status
                await _firebaseService.WriteProjectAsync(Project, "active", newShareToken);
                OnPropertyChanged(nameof(Status));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GalleryViewModel: Failed to reset selections: {ex.Message}");
            }
            finally
            {
                SyncOverlayVisible = false;
            }
        }

        private async Task ExportSelectedPhotosAsync(string? destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath) || Project.Selection == null) return;

            SyncOverlayVisible = true;
            try
            {
                await _projectManager.ExportSelectedPhotosAsync(Project, destinationPath, ExportCopyMode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GalleryViewModel: Export failed: {ex.Message}");
            }
            finally
            {
                SyncOverlayVisible = false;
            }
        }

        private async Task AutoPollSelectionsAsync()
        {
            while (Project.Status == "active")
            {
                await Task.Delay(15000); // Poll every 15 seconds
                if (Project.Status != "active") break;

                try
                {
                    var selection = await _firebaseService.FetchSelectionsAsync(Project.Id);
                    if (selection != null)
                    {
                        Project.Selection = selection;
                        if (selection.SubmittedAt != null)
                        {
                            Project.Status = "submitted";
                            _projectManager.SaveProject(Project);
                            OnPropertyChanged(nameof(Status));
                            break; // Stop polling once submitted
                        }
                    }
                }
                catch
                {
                    // Suppress network logs in background polling
                }
            }
        }

        private void NavigateBack()
        {
            _mainViewModel.NavigateToProjectsCommand.Execute(null);
        }

        private static string GenerateRandomToken(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
