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
        private static readonly SemaphoreSlim _fileSemaphore = new(1);
        private const int MAX_RETRIES = 3;
        private const int BATCH_SIZE = 100;

        public static List<KeyValuePair<string, int>> _totalAllData = [];

        private static readonly List<(string FileName, string EntityName, Func<string, Task<string>> ProcessFunc, bool isProvinsi)> _processConfigs =
        [
            ("", "Provinsi", async (_) => await _dataService.GetProvinsiData(), true),
            (AppSettings.FileNames.FILENAME_PROVINSI, "Kabupaten/Kota", async (code) => {
                var result = await _dataService.GetKabkotData(code);
                if (string.IsNullOrEmpty(result))
                {
                    throw new Exception("CSV path not found in the result");
                }
                return result;
            }, false),
            (AppSettings.FileNames.FILENAME_KABKOT, "Kecamatan", async (code) => {
                var result = await _dataService.GetKecamatanData(code);
                if (string.IsNullOrEmpty(result))
                {
                    throw new Exception("CSV path not found in the result");
                }
                return result;
            }, false),
            (AppSettings.FileNames.FILENAME_KECAMATAN, "Kelurahan", async (code) => {
                var result = await _dataService.GetKelurahanData(code);
                if (string.IsNullOrEmpty(result))
                {
                    throw new Exception("CSV path not found in the result");
                }
                return result;
            }, false)
        ];

        static async Task Main()
        {
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource();

            try
            {
                foreach (var (FileName, EntityName, ProcessFunc, isProvinsi) in _processConfigs)
                {
                    Logger.LogInfo($"Getting data {EntityName.ToLower()}.");
                    await ProcessRegionalData(
                        FileName,
                        EntityName,
                        ProcessFunc,
                        cts.Token,
                        isProvinsi
                    );
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
                _fileSemaphore.Dispose();
                Logger.LogInfo($"Application finished. Total time: {sw.Elapsed.TotalSeconds:F2} seconds");
                Logger.LogInfo($"Here is the summary data:");
                AnsiConsole.Write(SummaryTable(_totalAllData));
            }
        }

        private static async Task ProcessRegionalData(
            string fileName,
            string entityName,
            Func<string, Task<string>> processFunction,
            CancellationToken cancellationToken,
            bool isProvinsi = false)
        {
            try
            {
                if (isProvinsi)
                {
                    await ProcessProvinsiData(entityName, processFunction, cancellationToken);
                    return;
                }

                var records = await ReadRegionalRecords(fileName, entityName, cancellationToken);
                var lastCsvPath = await ProcessBatchedRecords(records, entityName, processFunction, cancellationToken);
                await UpdateFinalCount(entityName, lastCsvPath, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in Process{entityName}Data: {ex.Message}");
                throw;
            }
        }

        private static async Task ProcessProvinsiData(
            string entityName,
            Func<string, Task<string>> processFunction,
            CancellationToken cancellationToken)
        {
            string csvFilePath = await WithRetry(() => processFunction(""));
            await _fileSemaphore.WaitAsync(cancellationToken);
            try
            {
                var provinsiRecords = await CsvConverter.ReadBufferedAsync(csvFilePath);
                _totalAllData.Add(new KeyValuePair<string, int>(entityName, provinsiRecords.Count()));
                Logger.LogInfo($"✅ Completed {entityName} processing. Total processed: {provinsiRecords.Count:N0} records");
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }

        private static async Task<List<RegionalRecord>> ReadRegionalRecords(
            string fileName,
            string entityName,
            CancellationToken cancellationToken)
        {
            return await AnsiConsole.Status()
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
        }

        private static async Task<string> ProcessBatchedRecords(
            List<RegionalRecord> records,
            string entityName,
            Func<string, Task<string>> processFunction,
            CancellationToken cancellationToken)
        {
            var totalCount = records.Count;
            var lastCsvPath = string.Empty;
            var csvPathLock = new object();

            await AnsiConsole.Progress()
                .AutoClear(true)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                )
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
                                        string csvPath = await processFunction(record.kode!);
                                        lock (csvPathLock)
                                        {
                                            lastCsvPath = csvPath;
                                        }
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

            return lastCsvPath;
        }

        private static async Task UpdateFinalCount(
            string entityName,
            string lastCsvPath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(lastCsvPath))
            {
                Logger.LogWarning($"No CSV path found for {entityName}");
                return;
            }

            await _fileSemaphore.WaitAsync(cancellationToken);
            try
            {
                var appendedRecords = await CsvConverter.ReadBufferedAsync(lastCsvPath);
                var totalProcessed = appendedRecords.Count;
                _totalAllData.Add(new KeyValuePair<string, int>(entityName, totalProcessed));
                Logger.LogInfo($"✅ Completed {entityName}. Total data: {totalProcessed:N0} records");
            }
            finally
            {
                _fileSemaphore.Release();
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

        private static Table SummaryTable(List<KeyValuePair<string, int>> totalData)
        {
            var table = new Table().Border(TableBorder.Ascii);

            if (totalData.Count != 0)
            {
                Markup[] rowsTable = new Markup[totalData.Count];

                for (int i = 0; i < totalData.Count; i++)
                {
                    var record = totalData[i];
                    table.AddColumn(new TableColumn(record.Key).Centered());
                    rowsTable[i] = new Markup($"[green]{record.Value}[/]");
                }

                table.AddRow(rowsTable);
            }

            return table;
        }
    }
}