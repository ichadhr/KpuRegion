using System.Text;
using KpuRegion.Models;

namespace KpuRegion.Helpers
{
    public class CsvConverter
    {
        private static readonly SemaphoreSlim _fileLock = new(1, 1);

        public static async Task<string> SaveToCsvAsync(List<RegionalRecord> objects, string filename, CsvOptions options)
        {
            await _fileLock.WaitAsync();
            try
            {
                var sb = new StringBuilder(options.InitialBufferSize);

                // Write header only if file doesn't exist and header is requested
                bool fileExists = File.Exists(filename);
                if (!fileExists && options.IncludeHeader)
                {
                    sb.AppendLine("kode,nama");
                }

                foreach (var obj in objects)
                {
                    if (options.IgnoreNullValues && (obj.kode == null || obj.nama == null))
                        continue;

                    string nama = options.CapitalizeExceptRoman ?
                        StringExtensions.CapitalizeExceptRoman(obj.nama ?? "") :
                        obj.nama ?? "";

                    sb.AppendLine($"\"{obj.kode}\",\"{nama}\"");
                }

                // Use FileMode.Append for existing files
                using (var fileStream = new FileStream(filename,
                    fileExists ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                using (var writer = new StreamWriter(fileStream, options.Encoding))
                {
                    await writer.WriteAsync(sb.ToString());
                }

                return filename;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error saving CSV: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public static IEnumerable<RegionalRecord> ReadCsv(string filename, int bufferSize)
        {
            using var reader = new StreamReader(filename, Encoding.UTF8, true, bufferSize);

            // Skip header
            reader.ReadLine();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    yield return new RegionalRecord
                    {
                        kode = parts[0].Trim('"'),
                        nama = parts[1].Trim('"')
                    };
                }
            }
        }

        public static async Task<List<RegionalRecord>> ReadBufferedAsync(string path)
        {
            var records = new List<RegionalRecord>();

            if (!File.Exists(path))
            {
                // Handle the case where the file does not exist
                throw new FileNotFoundException("The specified CSV file was not found.");
            }
            using (var reader = new StreamReader(path))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) // Check for empty or whitespace lines
                    {
                        continue; // Skip processing of empty or whitespace lines
                    }
                    // Process each line to create a record object and add it to the list
                    var record = ConvertLineToRecord(line);
                    records.Add(record);
                }
            }
            return records;
        }

        private static RegionalRecord ConvertLineToRecord(string line)
        {
            // Implement your logic to convert a line into a record object
            var parts = line.Split(','); // Example split logic
            if (parts.Length < 2)
            {
                throw new InvalidDataException("Each line must contain at least two columns.");
            }
            return new RegionalRecord
            {
                kode = parts[0].Trim('"'),
                nama = parts[1].Trim('"')
            };
        }
    }
}