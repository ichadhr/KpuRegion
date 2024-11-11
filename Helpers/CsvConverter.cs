using System.Text;
using kpu.Models;

namespace kpu.Helpers
{
    public class CsvConverter
    {
        private static readonly SemaphoreSlim _fileLock = new(1, 1);

        public static async Task<string> SaveToCsvAsync(List<ObjectJson> objects, string filename, CsvOptions options)
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

        public static IEnumerable<ObjectJson> ReadCsv(string filename, int bufferSize)
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
                    yield return new ObjectJson
                    {
                        kode = parts[0].Trim('"'),
                        nama = parts[1].Trim('"')
                    };
                }
            }
        }
    }
}