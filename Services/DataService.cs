using System.Text;
using System.Text.Json;
using KpuRegion.Models;
using KpuRegion.Helpers;

namespace KpuRegion.Services
{
    public class DataService : IDataService
    {
        private readonly HttpClient _client;

        public DataService()
        {
            _client = new HttpClient();
        }

        public async Task<string> GetProvinsiData()
        {
            
            try
            {
                return await FetchAndProcessData("0.json", AppSettings.FileNames.FILENAME_PROVINSI, true);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting provinsi data: {ex.Message}");
                throw;

            }
        }

        public async Task<string> GetKabkotData(string kode)
        {
            var fileLock = FileLockHelper.GetLock(AppSettings.FileNames.FILENAME_KABKOT);
            await fileLock.WaitAsync();
            try
            {
                return await FetchAndProcessData($"{kode}.json", AppSettings.FileNames.FILENAME_KABKOT);
            }
            finally
            {

                fileLock.Release();
            }
        }

        public async Task<string> GetKecamatanData(string kode)
        {
            var fileLock = FileLockHelper.GetLock(AppSettings.FileNames.FILENAME_KECAMATAN);
            await fileLock.WaitAsync();
            try
            {
                return await FetchAndProcessData($"{kode[..2]}/{kode}.json", AppSettings.FileNames.FILENAME_KECAMATAN);
            }
            finally
            {

                fileLock.Release();
            }
        }

        public async Task<string> GetKelurahanData(string kode)
        {
            var fileLock = FileLockHelper.GetLock(AppSettings.FileNames.FILENAME_KELURAHAN);
            await fileLock.WaitAsync();
            try
            {
                return await FetchAndProcessData($"{kode[..2]}/{kode[..4]}/{kode}.json", AppSettings.FileNames.FILENAME_KELURAHAN);
            }
            finally
            {

                fileLock.Release();
            }
        }

        private async Task<string> FetchAndProcessData(string path, string filename, bool excludeLuarNegeri = false)
        {
            try
            {
                string jsonUri = AppSettings.Endpoints.URI_JSON_KPU + path;
                // Logger.LogInfo($"Fetching data from URI:");
                // await Logger.LogProgressAsync($"{jsonUri}");

                string jsonData = await FetchJsonDataAsync(jsonUri);
                if (string.IsNullOrWhiteSpace(jsonData))
                {
                    Logger.LogError("No data retrieved from the source.");
                    return string.Empty;
                }

                var objects = JsonSerializer.Deserialize<List<ObjectJson>>(jsonData);
                if (objects?.Count > 0)
                {
                    if (excludeLuarNegeri)
                    {
                        objects = objects.Where(obj => obj.nama != "Luar Negeri").ToList();
                    }

                    var options = new CsvOptions
                    {
                        Delimiter = ",",
                        IncludeHeader = true,
                        Encoding = Encoding.UTF8,
                        IgnoreNullValues = true,
                        InitialBufferSize = AppSettings.Configuration.BUFFER_SIZE,
                        Append = true, // Keep append true
                        CapitalizeExceptRoman = true
                    };

                    // Implement retry logic with file locking handling
                    int maxRetries = 3;
                    int currentRetry = 0;
                    while (currentRetry < maxRetries)
                    {
                        try
                        {
                            string result = await CsvConverter.SaveToCsvAsync(objects, filename, options);
                            return result;
                        }
                        catch (IOException) when (currentRetry < maxRetries - 1)
                        {
                            currentRetry++;
                            await Task.Delay(500 * currentRetry); // Progressive delay
                            continue;
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in FetchAndProcessData: {ex.Message}");
                throw;
            }
        }

        private async Task<string> FetchJsonDataAsync(string uri)
        {
            try
            {
                var response = await _client.GetAsync(uri);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException e)
            {
                Logger.LogError($"Request error: {e.Message}");
                return string.Empty;
            }
            catch (Exception e)
            {
                Logger.LogError($"Unexpected error: {e.Message}");
                return string.Empty;
            }
        }
    }
}