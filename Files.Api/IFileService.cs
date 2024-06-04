using Files.Api.Models;

namespace Files.Api
{
    public interface IFileService
    {
        void ChangeItemName(ChangeItemNameModel changeItemNameModel);
        void CopyItem(CopyItemModel copyItemModel);
        //void CreateFileLinks(IEnumerable<FileLink> fileLinks);
        void CreateFolder(CreateFolderModel createFolderModel);
        Task CreateURLAsync(CreateURLModel createURLModel);
        void DeleteItem(DeleteItemModel deleteItemModel);
        Task<ContentModel> GetContentAsync(string? path = null, uint page = 1, int pageSize = 20);
        Task<IEnumerable<PathAndStreamFactory>> GetPathsAndStreamFactoriesAsync(IEnumerable<string> paths);
        string GetType(string v);
        void MoveItem(MoveItemModel moveItemModel);
        void ToggleItemHidden(ToggleItemHiddenModel toggleItemHiddenModel);
        Task UploadFileChunkAsync(UploadFileModel uploadFileModel);
    }
}