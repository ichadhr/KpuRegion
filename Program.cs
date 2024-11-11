using System.Diagnostics;
using KpuRegion.Services;
using KpuRegion.Models;
using KpuRegion.Helpers;
using Spectre.Console;

namespace KpuRegion
{
    class Program
    {
        private static readonly IDataService _dataService = new DataService();
        private static readonly SemaphoreSlim _semaphore = new(AppSettings.Configuration.MAX_CONCURRENT_TASKS);
        private const int MAX_RETRIES = 3;
        private const int BATCH_SIZE = 100;

        private static readonly List<(string FileName, string EntityName, Func<string, Task> ProcessFunc)> _processConfigs = new()
        {
            (AppSettings.FileNames.FILENAME_PROVINSI, "Kabupaten/Kota", _dataService.GetKabkotData),
            (AppSettings.FileNames.FILENAME_KABKOT, "Kecamatan", _dataService.GetKecamatanData),
            (AppSettings.FileNames.FILENAME_KECAMATAN, "Kelurahan", _dataService.GetKelurahanData)
        };

        static async Task Main()
        {
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource();

            try
            {
                Logger.LogInfo("Getting data provinsi.");
                string dataProvinsi = await WithRetry(() => _dataService.GetProvinsiData());
                if (string.IsNullOrEmpty(dataProvinsi))
                {
                    Logger.LogWarning("No provinsi data received.");
                    return;
                }

                foreach (var config in _processConfigs)
                {
                    Logger.LogInfo($"Getting data {config.EntityName.ToLower()}.");
                    await ProcessRegionalData(config.FileName, config.EntityName, config.ProcessFunc, cts.Token);
                }
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
                var records = await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("green"))
                    .StartAsync($"Reading {entityName} data...", async ctx =>
                    {
                        return await Task.Run(() =>
                            CsvConverter.ReadCsv(fileName, AppSettings.Configuration.BUFFER_SIZE)
                                .Where(r => !string.IsNullOrEmpty(r.kode))
                                .ToList(),
                            cancellationToken);
                    });

                var totalCount = records.Count;
                var processedCount = 0;

                await AnsiConsole.Progress()
                    .AutoClear(true)
                    .HideCompleted(false)
                    .Columns(
                    [
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn(),
                    ])
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask($"Processing {entityName}", maxValue: totalCount);

                        foreach (var batch in records.Chunk(BATCH_SIZE))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var batchTasks = batch.Select(async record =>
                            {
                                await _semaphore.WaitAsync(cancellationToken);
                                try
                                {
                                    await WithRetry(async () =>
                                    {
                                        if (!string.IsNullOrEmpty(record.kode))
                                        {
                                            await processFunction(record.kode!);
                                            Interlocked.Increment(ref processedCount);
                                            task.Increment(1);
                                        }
                                    });
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            });

                            await Task.WhenAll(batchTasks);
                        }
                    });

                Logger.LogInfo($"✅ Completed {entityName} processing. Total processed: {processedCount:N0} records");
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

        private static async Task<string> WithRetry(Func<Task> action, int maxRetries = MAX_RETRIES)
        {
            return await WithRetry(async () =>
            {
                await action();
                return string.Empty;
            }, maxRetries);
        }
    }
}