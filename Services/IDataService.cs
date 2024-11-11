namespace KpuRegion.Services
{
    public interface IDataService
    {
        Task<string> GetProvinsiData();
        Task<string> GetKabkotData(string kodeProvinsi);
        Task<string> GetKecamatanData(string kodeKabkot);
        Task<string> GetKelurahanData(string kodeKecamatan);
    }
}