using CloudinaryDotNet.Actions;
using System.Linq.Expressions;
using VillaAPI.Models;

namespace VillaAPI.Repository.IRepository
{
    public interface IPhotoService
    {
        Task<ImageUploadResult> AddPhotoAsync(IFormFile file);
        Task CreateAsync(Photo photo);
        Task SaveAsync();
    }
}
