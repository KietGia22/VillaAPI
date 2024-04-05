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

        public async Task<TokenDTO> Login(LoginRequestDTO loginRequestDTO)
        {
            //throw new NotImplementedException();
            var user = _db.ApplicationUsers.FirstOrDefault(u => u.UserName.ToLower() == loginRequestDTO.UserName.ToLower());
            bool isValid = await _userManager.CheckPasswordAsync(user, loginRequestDTO.Password);
            if(user == null || isValid == false)
            {
                return new TokenDTO()
                {
                    AccessToken = "",
                    //User = null
                };

            }
            var jwtTokenId = $"JTI{Guid.NewGuid()}";
            var accesstoken = await GetAccessToken(user, jwtTokenId);
            var refreshToken = await CreateNewRefreshToken(user.Id, jwtTokenId);
            TokenDTO TokenDTO = new TokenDTO
            {
                AccessToken = accesstoken,
                //User = _mapper.Map<UserDTO>(user),
                RefreshToken = refreshToken
            };
            return TokenDTO;
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


        private async Task<string> GetAccessToken(ApplicationUser user, string jwtTokenId)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secretKey);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, user.UserName.ToString()),
                    new Claim(ClaimTypes.Role, roles.FirstOrDefault()),
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                    new Claim(JwtRegisteredClaimNames.Jti, jwtTokenId)
                }),
                Expires = DateTime.UtcNow.AddMinutes(1),
                SigningCredentials = new(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenStr = tokenHandler.WriteToken(token);
            return tokenStr;
        }

        public async Task<TokenDTO> RefreshAccessToken(TokenDTO tokenDTO)
        {
            // Find an existing refresh token
            var existingRefreshToken = await _db.refreshTokens.FirstOrDefaultAsync(u => u.Refresh_Token == tokenDTO.RefreshToken);
            if(existingRefreshToken == null)
            {
                return new TokenDTO();
            }

            // Compare data from existing refresh and access token provided and if there is any missmatch then consider it as a fraud
            var isTokenValid = GetAccessTokenData(tokenDTO.AccessToken, existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
            if(!isTokenValid)
            {
                await MarkTokenAsInvalid(existingRefreshToken);
                return new TokenDTO();
            }
            // When someone tries to use not valid  token, fraud possible
            if (!existingRefreshToken.IsValid)
            {
                await MarkAllTokenInChainAsInvalid(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
            }
            // If just expired then mark as invalid and return empty
            if (existingRefreshToken.ExpiresAt < DateTime.UtcNow)
            {
                await MarkTokenAsInvalid(existingRefreshToken);
                return new TokenDTO();
            }
            // replace old refresh with a new one with updated expire date
            var newRefreshToken = await CreateNewRefreshToken(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);

            // revoke existing refresh token
            await MarkTokenAsInvalid(existingRefreshToken);
            // generate new access token
            var applicationUser = _db.ApplicationUsers.FirstOrDefault(u => u.Id == existingRefreshToken.UserId);
            if (applicationUser == null)
            {
                return new TokenDTO();
            }
            var newAccessToken = await GetAccessToken(applicationUser, existingRefreshToken.JwtTokenId);
            return new TokenDTO()
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            };
        }

        private async Task<string> CreateNewRefreshToken(string userId, string tokenId)
        {
            RefreshToken refreshToken = new()
            {
                IsValid = true,
                UserId = userId,
                JwtTokenId = tokenId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                Refresh_Token = Guid.NewGuid() + "-" + Guid.NewGuid(),
            };

            await _db.refreshTokens.AddAsync(refreshToken);
            await _db.SaveChangesAsync();
            return refreshToken.Refresh_Token;
        }

        private bool GetAccessTokenData(string accessToken, string expectedUserId, string expectedTokenId)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwt = tokenHandler.ReadJwtToken(accessToken);
                var jwtTokenId = jwt.Claims.FirstOrDefault(u => u.Type == JwtRegisteredClaimNames.Jti).Value;
                var userId = jwt.Claims.FirstOrDefault(u => u.Type == JwtRegisteredClaimNames.Sub).Value;
                //return (true, userId, jwtTokenId);
                return userId==expectedUserId && jwtTokenId==expectedTokenId;
            }
            catch
            {
                //return (false, null, null);
                return false;
            }
        }

        private async Task MarkAllTokenInChainAsInvalid(string userId, string tokenId)
        {
            await _db.refreshTokens.Where(u => u.UserId == userId && u.JwtTokenId == tokenId)
                    .ExecuteUpdateAsync(u => u.SetProperty(refreshToken => refreshToken.IsValid, false));
        }

        private Task MarkTokenAsInvalid(RefreshToken refreshToken)
        {
            refreshToken.IsValid = false;
            return _db.SaveChangesAsync();
        }

        public async Task RevokeRefreshToken(TokenDTO tokenDTO)
        {
            var existingRefreshToken = await _db.refreshTokens.FirstOrDefaultAsync(_ => _.Refresh_Token == tokenDTO.RefreshToken);

            if (existingRefreshToken == null)
                return;

            // Compare data from existing refresh and access token provided and
            // if there is any missmatch then we should do nothing with refresh token

            var isTokenValid = GetAccessTokenData(tokenDTO.AccessToken, existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);
            if (!isTokenValid)
            {

                return;
            }

            await MarkAllTokenInChainAsInvalid(existingRefreshToken.UserId, existingRefreshToken.JwtTokenId);

        }
    }
}
