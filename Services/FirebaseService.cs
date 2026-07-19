using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using SnapPickWin.Models;

namespace SnapPickWin.Services
{
    public class FirebaseService
    {
        private static readonly HttpClient HttpClient = new();
        
        public FirebaseConfig Config { get; private set; } = new();
        public string? IdToken { get; private set; }
        public string? LocalId { get; private set; } // Firebase User UID
        public string? Email { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(IdToken);

        private readonly string _sessionFilePath;

        public FirebaseService()
        {
            // Session path in AppData or local directory
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string snapPickDir = Path.Combine(appData, "SnapPickWin");
            Directory.CreateDirectory(snapPickDir);
            _sessionFilePath = Path.Combine(snapPickDir, "shared_session.json");

            LoadConfigFromPlist();
            LoadSession();
        }

        public void LoadConfigFromPlist()
        {
            XDocument? doc = null;

            // 1. Try loading from Assembly Embedded Resource
            try
            {
                var assembly = typeof(FirebaseService).Assembly;
                using (Stream? stream = assembly.GetManifestResourceStream("SnapPickWin.GoogleService-Info.plist"))
                {
                    if (stream != null)
                    {
                        doc = XDocument.Load(stream);
                        Console.WriteLine("🔑 FirebaseService: Config successfully loaded from embedded resource.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ FirebaseService: Failed loading embedded plist: {ex.Message}");
            }

            // 2. Fall back to local filesystem if embedded loading didn't succeed
            if (doc == null)
            {
                string plistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GoogleService-Info.plist");
                if (!File.Exists(plistPath))
                {
                    plistPath = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName ?? "", "GoogleService-Info.plist");
                }

                if (File.Exists(plistPath))
                {
                    try
                    {
                        doc = XDocument.Load(plistPath);
                        Console.WriteLine($"🔑 FirebaseService: Config loaded from filesystem: {plistPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ FirebaseService: Error parsing local GoogleService-Info.plist: {ex.Message}");
                    }
                }
            }

            if (doc != null)
            {
                try
                {
                    var dict = doc.Element("plist")?.Element("dict");
                    if (dict != null)
                    {
                        var keys = new List<string>();
                        var values = new List<string>();
                        
                        foreach (var node in dict.Elements())
                        {
                            if (node.Name.LocalName == "key")
                            {
                                keys.Add(node.Value);
                            }
                            else if (node.Name.LocalName == "string")
                            {
                                values.Add(node.Value);
                            }
                        }

                        var config = new FirebaseConfig();
                        for (int i = 0; i < Math.Min(keys.Count, values.Count); i++)
                        {
                            switch (keys[i])
                            {
                                case "API_KEY":
                                    config.ApiKey = values[i];
                                    break;
                                case "PROJECT_ID":
                                    config.ProjectId = values[i];
                                    break;
                                case "STORAGE_BUCKET":
                                    config.StorageBucket = values[i];
                                    break;
                                case "GOOGLE_APP_ID":
                                    config.GoogleAppID = values[i];
                                    break;
                                case "GCM_SENDER_ID":
                                    config.GcmSenderID = values[i];
                                    break;
                            }
                        }
                        Config = config;
                        Console.WriteLine($"🔑 FirebaseService: Project ID: {Config.ProjectId}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ FirebaseService: Error parsing plist XML dictionary: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("🔑 FirebaseService: GoogleService-Info.plist config not found (either embedded or in filesystem).");
            }
        }

        private void LoadSession()
        {
            if (File.Exists(_sessionFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_sessionFilePath);
                    var session = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (session != null && session.TryGetValue("idToken", out string? token) && session.TryGetValue("localId", out string? uid) && session.TryGetValue("email", out string? email))
                    {
                        IdToken = token;
                        LocalId = uid;
                        Email = email;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ FirebaseService: Error loading session: {ex.Message}");
                }
            }
        }

        public void SaveSession(string token, string uid, string email)
        {
            IdToken = token;
            LocalId = uid;
            Email = email;

            try
            {
                var sessionData = new Dictionary<string, string>
                {
                    { "apiKey", Config.ApiKey },
                    { "projectId", Config.ProjectId },
                    { "storageBucket", Config.StorageBucket },
                    { "googleAppID", Config.GoogleAppID },
                    { "gcmSenderID", Config.GcmSenderID },
                    { "localId", uid },
                    { "email", email },
                    { "idToken", token }
                };

                string json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_sessionFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FirebaseService: Error saving session: {ex.Message}");
            }
        }

        public void Logout()
        {
            IdToken = null;
            LocalId = null;
            Email = null;

            try
            {
                if (File.Exists(_sessionFilePath))
                {
                    File.Delete(_sessionFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FirebaseService: Error deleting session: {ex.Message}");
            }
        }

        // MARK: - Auth REST APIs

        public async Task SignUpAsync(string email, string password)
        {
            if (string.IsNullOrEmpty(Config.ApiKey))
                throw new InvalidOperationException("API Key is missing. GoogleService-Info.plist may not be loaded.");

            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={Config.ApiKey}";
            var payload = new
            {
                email = email,
                password = password,
                returnSecureToken = true
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            
            string responseString = await response.Content.ReadAsStringAsync();
            VerifyResponse(response, responseString);

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            string token = root.GetProperty("idToken").GetString() ?? throw new Exception("No ID Token returned.");
            string uid = root.GetProperty("localId").GetString() ?? throw new Exception("No local ID returned.");

            SaveSession(token, uid, email);
        }

        public async Task SignInAsync(string email, string password)
        {
            if (string.IsNullOrEmpty(Config.ApiKey))
                throw new InvalidOperationException("API Key is missing. GoogleService-Info.plist may not be loaded.");

            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={Config.ApiKey}";
            var payload = new
            {
                email = email,
                password = password,
                returnSecureToken = true
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);

            string responseString = await response.Content.ReadAsStringAsync();
            VerifyResponse(response, responseString);

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            string token = root.GetProperty("idToken").GetString() ?? throw new Exception("No ID Token returned.");
            string uid = root.GetProperty("localId").GetString() ?? throw new Exception("No local ID returned.");

            SaveSession(token, uid, email);
        }

        // MARK: - Storage REST API

        public async Task<string> UploadThumbnailAsync(string projectID, string filePath)
        {
            if (string.IsNullOrEmpty(IdToken))
                throw new InvalidOperationException("User is unauthenticated.");
            if (string.IsNullOrEmpty(Config.StorageBucket))
                throw new InvalidOperationException("Storage bucket is missing.");

            string fileName = Path.GetFileName(filePath);
            string storagePath = $"projects/{projectID}/{fileName}";
            string escapedPath = Uri.EscapeDataString(storagePath);

            string url = $"https://firebasestorage.googleapis.com/v0/b/{Config.StorageBucket}/o?uploadType=media&name={escapedPath}";

            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);
            
            string ext = Path.GetExtension(filePath).ToLower().TrimStart('.');
            string mimeType = ext == "webp" ? "image/webp" : (ext == "png" ? "image/png" : "image/jpeg");
            
            var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            request.Content = content;

            var response = await HttpClient.SendAsync(request);
            string responseString = await response.Content.ReadAsStringAsync();
            VerifyResponse(response, responseString);

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (root.TryGetProperty("downloadTokens", out var tokenProp))
            {
                string downloadToken = tokenProp.GetString() ?? "";
                return $"https://firebasestorage.googleapis.com/v0/b/{Config.StorageBucket}/o/{escapedPath}?alt=media&token={downloadToken}";
            }
            throw new Exception("Failed to retrieve download tokens from Storage response.");
        }

        // MARK: - Firestore REST API

        public async Task WriteProjectAsync(SnapPickProject project, string status, string token)
        {
            if (string.IsNullOrEmpty(Config.ProjectId))
                throw new InvalidOperationException("Project ID is missing.");
            if (string.IsNullOrEmpty(LocalId))
                throw new InvalidOperationException("User is unauthenticated.");

            string docPath = $"projects/{project.Id}";
            string url = $"https://firestore.googleapis.com/v1/projects/{Config.ProjectId}/databases/(default)/documents/{docPath}";

            var fields = new Dictionary<string, object>
            {
                { "owner_uid", new { stringValue = LocalId } },
                { "name", new { stringValue = project.Title } },
                { "status", new { stringValue = status } },
                { "share_token", new { stringValue = token } },
                { "created_at", new { integerValue = new DateTimeOffset(project.CreatedAt).ToUnixTimeMilliseconds().ToString() } }
            };

            var docPayload = new { fields = fields };
            string jsonPayload = JsonSerializer.Serialize(docPayload);

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (IdToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);

            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await HttpClient.SendAsync(request);
            string responseString = await response.Content.ReadAsStringAsync();
            VerifyResponse(response, responseString);
        }

        public async Task WriteCategoryAsync(string projectID, PhotoCategory category)
        {
            if (string.IsNullOrEmpty(Config.ProjectId))
                throw new InvalidOperationException("Project ID is missing.");

            string docPath = $"projects/{projectID}/categories/{category.Id}";
            string url = $"https://firestore.googleapis.com/v1/projects/{Config.ProjectId}/databases/(default)/documents/{docPath}";

            var fields = new Dictionary<string, object>
            {
                { "name", new { stringValue = category.Name } },
                { "display_order", new { integerValue = category.DisplayOrder.ToString() } },
                { "photo_count", new { integerValue = category.PhotoCount.ToString() } }
            };

            var docPayload = new { fields = fields };
            string jsonPayload = JsonSerializer.Serialize(docPayload);

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (IdToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);

            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await HttpClient.SendAsync(request);
            string responseString = await response.Content.ReadAsStringAsync();
            VerifyResponse(response, responseString);
        }

        public async Task WritePhotoAsync(string projectID, string categoryID, ProjectImageRef photo, string localFilePath, int displayOrder)
        {
            // 1. Upload thumbnail to Storage first
            string publicURL = await UploadThumbnailAsync(projectID, localFilePath);

            // 2. Write document to Firestore
            if (string.IsNullOrEmpty(Config.ProjectId))
                throw new InvalidOperationException("Project ID is missing.");

            string docPath = $"projects/{projectID}/categories/{categoryID}/photos/{photo.Id}";
            string url = $"https://firestore.googleapis.com/v1/projects/{Config.ProjectId}/databases/(default)/documents/{docPath}";

            string fileName = Path.GetFileName(localFilePath);

            var fields = new Dictionary<string, object>
            {
                { "original_filename", new { stringValue = fileName } },
                { "thumbnail_url", new { stringValue = publicURL } },
                { "display_order", new { integerValue = displayOrder.ToString() } }
            };

            var docPayload = new { fields = fields };
            string jsonPayload = JsonSerializer.Serialize(docPayload);

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (IdToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);

            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await HttpClient.SendAsync(request);
            string responseString = await response.Content.ReadAsStringAsync();
            VerifyResponse(response, responseString);
        }

        public async Task<SelectionData?> FetchSelectionsAsync(string projectID)
        {
            if (string.IsNullOrEmpty(Config.ProjectId))
                throw new InvalidOperationException("Project ID is missing.");

            string docPath = $"projects/{projectID}/selection/client_selection";
            string url = $"https://firestore.googleapis.com/v1/projects/{Config.ProjectId}/databases/(default)/documents/{docPath}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (IdToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);

            var response = await HttpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            string responseString = await response.Content.ReadAsStringAsync();
            VerifyResponse(response, responseString);

            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            if (!root.TryGetProperty("fields", out var fields))
            {
                return null;
            }

            var selection = new SelectionData();

            if (fields.TryGetProperty("submitted_at", out var submittedAtProp) && submittedAtProp.TryGetProperty("stringValue", out var subVal))
            {
                selection.SubmittedAt = subVal.GetString();
            }

            if (fields.TryGetProperty("recheck_count", out var recheckProp))
            {
                if (recheckProp.TryGetProperty("integerValue", out var intValStr))
                {
                    int.TryParse(intValStr.GetString(), out int count);
                    selection.RecheckCount = count;
                }
            }

            if (fields.TryGetProperty("categories", out var categoriesProp) &&
                categoriesProp.TryGetProperty("mapValue", out var mapValue) &&
                mapValue.TryGetProperty("fields", out var catFields))
            {
                foreach (var catField in catFields.EnumerateObject())
                {
                    string categoryID = catField.Name;
                    var catVal = catField.Value;

                    if (catVal.TryGetProperty("mapValue", out var catMapVal) && catMapVal.TryGetProperty("fields", out var fieldsMap))
                    {
                        var selectedIDs = new List<string>();
                        var maybeIDs = new List<string>();
                        bool isCompleted = false;

                        if (fieldsMap.TryGetProperty("selected_ids", out var idsVal) &&
                            idsVal.TryGetProperty("arrayValue", out var arrVal) &&
                            arrVal.TryGetProperty("values", out var values))
                        {
                            foreach (var val in values.EnumerateArray())
                            {
                                if (val.TryGetProperty("stringValue", out var str))
                                    selectedIDs.Add(str.GetString() ?? "");
                            }
                        }

                        if (fieldsMap.TryGetProperty("maybe_ids", out var maybeVal) &&
                            maybeVal.TryGetProperty("arrayValue", out var arrValM) &&
                            arrValM.TryGetProperty("values", out var valuesM))
                        {
                            foreach (var val in valuesM.EnumerateArray())
                            {
                                if (val.TryGetProperty("stringValue", out var str))
                                    maybeIDs.Add(str.GetString() ?? "");
                            }
                        }

                        if (fieldsMap.TryGetProperty("completed", out var compVal) && compVal.TryGetProperty("booleanValue", out var boolVal))
                        {
                            isCompleted = boolVal.GetBoolean();
                        }

                        selection.Categories[categoryID] = new CategorySelection
                        {
                            SelectedIDs = selectedIDs,
                            MaybeIDs = maybeIDs,
                            Completed = isCompleted
                        };
                    }
                }
            }

            return selection;
        }

        public async Task DeleteSelectionsAsync(string projectID)
        {
            if (string.IsNullOrEmpty(Config.ProjectId))
                throw new InvalidOperationException("Project ID is missing.");

            string docPath = $"projects/{projectID}/selection/client_selection";
            string url = $"https://firestore.googleapis.com/v1/projects/{Config.ProjectId}/databases/(default)/documents/{docPath}";

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            if (IdToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);

            var response = await HttpClient.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.NotFound) // Ignore 404
            {
                string responseString = await response.Content.ReadAsStringAsync();
                VerifyResponse(response, responseString);
            }
        }

        public async Task DeleteStorageFileAsync(string projectID, string filename)
        {
            if (string.IsNullOrEmpty(IdToken))
                throw new InvalidOperationException("User is unauthenticated.");
            if (string.IsNullOrEmpty(Config.StorageBucket))
                throw new InvalidOperationException("Storage bucket is missing.");

            string storagePath = $"projects/{projectID}/{filename}";
            string escapedPath = Uri.EscapeDataString(storagePath);

            string url = $"https://firebasestorage.googleapis.com/v0/b/{Config.StorageBucket}/o/{escapedPath}";

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);

            var response = await HttpClient.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.NotFound) // Ignore 404
            {
                string responseString = await response.Content.ReadAsStringAsync();
                VerifyResponse(response, responseString);
            }
        }

        public async Task DeleteProjectCloudDataAsync(string projectID, List<string> categoryIDs, List<string> photoPaths, List<string> storageFiles)
        {
            var errors = new List<Exception>();

            // 1. Delete Storage thumbnail files
            foreach (var file in storageFiles)
            {
                try
                {
                    await DeleteStorageFileAsync(projectID, file);
                    Console.WriteLine($"Deleted Firebase Storage thumbnail: {file}");
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    Console.WriteLine($"Failed to delete storage file {file}: {ex.Message}");
                }
            }

            // 2. Delete Firestore photos
            foreach (var path in photoPaths)
            {
                string docPath = $"projects/{projectID}/{path}";
                try
                {
                    await DeleteFirestoreDocumentAsync(docPath);
                    Console.WriteLine($"Deleted Firestore photo document: {docPath}");
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    Console.WriteLine($"Failed to delete Firestore photo {docPath}: {ex.Message}");
                }
            }

            // 3. Delete Firestore categories
            foreach (var categoryID in categoryIDs)
            {
                string docPath = $"projects/{projectID}/categories/{categoryID}";
                try
                {
                    await DeleteFirestoreDocumentAsync(docPath);
                    Console.WriteLine($"Deleted Firestore category document: {docPath}");
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    Console.WriteLine($"Failed to delete Firestore category {docPath}: {ex.Message}");
                }
            }

            // 4. Delete selection document
            try
            {
                await DeleteSelectionsAsync(projectID);
                Console.WriteLine($"Deleted Firestore selection document for project {projectID}");
            }
            catch (Exception ex)
            {
                errors.Add(ex);
                Console.WriteLine($"Failed to delete Firestore selection: {ex.Message}");
            }

            // 5. Delete parent project document
            string projectDocPath = $"projects/{projectID}";
            try
            {
                await DeleteFirestoreDocumentAsync(projectDocPath);
                Console.WriteLine($"Deleted Firestore project document: {projectDocPath}");
            }
            catch (Exception ex)
            {
                errors.Add(ex);
                Console.WriteLine($"Failed to delete Firestore project document: {projectDocPath}");
            }

            if (errors.Count > 0)
            {
                throw new AggregateException($"Deletion failed with {errors.Count} errors.", errors);
            }
        }

        private async Task DeleteFirestoreDocumentAsync(string docPath)
        {
            if (string.IsNullOrEmpty(Config.ProjectId))
                throw new InvalidOperationException("Project ID is missing.");

            string url = $"https://firestore.googleapis.com/v1/projects/{Config.ProjectId}/databases/(default)/documents/{docPath}";

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            if (IdToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdToken);

            var response = await HttpClient.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.NotFound) // Ignore 404
            {
                string responseString = await response.Content.ReadAsStringAsync();
                VerifyResponse(response, responseString);
            }
        }

        private static void VerifyResponse(HttpResponseMessage response, string content)
        {
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"🔑 FirebaseService Network Error Status {response.StatusCode}: {content}");
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var errorProp) && errorProp.TryGetProperty("message", out var msgProp))
                    {
                        throw new Exception(msgProp.GetString() ?? "Unknown API Error");
                    }
                }
                catch (JsonException)
                {
                    // Fall through
                }
                throw new HttpRequestException($"HTTP request failed with status code {response.StatusCode}.", null, response.StatusCode);
            }
        }
    }

    public class FirebaseConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string StorageBucket { get; set; } = string.Empty;
        public string GoogleAppID { get; set; } = string.Empty;
        public string GcmSenderID { get; set; } = string.Empty;
    }
}
