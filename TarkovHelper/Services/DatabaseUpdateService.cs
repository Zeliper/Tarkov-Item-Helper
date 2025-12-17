using System.IO;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// tarkov_data.db 업데이트를 관리하는 서비스.
/// GitHub에서 버전을 확인하고 새 버전이 있으면 자동으로 다운로드.
/// 5분마다 백그라운드에서 업데이트 체크.
/// </summary>
public sealed class DatabaseUpdateService : IDisposable
{
    private static readonly ILogger _log = Log.For<DatabaseUpdateService>();
    private static DatabaseUpdateService? _instance;
    public static DatabaseUpdateService Instance => _instance ??= new DatabaseUpdateService();

    private const string VERSION_URL = "https://raw.githubusercontent.com/Zeliper/Tarkov-Item-Helper/refs/heads/main/TarkovHelper/Assets/db_version.txt";
    private const string DATABASE_URL = "https://raw.githubusercontent.com/Zeliper/Tarkov-Item-Helper/refs/heads/main/TarkovHelper/Assets/tarkov_data.db";
    private const string LOCAL_VERSION_FILE = "db_version.txt";
    private const string DATABASE_FILE = "tarkov_data.db";
    private const int UPDATE_INTERVAL_MS = 5 * 60 * 1000; // 5분

    private readonly string _assetsPath;
    private readonly string _databasePath;
    private readonly string _versionFilePath;
    private readonly HttpClient _httpClient;
    private readonly System.Threading.Timer _updateTimer;
    private bool _isUpdating;
    private bool _disposed;

    /// <summary>
    /// 데이터베이스 파일 경로
    /// </summary>
    public string DatabasePath => _databasePath;

    /// <summary>
    /// 현재 로컬 버전
    /// </summary>
    public string? LocalVersion { get; private set; }

    /// <summary>
    /// 최신 원격 버전
    /// </summary>
    public string? RemoteVersion { get; private set; }

    /// <summary>
    /// 업데이트 진행 중 여부
    /// </summary>
    public bool IsUpdating => _isUpdating;

    /// <summary>
    /// 데이터베이스가 업데이트되었을 때 발생하는 이벤트.
    /// 모든 DB 서비스는 이 이벤트를 구독하여 데이터를 리로드해야 함.
    /// </summary>
    public event EventHandler? DatabaseUpdated;

    /// <summary>
    /// 업데이트 체크 시작 시 발생
    /// </summary>
    public event EventHandler? UpdateCheckStarted;

    /// <summary>
    /// 업데이트 체크 완료 시 발생 (업데이트 여부와 관계없이)
    /// </summary>
    public event EventHandler<UpdateCheckResult>? UpdateCheckCompleted;

    private DatabaseUpdateService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _assetsPath = Path.Combine(appDir, "Assets");
        _databasePath = Path.Combine(_assetsPath, DATABASE_FILE);
        _versionFilePath = Path.Combine(_assetsPath, LOCAL_VERSION_FILE);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovHelper/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // 로컬 버전 로드
        LoadLocalVersion();

        // 5분마다 업데이트 체크 타이머 설정
        _updateTimer = new System.Threading.Timer(
            OnUpdateTimerElapsed,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <summary>
    /// 로컬 버전 파일에서 버전 정보 로드
    /// </summary>
    private void LoadLocalVersion()
    {
        try
        {
            if (File.Exists(_versionFilePath))
            {
                LocalVersion = File.ReadAllText(_versionFilePath).Trim();
                _log.Debug($"Local version: {LocalVersion}");
            }
            else
            {
                LocalVersion = null;
                _log.Debug("No local version file found");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Error loading local version: {ex.Message}");
            LocalVersion = null;
        }
    }

    /// <summary>
    /// 백그라운드 업데이트 체크 시작
    /// </summary>
    public void StartBackgroundUpdates()
    {
        _log.Info("Starting background update checks (every 5 minutes)");
        _updateTimer.Change(0, UPDATE_INTERVAL_MS); // 즉시 시작 후 5분마다 반복
    }

    /// <summary>
    /// 백그라운드 업데이트 체크 중지
    /// </summary>
    public void StopBackgroundUpdates()
    {
        _log.Info("Stopping background update checks");
        _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 타이머 콜백
    /// </summary>
    private async void OnUpdateTimerElapsed(object? state)
    {
        await CheckAndUpdateAsync();
    }

    /// <summary>
    /// 업데이트 확인 및 필요시 다운로드
    /// </summary>
    public async Task<UpdateCheckResult> CheckAndUpdateAsync()
    {
        if (_isUpdating)
        {
            _log.Debug("Update already in progress, skipping");
            return new UpdateCheckResult(false, false, "Update already in progress");
        }

        _isUpdating = true;
        UpdateCheckStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            // 1. 원격 버전 확인
            _log.Debug("Checking remote version...");
            var remoteVersion = await GetRemoteVersionAsync();

            if (string.IsNullOrEmpty(remoteVersion))
            {
                var result = new UpdateCheckResult(false, false, "Failed to get remote version");
                UpdateCheckCompleted?.Invoke(this, result);
                return result;
            }

            RemoteVersion = remoteVersion;
            _log.Debug($"Remote version: {remoteVersion}, Local version: {LocalVersion}");

            // 2. 버전 비교
            if (LocalVersion == remoteVersion)
            {
                _log.Debug("Database is up to date");
                var result = new UpdateCheckResult(true, false, "Database is up to date");
                UpdateCheckCompleted?.Invoke(this, result);
                return result;
            }

            // 3. 새 버전 다운로드
            _log.Info($"New version available: {remoteVersion}");
            var downloadSuccess = await DownloadDatabaseAsync();

            if (!downloadSuccess)
            {
                var result = new UpdateCheckResult(false, false, "Failed to download database");
                UpdateCheckCompleted?.Invoke(this, result);
                return result;
            }

            // 4. 버전 파일 업데이트
            await UpdateLocalVersionAsync(remoteVersion);

            // 5. 업데이트 완료 이벤트 발생
            _log.Info("Database updated successfully, notifying services...");
            OnDatabaseUpdated();

            var successResult = new UpdateCheckResult(true, true, $"Updated to version {remoteVersion}");
            UpdateCheckCompleted?.Invoke(this, successResult);
            return successResult;
        }
        catch (Exception ex)
        {
            _log.Error($"Error during update check: {ex.Message}");
            var result = new UpdateCheckResult(false, false, ex.Message);
            UpdateCheckCompleted?.Invoke(this, result);
            return result;
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// 원격 버전 정보 가져오기
    /// </summary>
    private async Task<string?> GetRemoteVersionAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(VERSION_URL);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to get remote version: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 데이터베이스 파일 다운로드
    /// </summary>
    private async Task<bool> DownloadDatabaseAsync()
    {
        try
        {
            _log.Info("Downloading database...");

            // Assets 폴더가 없으면 생성
            if (!Directory.Exists(_assetsPath))
            {
                Directory.CreateDirectory(_assetsPath);
            }

            // 임시 파일로 다운로드
            var tempPath = _databasePath + ".tmp";

            using (var response = await _httpClient.GetAsync(DATABASE_URL, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                _log.Debug($"Database size: {totalBytes} bytes");

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long downloadedBytes = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (double)downloadedBytes / totalBytes * 100;
                        _log.Trace($"Download progress: {progress:F1}%");
                    }
                }
            }

            // 기존 파일 백업 및 교체
            var backupPath = _databasePath + ".bak";
            if (File.Exists(_databasePath))
            {
                // SQLite 연결 풀 클리어 - 파일 핸들 해제를 위해 필수
                _log.Debug("Clearing SQLite connection pools...");
                SqliteConnection.ClearAllPools();

                // 연결 풀 클리어 후 파일 핸들이 해제될 시간 확보
                await Task.Delay(100);

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                // 파일 이동 재시도 로직 (연결 풀 해제 지연 대응)
                const int maxRetries = 3;
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        File.Move(_databasePath, backupPath);
                        break;
                    }
                    catch (IOException) when (retry < maxRetries - 1)
                    {
                        _log.Warning($"File move failed, retrying ({retry + 1}/{maxRetries})...");
                        SqliteConnection.ClearAllPools();
                        await Task.Delay(500 * (retry + 1));
                    }
                }
            }

            File.Move(tempPath, _databasePath);
            _log.Info("Database downloaded successfully");

            // 백업 파일 삭제
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to download database: {ex.Message}");

            // 다운로드 실패 시 임시 파일 정리
            var tempPath = _databasePath + ".tmp";
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            return false;
        }
    }

    /// <summary>
    /// 로컬 버전 파일 업데이트
    /// </summary>
    private async Task UpdateLocalVersionAsync(string version)
    {
        try
        {
            await File.WriteAllTextAsync(_versionFilePath, version);
            LocalVersion = version;
            _log.Debug($"Local version updated to: {version}");
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to update local version file: {ex.Message}");
        }
    }

    /// <summary>
    /// 데이터베이스 업데이트 완료 이벤트 발생
    /// </summary>
    private void OnDatabaseUpdated()
    {
        // UI 스레드에서 이벤트 발생
        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                DatabaseUpdated?.Invoke(this, EventArgs.Empty);
            });
        }
        else
        {
            DatabaseUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 수동 업데이트 체크 (UI에서 호출용)
    /// </summary>
    public async Task<UpdateCheckResult> ForceUpdateCheckAsync()
    {
        _log.Info("Manual update check requested");
        return await CheckAndUpdateAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _updateTimer.Dispose();
        _httpClient.Dispose();
    }
}

/// <summary>
/// 업데이트 체크 결과
/// </summary>
public class UpdateCheckResult
{
    public bool Success { get; }
    public bool WasUpdated { get; }
    public string Message { get; }

    public UpdateCheckResult(bool success, bool wasUpdated, string message)
    {
        Success = success;
        WasUpdated = wasUpdated;
        Message = message;
    }
}
