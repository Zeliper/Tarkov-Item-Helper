using System.IO;
using System.Text;
using System.Text.Json;
using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for loading map data from local JSON files.
    /// All other data is now loaded from tarkov_data.db via specialized services.
    /// </summary>
    public class TarkovDevApiService : IDisposable
    {
        private static TarkovDevApiService? _instance;
        public static TarkovDevApiService Instance => _instance ??= new TarkovDevApiService();

        public TarkovDevApiService()
        {
        }

        /// <summary>
        /// Load maps from JSON file
        /// </summary>
        public async Task<List<TarkovMap>?> LoadMapsFromJsonAsync(string? fileName = null)
        {
            fileName ??= "maps.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<TarkovMap>>(json);
        }

        public void Dispose()
        {
        }
    }
}
