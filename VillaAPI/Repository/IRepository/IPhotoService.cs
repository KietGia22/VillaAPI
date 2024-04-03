using CloudinaryDotNet.Actions;

namespace VillaAPI.Repository.IRepository
{
    public interface IPhotoService
    {
        Task<ImageUploadResult> AddPhotoAsync(IFormFile file);
    }
}
