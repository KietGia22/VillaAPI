using CloudinaryDotNet.Actions;
using CloudinaryDotNet;
using Microsoft.Extensions.Options;
using VillaAPI.Config;
using VillaAPI.Repository.IRepository;
using VillaAPI.Models;
using VillaAPI.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace VillaAPI.Repository
{
    public class PhotoRepositroy :IPhotoService
    {
        private readonly Cloudinary _cloudinary;
        private readonly ApplicationDbContext _db;
        internal DbSet<Photo> dbSet;
        public PhotoRepositroy(IOptions<CloudinaryConfig> config, ApplicationDbContext db)
        {
            var newAcc = new Account(
                    config.Value.CloudName,
                    config.Value.ApiKey,
                    config.Value.ApiSecret
                );
            _cloudinary = new Cloudinary(newAcc);
            _db = db;
        }

        public async Task<ImageUploadResult> AddPhotoAsync(IFormFile file)
        {
            var uploadResult = new ImageUploadResult();

            if (file.Length > 0)
            {
                using var stream = file.OpenReadStream();
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Transformation = new Transformation().Height(500).Width(500).Crop("fill").Gravity("face")
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }
            return uploadResult;
        }

        public async Task CreateAsync(Photo photo)
        {
            await _db.AddAsync(photo);
            await SaveAsync();
        }
        public async Task SaveAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}
