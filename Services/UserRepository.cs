using GridPocBlazor.Models;
using Microsoft.Data.SqlClient;

namespace GridPocBlazor.Services;

public class UserRepository
{
    private readonly string _connectionString;
    private readonly string _databaseName;
    private bool _initialized;

    public UserRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        var builder = new SqlConnectionStringBuilder(_connectionString);
        _databaseName = string.IsNullOrWhiteSpace(builder.InitialCatalog)
            ? "GridPocBlazorDb"
            : builder.InitialCatalog;

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            builder.InitialCatalog = _databaseName;
            _connectionString = builder.ConnectionString;
        }
    }

    public async Task EnsureDatabaseAsync()
    {
        if (_initialized)
        {
            return;
        }

        var masterBuilder = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master"
        };

        await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await connection.OpenAsync();

            var createDatabaseSql = $"""
                IF DB_ID(@DatabaseName) IS NULL
                BEGIN
                    EXEC('CREATE DATABASE [{_databaseName}]')
                END
                """;

            await using var createDatabaseCommand = new SqlCommand(createDatabaseSql, connection);
            createDatabaseCommand.Parameters.AddWithValue("@DatabaseName", _databaseName);
            await createDatabaseCommand.ExecuteNonQueryAsync();
        }

        await using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            const string createTableSql = """
                IF OBJECT_ID('dbo.Users', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Users
                    (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        FirstName NVARCHAR(100) NOT NULL,
                        LastName NVARCHAR(100) NOT NULL,
                        ProfileImageUrl NVARCHAR(500) NULL,
                        Email NVARCHAR(200) NOT NULL UNIQUE,
                        Phone NVARCHAR(50) NOT NULL,
                        Gender NVARCHAR(20) NULL,
                        Address NVARCHAR(300) NULL,
                        City NVARCHAR(100) NULL,
                        State NVARCHAR(100) NULL,
                        Country NVARCHAR(100) NULL,
                        ZipCode NVARCHAR(20) NULL,
                        CreatedDate DATETIME2 NOT NULL,
                        LastLoginDate DATETIME2 NULL,
                        IsLoggedIn BIT NOT NULL CONSTRAINT DF_Users_IsLoggedIn DEFAULT 0,
                        PasswordHash NVARCHAR(400) NOT NULL,
                        PasswordSalt NVARCHAR(400) NOT NULL
                    );
                END
                """;

            await using var createTableCommand = new SqlCommand(createTableSql, connection);
            await createTableCommand.ExecuteNonQueryAsync();
        }

        _initialized = true;
    }

    public async Task<List<User>> GetUsersAsync()
    {
        await EnsureDatabaseAsync();

        const string sql = """
            SELECT Id, FirstName, LastName, ProfileImageUrl, Email, Phone, Gender, Address,
                   City, State, Country, ZipCode, CreatedDate, LastLoginDate, IsLoggedIn,
                   PasswordHash, PasswordSalt
            FROM dbo.Users
            ORDER BY Id;
            """;

        var users = new List<User>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public async Task<User> CreateUserAsync(User user)
    {
        await EnsureDatabaseAsync();

        NormalizeUser(user);

        var validationMessage = UserInputRules.ValidateUser(user, true);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            throw new InvalidOperationException(validationMessage);
        }

        if (await EmailExistsAsync(user.Email))
        {
            throw new InvalidOperationException("A user with this email already exists.");
        }

        var (hash, salt) = PasswordHasher.HashPassword(user.Password);

        const string sql = """
            INSERT INTO dbo.Users
            (
                FirstName, LastName, ProfileImageUrl, Email, Phone, Gender, Address,
                City, State, Country, ZipCode, CreatedDate, LastLoginDate, IsLoggedIn,
                PasswordHash, PasswordSalt
            )
            OUTPUT INSERTED.Id
            VALUES
            (
                @FirstName, @LastName, @ProfileImageUrl, @Email, @Phone, @Gender, @Address,
                @City, @State, @Country, @ZipCode, @CreatedDate, NULL, 0, @PasswordHash, @PasswordSalt
            );
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddCommonParameters(command, user);
        command.Parameters.AddWithValue("@PasswordHash", hash);
        command.Parameters.AddWithValue("@PasswordSalt", salt);

        user.Id = Convert.ToInt32(await command.ExecuteScalarAsync());
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.Password = string.Empty;
        user.ConfirmPassword = string.Empty;
        return user;
    }

    public async Task UpdateUserAsync(User user)
    {
        await EnsureDatabaseAsync();

        NormalizeUser(user);

        var validationMessage = UserInputRules.ValidateUser(user, false);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            throw new InvalidOperationException(validationMessage);
        }

        var updatePassword = !string.IsNullOrWhiteSpace(user.Password);
        if (updatePassword)
        {
            var passwordDetails = PasswordHasher.HashPassword(user.Password);
            user.PasswordHash = passwordDetails.Hash;
            user.PasswordSalt = passwordDetails.Salt;
        }

        var sql = updatePassword
            ? """
                UPDATE dbo.Users
                SET FirstName = @FirstName,
                    LastName = @LastName,
                    ProfileImageUrl = @ProfileImageUrl,
                    Email = @Email,
                    Phone = @Phone,
                    Gender = @Gender,
                    Address = @Address,
                    City = @City,
                    State = @State,
                    Country = @Country,
                    ZipCode = @ZipCode,
                    CreatedDate = @CreatedDate,
                    PasswordHash = @PasswordHash,
                    PasswordSalt = @PasswordSalt
                WHERE Id = @Id;
                """
            : """
                UPDATE dbo.Users
                SET FirstName = @FirstName,
                    LastName = @LastName,
                    ProfileImageUrl = @ProfileImageUrl,
                    Email = @Email,
                    Phone = @Phone,
                    Gender = @Gender,
                    Address = @Address,
                    City = @City,
                    State = @State,
                    Country = @Country,
                    ZipCode = @ZipCode,
                    CreatedDate = @CreatedDate
                WHERE Id = @Id;
                """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddCommonParameters(command, user);
        command.Parameters.AddWithValue("@Id", user.Id);

        if (updatePassword)
        {
            command.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
            command.Parameters.AddWithValue("@PasswordSalt", user.PasswordSalt);
        }

        await command.ExecuteNonQueryAsync();
        user.Password = string.Empty;
        user.ConfirmPassword = string.Empty;
    }

    public async Task DeleteUserAsync(int id)
    {
        await EnsureDatabaseAsync();

        const string sql = "DELETE FROM dbo.Users WHERE Id = @Id;";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteUsersAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await EnsureDatabaseAsync();

        var parameterNames = idList.Select((_, index) => $"@Id{index}").ToArray();
        var sql = $"DELETE FROM dbo.Users WHERE Id IN ({string.Join(", ", parameterNames)});";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);

        for (var index = 0; index < idList.Count; index++)
        {
            command.Parameters.AddWithValue(parameterNames[index], idList[index]);
        }

        await command.ExecuteNonQueryAsync();
    }

    public async Task<User?> AuthenticateAsync(string email, string password)
    {
        await EnsureDatabaseAsync();

        email = UserInputRules.NormalizeEmail(email);
        password = password.Trim();

        var emailValidationMessage = UserInputRules.ValidateEmail(email);
        if (!string.IsNullOrWhiteSpace(emailValidationMessage))
        {
            throw new InvalidOperationException(emailValidationMessage);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Password is required.");
        }

        const string sql = """
            SELECT Id, FirstName, LastName, ProfileImageUrl, Email, Phone, Gender, Address,
                   City, State, Country, ZipCode, CreatedDate, LastLoginDate, IsLoggedIn,
                   PasswordHash, PasswordSalt
            FROM dbo.Users
            WHERE Email = @Email;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Email", email);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        var user = MapUser(reader);
        if (!PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
        {
            return null;
        }

        await reader.CloseAsync();
        user.IsLoggedIn = true;
        user.LastLoginDate = DateTime.Now;
        await SetLoginStatusAsync(user.Id, true, user.LastLoginDate);

        user.Password = string.Empty;
        user.ConfirmPassword = string.Empty;
        return user;
    }

    public async Task SetLoginStatusAsync(int id, bool isLoggedIn, DateTime? lastLoginDate = null)
    {
        await EnsureDatabaseAsync();

        const string sql = """
            UPDATE dbo.Users
            SET IsLoggedIn = @IsLoggedIn,
                LastLoginDate = COALESCE(@LastLoginDate, LastLoginDate)
            WHERE Id = @Id;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@IsLoggedIn", isLoggedIn);
        command.Parameters.AddWithValue("@LastLoginDate", (object?)lastLoginDate ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> EmailExistsAsync(string email)
    {
        const string sql = "SELECT COUNT(1) FROM dbo.Users WHERE Email = @Email;";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Email", email);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static void NormalizeUser(User user)
    {
        user.FirstName = UserInputRules.LettersAndSeparatorsOnly(user.FirstName);
        user.LastName = UserInputRules.LettersAndSeparatorsOnly(user.LastName);
        user.Email = UserInputRules.NormalizeEmail(user.Email);
        user.Phone = UserInputRules.DigitsOnly(user.Phone, 15);
        user.City = UserInputRules.LettersAndSeparatorsOnly(user.City);
        user.State = UserInputRules.LettersAndSeparatorsOnly(user.State);
        user.Country = UserInputRules.LettersAndSeparatorsOnly(user.Country);
        user.ZipCode = UserInputRules.DigitsOnly(user.ZipCode, 10);
        user.Address = UserInputRules.TrimText(user.Address, 300);
        user.Password = user.Password.Trim();
        user.ConfirmPassword = user.ConfirmPassword.Trim();
    }

    private static void AddCommonParameters(SqlCommand command, User user)
    {
        command.Parameters.AddWithValue("@FirstName", user.FirstName);
        command.Parameters.AddWithValue("@LastName", user.LastName);
        command.Parameters.AddWithValue("@ProfileImageUrl", (object?)user.ProfileImageUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@Email", user.Email);
        command.Parameters.AddWithValue("@Phone", user.Phone);
        command.Parameters.AddWithValue("@Gender", (object?)user.Gender ?? DBNull.Value);
        command.Parameters.AddWithValue("@Address", (object?)user.Address ?? DBNull.Value);
        command.Parameters.AddWithValue("@City", (object?)user.City ?? DBNull.Value);
        command.Parameters.AddWithValue("@State", (object?)user.State ?? DBNull.Value);
        command.Parameters.AddWithValue("@Country", (object?)user.Country ?? DBNull.Value);
        command.Parameters.AddWithValue("@ZipCode", (object?)user.ZipCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedDate", user.CreatedDate);
    }

    private static User MapUser(SqlDataReader reader)
    {
        return new User
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            FirstName = reader.GetString(reader.GetOrdinal("FirstName")),
            LastName = reader.GetString(reader.GetOrdinal("LastName")),
            ProfileImageUrl = reader["ProfileImageUrl"] as string ?? string.Empty,
            Email = reader.GetString(reader.GetOrdinal("Email")),
            Phone = reader.GetString(reader.GetOrdinal("Phone")),
            Gender = reader["Gender"] as string ?? string.Empty,
            Address = reader["Address"] as string ?? string.Empty,
            City = reader["City"] as string ?? string.Empty,
            State = reader["State"] as string ?? string.Empty,
            Country = reader["Country"] as string ?? string.Empty,
            ZipCode = reader["ZipCode"] as string ?? string.Empty,
            CreatedDate = reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
            LastLoginDate = reader["LastLoginDate"] as DateTime?,
            IsLoggedIn = reader.GetBoolean(reader.GetOrdinal("IsLoggedIn")),
            PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
            PasswordSalt = reader.GetString(reader.GetOrdinal("PasswordSalt"))
        };
    }
}
