using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SnapPickWin.Models;

namespace SnapPickWin.Services
{
    public class ProjectManager
    {
        private readonly string _workspaceDirectory;
        private readonly List<SnapPickProject> _projects = new();
        private readonly string _pendingCleanupPath;

        public IReadOnlyList<SnapPickProject> Projects => _projects;

        public ProjectManager()
        {
            // Windows Documents folder for SnapPick projects
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _workspaceDirectory = Path.Combine(documents, "SnapPick");
            Directory.CreateDirectory(_workspaceDirectory);

            _pendingCleanupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "SnapPickWin", 
                "pending_cleanup.json"
            );

            LoadProjects();
        }

        public void LoadProjects()
        {
            _projects.Clear();
            if (!Directory.Exists(_workspaceDirectory)) return;

            // Search for all folder workspaces that contain a .snappick file
            foreach (var dir in Directory.GetDirectories(_workspaceDirectory))
            {
                string projectName = Path.GetFileName(dir);
                string snappickFilePath = Path.Combine(dir, $"{projectName}.snappick");

                if (File.Exists(snappickFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(snappickFilePath);
                        var project = JsonSerializer.Deserialize<SnapPickProject>(json);
                        if (project != null)
                        {
                            _projects.Add(project);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ ProjectManager: Failed to load project at {snappickFilePath}: {ex.Message}");
                    }
                }
            }

            // Sort by last modified descending
            _projects.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
        }

        public SnapPickProject CreateProject(string title)
        {
            string cleanTitle = string.Concat(title.Split(Path.GetInvalidFileNameChars()));
            string projectFolder = Path.Combine(_workspaceDirectory, cleanTitle);
            
            // Handle duplicate folder names
            int counter = 1;
            while (Directory.Exists(projectFolder))
            {
                projectFolder = Path.Combine(_workspaceDirectory, $"{cleanTitle}_{counter++}");
            }

            Directory.CreateDirectory(projectFolder);
            string projectImagesFolder = Path.Combine(projectFolder, "thumbnails");
            Directory.CreateDirectory(projectImagesFolder);

            var project = new SnapPickProject
            {
                Title = title,
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };

            // Store project directory path in FolderBookmarks for Windows reference
            project.FolderBookmarks["project_folder"] = projectFolder;
            project.FolderBookmarks["thumbnails_folder"] = projectImagesFolder;

            SaveProject(project);
            _projects.Insert(0, project);
            return project;
        }

        public void SaveProject(SnapPickProject project)
        {
            if (!project.FolderBookmarks.TryGetValue("project_folder", out string? folderPath) || !Directory.Exists(folderPath))
            {
                Console.WriteLine("❌ ProjectManager: Cannot save project, directory path missing.");
                return;
            }

            project.LastModified = DateTime.UtcNow;
            string projectName = Path.GetFileName(folderPath);
            string filePath = Path.Combine(folderPath, $"{projectName}.snappick");

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(project, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ProjectManager: Error saving project file {filePath}: {ex.Message}");
            }
        }

        public void DeleteProject(SnapPickProject project)
        {
            _projects.Remove(project);

            if (project.FolderBookmarks.TryGetValue("project_folder", out string? folderPath) && Directory.Exists(folderPath))
            {
                try
                {
                    Directory.Delete(folderPath, true);
                    Console.WriteLine($"Project folder deleted: {folderPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ ProjectManager: Failed to delete project folder: {ex.Message}");
                }
            }
        }

        // Auto-detect subfolder categories and register photos
        public void ImportPhotosFromFolder(SnapPickProject project, string rootImportPath)
        {
            if (!Directory.Exists(rootImportPath)) return;

            project.Categories.Clear();

            string[] subdirs = Directory.GetDirectories(rootImportPath);
            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

            // Helper to get image files
            List<string> GetImagesInDir(string dir) => 
                Directory.GetFiles(dir)
                    .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .ToList();

            if (subdirs.Length > 0)
            {
                // Category Import Rule: Subfolders -> auto-categories
                int displayOrder = 1;
                foreach (var dir in subdirs)
                {
                    var imageFiles = GetImagesInDir(dir);
                    if (imageFiles.Count == 0) continue;

                    var category = new PhotoCategory
                    {
                        Name = Path.GetFileName(dir),
                        DisplayOrder = displayOrder++
                    };

                    foreach (var imgPath in imageFiles)
                    {
                        category.ImageReferences.Add(new ProjectImageRef
                        {
                            OriginalPath = imgPath,
                            CategoryID = category.Id
                        });
                    }

                    project.Categories.Add(category);
                }

                // Loose root images alongside subfolders -> "General" category
                var looseImages = GetImagesInDir(rootImportPath);
                if (looseImages.Count > 0)
                {
                    var generalCategory = new PhotoCategory
                    {
                        Name = "General",
                        DisplayOrder = displayOrder
                    };

                    foreach (var imgPath in looseImages)
                    {
                        generalCategory.ImageReferences.Add(new ProjectImageRef
                        {
                            OriginalPath = imgPath,
                            CategoryID = generalCategory.Id
                        });
                    }

                    project.Categories.Add(generalCategory);
                }
            }
            else
            {
                // Flat folder -> single category
                var imageFiles = GetImagesInDir(rootImportPath);
                if (imageFiles.Count > 0)
                {
                    var category = new PhotoCategory
                    {
                        Name = Path.GetFileName(rootImportPath),
                        DisplayOrder = 1
                    };

                    foreach (var imgPath in imageFiles)
                    {
                        category.ImageReferences.Add(new ProjectImageRef
                        {
                            OriginalPath = imgPath,
                            CategoryID = category.Id
                        });
                    }

                    project.Categories.Add(category);
                }
            }

            SaveProject(project);
        }

        // Export selected original files to destination category subfolders
        public async Task ExportSelectedPhotosAsync(SnapPickProject project, string destinationRootPath, bool copyMode)
        {
            if (project.Selection == null || string.IsNullOrEmpty(destinationRootPath)) return;

            await Task.Run(() =>
            {
                Directory.CreateDirectory(destinationRootPath);

                foreach (var category in project.Categories)
                {
                    if (!project.Selection.Categories.TryGetValue(category.Id, out var clientCatSelection)) 
                        continue;

                    var selectedIDs = clientCatSelection.SelectedIDs;
                    if (selectedIDs.Count == 0) continue;

                    string categoryExportFolder = Path.Combine(destinationRootPath, category.Name);
                    Directory.CreateDirectory(categoryExportFolder);

                    foreach (var photo in category.ImageReferences)
                    {
                        if (!selectedIDs.Contains(photo.Id)) continue;
                        if (!File.Exists(photo.OriginalPath)) continue;

                        string fileName = Path.GetFileName(photo.OriginalPath);
                        string destFilePath = Path.Combine(categoryExportFolder, fileName);

                        if (copyMode)
                        {
                            File.Copy(photo.OriginalPath, destFilePath, true);
                        }
                        else
                        {
                            // Move mode: update original path after moving
                            if (File.Exists(destFilePath))
                            {
                                File.Delete(destFilePath);
                            }
                            File.Move(photo.OriginalPath, destFilePath);
                            photo.OriginalPath = destFilePath;
                        }
                    }
                }

                SaveProject(project);
            });
        }
    }
}
