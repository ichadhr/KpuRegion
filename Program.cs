using System.Diagnostics;
using kpu.Services;
using kpu.Helpers;
using kpu.Models;

namespace kpu
{
    class Program
    {
        private static readonly IDataService _dataService = new DataService();
        private static readonly SemaphoreSlim _semaphore = new(AppSettings.Configuration.MAX_CONCURRENT_TASKS);
        private const int PROGRESS_LOG_INTERVAL = 10;
        private const int MAX_RETRIES = 3;
        private const int BATCH_SIZE = 100;

        static async Task Main()
        {
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource();
            
            Logger.LogInfo("Application started.");

            try
            {
                string dataProvinsi = await WithRetry(() => _dataService.GetProvinsiData());
                if (string.IsNullOrEmpty(dataProvinsi))
                {
                    Logger.LogWarning("No provinsi data received.");
                    return;
                }

                // Process hierarchical data
                await ProcessRegionalData(
                    AppSettings.FileNames.FILENAME_PROVINSI,
                    "Kabupaten/Kota",
                    _dataService.GetKabkotData,
                    cts.Token);

                await ProcessRegionalData(
                    AppSettings.FileNames.FILENAME_KABKOT,
                    "Kecamatan",
                    _dataService.GetKecamatanData,
                    cts.Token);

                await ProcessRegionalData(
                    AppSettings.FileNames.FILENAME_KECAMATAN,
                    "Kelurahan",
                    _dataService.GetKelurahanData,
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Application error: {ex.Message}");
                throw;
            }
            finally
            {
                sw.Stop();
                _semaphore.Dispose();
                Logger.LogInfo($"Application finished. Total time: {sw.Elapsed.TotalSeconds:F2} seconds");
            }
        }

        private static async Task ProcessRegionalData(
            string fileName,
            string entityName,
            Func<string, Task> processFunction,
            CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInfo($"Starting {entityName} processing...");

                // Read and filter records
                var records = await Task.Run(() =>
                    CsvConverter.ReadCsv(fileName, AppSettings.Configuration.BUFFER_SIZE)
                        .Where(r => !string.IsNullOrEmpty(r.kode))
                        .ToList(),
                    cancellationToken);

                var totalCount = records.Count;
                var processedCount = 0;

                // Process in batches
                foreach (var batch in records.Chunk(BATCH_SIZE))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batchTasks = batch.Select(async record =>
                    {
                        await _semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            await WithRetry<string>(async () =>
                            {
                                if (string.IsNullOrEmpty(record.kode))
                                {
                                    Logger.LogWarning($"Skipping {entityName} processing - empty kode found");
                                    return string.Empty;
                                }

                                await processFunction(record.kode!);
                                var count = Interlocked.Increment(ref processedCount);

                                if (count % PROGRESS_LOG_INTERVAL == 0)
                                {
                                    Logger.LogInfo($"{entityName} Progress: {count}/{totalCount}");
                                }
                                return string.Empty;
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Error processing {entityName} for kode {record.kode}: {ex.Message}");
                            throw;
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    });

                    await Task.WhenAll(batchTasks);
                }

                Logger.LogInfo($"Completed {entityName} processing. Total processed: {processedCount}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in Process{entityName}Data: {ex.Message}");
                throw;
            }
        }

        private static async Task<T> WithRetry<T>(Func<Task<T>> action, int maxRetries = MAX_RETRIES)
        {
            for (int i = 1; i <= maxRetries; i++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (i < maxRetries)
                {
                    Logger.LogWarning($"Retry attempt {i} of {maxRetries}. Error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
                }
            }

            return await action();
        }

        private static Task WithRetry(Func<Task> action, int maxRetries = MAX_RETRIES)
            => WithRetry<string>(async () =>
            {
                await action();
                return string.Empty;
            }, maxRetries);
    }
}