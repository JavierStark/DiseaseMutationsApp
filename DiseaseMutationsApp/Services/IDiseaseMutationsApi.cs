using Refit;

namespace DiseaseMutationsApp.Services;

public interface IDiseaseMutationsApi
{
    [Get("/getbestrna")]
    Task<List<Tuple<string,double,int,string>>> GetBestgRNA([Query] int window, [Query] string sequence);
}