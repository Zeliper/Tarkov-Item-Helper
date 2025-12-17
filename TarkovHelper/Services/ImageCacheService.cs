using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using TarkovHelper.Debug;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for caching images from the web (wiki images, item icons)
    /// </summary>
    public class ImageCacheService : IDisposable
    {
        private static ImageCacheService? _instance;
        public static ImageCacheService Instance => _instance ??= new ImageCacheService();

        private readonly HttpClient _httpClient;
        private readonly string _imageCachePath;
        private readonly string _localIconsPath;
        private readonly Dictionary<string, BitmapImage> _memoryCache = new();
        private readonly LinkedList<string> _lruOrder = new(); // Track access order for LRU eviction
        private readonly object _cacheLock = new();

        // Memory cache limit (max number of images to keep in memory)
        private const int MaxMemoryCacheSize = 200;

        // Wiki image base URL
        private const string WikiImageBaseUrl = "https://escapefromtarkov.fandom.com/wiki/Special:FilePath/";

        public ImageCacheService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovHelper/1.0");
            _imageCachePath = Path.Combine(AppEnv.CachePath, "Images");
            _localIconsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icons");
            Directory.CreateDirectory(_imageCachePath);
        }

        /// <summary>
        /// Get a wiki image by file name (e.g., "Delivery from the past Customs.png")
        /// </summary>
        public async Task<BitmapImage?> GetWikiImageAsync(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            // Build the wiki URL
            var encodedFileName = Uri.EscapeDataString(fileName.Replace(" ", "_"));
            var url = WikiImageBaseUrl + encodedFileName;

            return await GetImageAsync(url, "wiki");
        }

        /// <summary>
        /// Get an item icon from local Assets/icons folder by item ID
        /// </summary>
        public BitmapImage? GetLocalItemIcon(string? itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            // Check memory cache first
            var cacheKey = $"local:{itemId}";
            lock (_cacheLock)
            {
                if (_memoryCache.TryGetValue(cacheKey, out var cachedImage))
                {
                    _lruOrder.Remove(cacheKey);
                    _lruOrder.AddLast(cacheKey);
                    return cachedImage;
                }
            }

            // Load from local Assets/icons folder
            var localPath = Path.Combine(_localIconsPath, $"{itemId}.png");
            if (!File.Exists(localPath))
            {
                return null;
            }

            var image = LoadImageFromFile(localPath);
            if (image != null)
            {
                lock (_cacheLock)
                {
                    AddToMemoryCache(cacheKey, image);
                }
            }

            return image;
        }

        /// <summary>
        /// Get an item icon from local Assets/icons folder by item ID (async wrapper)
        /// </summary>
        public Task<BitmapImage?> GetItemIconAsync(string? itemId)
        {
            return Task.FromResult(GetLocalItemIcon(itemId));
        }

        /// <summary>
        /// Get an image from URL, using cache if available
        /// </summary>
        public async Task<BitmapImage?> GetImageAsync(string url, string category = "misc")
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // Check memory cache first
            lock (_cacheLock)
            {
                if (_memoryCache.TryGetValue(url, out var cachedImage))
                {
                    // Move to end of LRU list (most recently used)
                    _lruOrder.Remove(url);
                    _lruOrder.AddLast(url);
                    return cachedImage;
                }
            }

            // Check disk cache
            var cacheFileName = GetCacheFileName(url);
            var categoryPath = Path.Combine(_imageCachePath, category);
            Directory.CreateDirectory(categoryPath);
            var cacheFilePath = Path.Combine(categoryPath, cacheFileName);

            if (File.Exists(cacheFilePath))
            {
                try
                {
                    var image = LoadImageFromFile(cacheFilePath);
                    if (image != null)
                    {
                        lock (_cacheLock)
                        {
                            AddToMemoryCache(url, image);
                        }
                        return image;
                    }
                }
                catch
                {
                    // If cache file is corrupted, delete and re-download
                    try { File.Delete(cacheFilePath); } catch { }
                }
            }

            // Download from web
            try
            {
                var imageBytes = await _httpClient.GetByteArrayAsync(url);

                // Save to disk cache
                await File.WriteAllBytesAsync(cacheFilePath, imageBytes);

                // Load and cache in memory
                var image = LoadImageFromBytes(imageBytes);
                if (image != null)
                {
                    lock (_cacheLock)
                    {
                        AddToMemoryCache(url, image);
                    }
                }

                return image;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Add image to memory cache with LRU eviction
        /// Must be called within _cacheLock
        /// </summary>
        private void AddToMemoryCache(string url, BitmapImage image)
        {
            // If already in cache, just update LRU order
            if (_memoryCache.ContainsKey(url))
            {
                _lruOrder.Remove(url);
                _lruOrder.AddLast(url);
                return;
            }

            // Evict oldest entries if cache is full
            while (_memoryCache.Count >= MaxMemoryCacheSize && _lruOrder.First != null)
            {
                var oldestUrl = _lruOrder.First.Value;
                _lruOrder.RemoveFirst();
                _memoryCache.Remove(oldestUrl);
            }

            // Add new entry
            _memoryCache[url] = image;
            _lruOrder.AddLast(url);
        }

        /// <summary>
        /// Preload multiple images in parallel
        /// </summary>
        public async Task PreloadImagesAsync(IEnumerable<string> urls, string category = "misc")
        {
            var tasks = urls.Select(url => GetImageAsync(url, category));
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Clear memory cache (disk cache remains)
        /// </summary>
        public void ClearMemoryCache()
        {
            lock (_cacheLock)
            {
                _memoryCache.Clear();
                _lruOrder.Clear();
            }
        }

        /// <summary>
        /// Clear all cached images (both memory and disk)
        /// </summary>
        public void ClearAllCache()
        {
            ClearMemoryCache();

            try
            {
                if (Directory.Exists(_imageCachePath))
                {
                    Directory.Delete(_imageCachePath, true);
                    Directory.CreateDirectory(_imageCachePath);
                }
            }
            catch
            {
                // Ignore errors during cache cleanup
            }
        }

        /// <summary>
        /// Generate a unique cache file name from URL
        /// </summary>
        private static string GetCacheFileName(string url)
        {
            using var md5 = MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(url);
            var hashBytes = md5.ComputeHash(inputBytes);
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            // Try to preserve original extension
            var extension = ".png";
            var uri = new Uri(url);
            var lastSegment = uri.Segments.LastOrDefault() ?? "";
            var originalExtension = Path.GetExtension(lastSegment);
            if (!string.IsNullOrEmpty(originalExtension))
            {
                extension = originalExtension;
            }

            return hash + extension;
        }

        /// <summary>
        /// Load a BitmapImage from file path
        /// </summary>
        private static BitmapImage? LoadImageFromFile(string filePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // Important for cross-thread access
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a BitmapImage from byte array
        /// </summary>
        private static BitmapImage? LoadImageFromBytes(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze(); // Important for cross-thread access
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
