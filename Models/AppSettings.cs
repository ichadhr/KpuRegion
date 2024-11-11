namespace KpuRegion.Models
{
    public static class AppSettings
    {
        public static class Endpoints
        {
            public const string URI_JSON_KPU = "https://sirekap-obj-data.kpu.go.id/wilayah/pemilu/ppwp/";
        }

        public static class FileNames
        {
            public const string FILENAME_PROVINSI = "provinsi.csv";
            public const string FILENAME_KABKOT = "kabupaten_kota.csv";
            public const string FILENAME_KECAMATAN = "kecamatan.csv";
            public const string FILENAME_KELURAHAN = "kelurahan.csv";
        }

        public static class Configuration
        {
            public const int BUFFER_SIZE = 8192;
            public const int MAX_CONCURRENT_TASKS = 3;
        }
    }
}