using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SnapPickWin.Models
{
    public class SnapPickProject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "draft"; // "draft" | "uploading" | "active" | "submitted" | "expired" | "archived"

        [JsonPropertyName("shareToken")]
        public string? ShareToken { get; set; }

        [JsonPropertyName("uploadedAt")]
        public DateTime? UploadedAt { get; set; }

        [JsonPropertyName("selection")]
        public SelectionData? Selection { get; set; }

        [JsonPropertyName("folderBookmarks")]
        public Dictionary<string, string> FolderBookmarks { get; set; } = new(); // In Windows, we store raw directory paths instead of macOS Security Bookmarks

        [JsonPropertyName("categories")]
        public List<PhotoCategory> Categories { get; set; } = new();

        [JsonIgnore]
        public List<ProjectImageRef> AllCategoryRefs
        {
            get
            {
                var list = new List<ProjectImageRef>();
                foreach (var category in Categories)
                {
                    list.AddRange(category.ImageReferences);
                }
                return list;
            }
        }

        [JsonIgnore]
        public bool HasCategoryStructure => Categories.Count > 0;
    }

    public class PhotoCategory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayOrder")]
        public int DisplayOrder { get; set; } = 0;

        [JsonPropertyName("imageReferences")]
        public List<ProjectImageRef> ImageReferences { get; set; } = new();

        [JsonIgnore]
        public int PhotoCount => ImageReferences.Count;
    }

    public class ProjectImageRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("originalPath")]
        public string OriginalPath { get; set; } = string.Empty;

        [JsonPropertyName("thumbnailPath")]
        public string? ThumbnailPath { get; set; }

        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ImageStatus Status { get; set; } = ImageStatus.Pending;

        [JsonPropertyName("categoryID")]
        public string? CategoryID { get; set; }
    }

    public enum ImageStatus
    {
        [JsonPropertyName("pending")]
        Pending,
        [JsonPropertyName("kept")]
        Kept,
        [JsonPropertyName("discarded")]
        Discarded
    }

    public class SelectionData
    {
        [JsonPropertyName("categories")]
        public Dictionary<string, CategorySelection> Categories { get; set; } = new();

        [JsonPropertyName("submitted_at")]
        public string? SubmittedAt { get; set; }

        [JsonPropertyName("recheck_count")]
        public int RecheckCount { get; set; } = 0;
    }

    public class CategorySelection
    {
        [JsonPropertyName("selected_ids")]
        public List<string> SelectedIDs { get; set; } = new();

        [JsonPropertyName("maybe_ids")]
        public List<string> MaybeIDs { get; set; } = new();

        [JsonPropertyName("completed")]
        public bool Completed { get; set; } = false;
    }
}
