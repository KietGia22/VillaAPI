using AutoMapper;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using VillaAPI.Data;
using VillaAPI.Models;
using VillaAPI.Models.DTO;
using VillaAPI.Repository.IRepository;

namespace VillaAPI.Repository
{
    public class UserRepository :IUserRepository
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManage;
        private string secretKey;
        private readonly IMapper _mapper;
        public UserRepository(ApplicationDbContext db, IConfiguration configuration,
                                UserManager<ApplicationUser> userManager, IMapper mapper,
                                RoleManager<IdentityRole> roleManage)
        {
            _db = db;
            _mapper = mapper;
            _userManager = userManager;
            secretKey = configuration.GetValue<string>("ApiSettings:Secret");
            _roleManage = roleManage;

        }
        public bool IsUniqueUser(string username)
        {
            //throw new NotImplementedException();
            var user = _db.ApplicationUsers.FirstOrDefault(x => x.UserName == username);
            if(user == null)
            {
                return true;
            }
            return false;
        }

        public async Task<LoginResponseDTO> Login(LoginRequestDTO loginRequestDTO)
        {
            //throw new NotImplementedException();
            var user = _db.ApplicationUsers.FirstOrDefault(u => u.UserName.ToLower() == loginRequestDTO.UserName.ToLower());
            bool isValid = await _userManager.CheckPasswordAsync(user, loginRequestDTO.Password);
            if(user == null || isValid == false)
            {
                return new LoginResponseDTO()
                {
                    Token = "",
                    User = null
                };

            }

            var roles = await _userManager.GetRolesAsync(user);
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secretKey);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, user.UserName.ToString()),
                    new Claim(ClaimTypes.Role, roles.FirstOrDefault()),
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            LoginResponseDTO loginResponseDTO = new LoginResponseDTO
            {
                Token = tokenHandler.WriteToken(token),
                User = _mapper.Map<UserDTO>(user),
            };
            return loginResponseDTO;
        } 

        public async Task<UserDTO> Register(RegisterationRequestDTO registerationRequestDTO)
        {
            //throw new NotImplementedException();
            ApplicationUser user = new()
            {
                UserName = registerationRequestDTO.UserName,
                //Password = registerationRequestDTO.Password,
                //Name = registerationRequestDTO.Name,
                //Role = registerationRequestDTO.Role
                Email = registerationRequestDTO.UserName,
                NormalizedEmail = registerationRequestDTO.UserName.ToUpper(),
                Name = registerationRequestDTO.Name

            };
            //_db.LocalUsers.Add(user);
            //await _db.SaveChangesAsync();
            //return user;
            try
            {
                var result = await _userManager.CreateAsync(user, registerationRequestDTO.Password);
                if(result.Succeeded)
                {
                    if(!_roleManage.RoleExistsAsync("admin").GetAwaiter().GetResult())
                    {
                        await _roleManage.CreateAsync(new IdentityRole("admin"));
                        await _roleManage.CreateAsync(new IdentityRole("customer"));
                    }
                    var IsFirstUser = await _db.ApplicationUsers.CountAsync();
                    if(IsFirstUser > 1)
                    {
                        Console.WriteLine(IsFirstUser);
                        await _userManager.AddToRoleAsync(user, "customer");
                    }
                    else
                    {
                        await _userManager.AddToRoleAsync(user, "admin");
                    }
                    var userToReturn = _db.ApplicationUsers.FirstOrDefault(
                            u => u.UserName == registerationRequestDTO.UserName
                        );
                    return _mapper.Map<UserDTO>(userToReturn);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return new UserDTO();
        }
    }
}
