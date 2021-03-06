
namespace Core.AuthRepositoryWrapper;
public class AuthRepository : IAuthRepository {
    private readonly TravelLogContext db;
    public static TimeSpan ExpiryDuration = new TimeSpan(0, 30, 0);

    public AuthRepository(TravelLogContext db) {
        this.db = db;
    }
    public Tuple<bool, User> AuthRegister(User user) {
        var newUser = new User {
            fullname = user.fullname,
            email = user.email,
            isAdmin = false,
            password = BCrypt.Net.BCrypt.HashPassword(user.password),
        };

        db.user.AddRange(newUser);
        db.SaveChanges();

        return Tuple.Create(true, newUser);
    }

    public Tuple<bool, User, string> GetAuth(AuthCredentials credentials) {
        var userByEmail = db.user.FirstOrDefault(u => u.email == credentials.email);
        if (userByEmail == null) {
            return Tuple.Create(false, userByEmail, "NO_USER");
        }

        bool verified = BCrypt.Net.BCrypt.Verify(credentials.password, userByEmail.password);
        if (verified) {
            return Tuple.Create(verified, userByEmail, "");
        }

        return Tuple.Create(false, userByEmail, "PASSWORD_MISSMATCH");
    }

    public async Task<bool> UpdateUser(UserUpdateModel u, string userId, bool isAdmin) {
        var record = db.user.FirstOrDefault(u => u.id == userId);
        bool verified = BCrypt.Net.BCrypt.Verify(u.oldpassword, record.password);
        if (!verified) {
            Console.WriteLine("verified failed");
            return false;
        }

        record.fullname = u.fullname != null ? u.fullname : record.fullname;
        record.email = u.email != null ? u.email : record.email;
        record.langCode = u.langCode != null ? u.langCode : record.langCode;
        if (u.password != null && u.password.Length > 0) {
            record.password = BCrypt.Net.BCrypt.HashPassword(u.password);
        }
        return (await db.SaveChangesAsync()) > 0;
    }

    public User ParseUser(HttpContext http) {
        User currentUser;
        if (http.User.Identity is ClaimsIdentity identity) {
            string userId = identity.FindFirst(ClaimTypes.Name)?.Value;
            currentUser = db.user.FirstOrDefault(u => u.id == userId);
            if (currentUser != null) {
                return currentUser;
            }
        }

        http.Response.StatusCode = 401;
        http.Response.WriteAsJsonAsync(new { message = "TOKEN_PARSE_FAILED" });
        throw new Exception("Token parse failed");
    }
    public string BuildToken(string key, string issuer, User user) {
        var claims = new[] {
            new Claim(ClaimTypes.Name, user.id),
            new Claim(ClaimTypes.NameIdentifier,
            Guid.NewGuid().ToString())
        };

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature);
        var tokenDescriptor = new JwtSecurityToken(issuer, issuer, claims,
            expires: DateTime.Now.Add(AuthRepository.ExpiryDuration), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
    }
}