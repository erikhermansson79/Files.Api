using System.Threading.Tasks;

namespace Files.Api
{
    public interface IThumbnailService
    {
        /// <summary>
        /// Generates a thumbnail for the given file path and returns a data URI (e.g. "data:image/png;base64,...") or null if no thumbnail can be produced.
        /// </summary>
        Task<string?> GetThumbnailDataUriAsync(string path, int size = 128);
    }
}
