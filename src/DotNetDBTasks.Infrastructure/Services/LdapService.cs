using DotNetDBTasks.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// LDAP/Active Directory service for user authentication and directory queries.
/// </summary>
public class LdapService : ILdapService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LdapService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly string _baseDn;
    private readonly string _usersDn;
    private readonly string _adminDn;
    private readonly string _adminPassword;

    public LdapService(IConfiguration configuration, ILogger<LdapService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _host = configuration["Ldap:Host"] ?? "localhost";
        _port = int.Parse(configuration["Ldap:Port"] ?? "389");
        _baseDn = configuration["Ldap:BaseDn"] ?? "dc=dotnetdbtasks,dc=local";
        _usersDn = configuration["Ldap:UsersDn"] ?? "ou=users,dc=dotnetdbtasks,dc=local";
        _adminDn = configuration["Ldap:AdminDn"] ?? "cn=admin,dc=dotnetdbtasks,dc=local";
        _adminPassword = configuration["Ldap:AdminPassword"] ?? "";
    }

    public Task<LdapUserInfo?> AuthenticateAsync(string username, string password)
    {
        try
        {
            using var connection = new LdapConnection();
            connection.Connect(_host, _port);

            // First find the user DN using admin bind
            connection.Bind(_adminDn, _adminPassword);

            var searchResults = connection.Search(
                _usersDn,
                LdapConnection.ScopeSub,
                $"(uid={EscapeLdapFilter(username)})",
                new[] { "dn", "uid", "cn", "sn", "givenName", "mail", "departmentNumber" },
                false);

            if (!searchResults.HasMore())
                return Task.FromResult<LdapUserInfo?>(null);

            var entry = searchResults.Next();
            var userDn = entry.Dn;

            // Now try to bind as the user to verify password
            using var userConnection = new LdapConnection();
            userConnection.Connect(_host, _port);
            userConnection.Bind(userDn, password);

            return Task.FromResult<LdapUserInfo?>(MapEntry(entry));
        }
        catch (LdapException ex)
        {
            _logger.LogDebug(ex, "LDAP authentication failed for user {Username}", username);
            return Task.FromResult<LdapUserInfo?>(null);
        }
    }

    public Task<IReadOnlyList<LdapUserInfo>> SearchUsersAsync(string searchTerm)
    {
        var results = new List<LdapUserInfo>();

        try
        {
            using var connection = new LdapConnection();
            connection.Connect(_host, _port);
            connection.Bind(_adminDn, _adminPassword);

            var escaped = EscapeLdapFilter(searchTerm);
            var filter = $"(|(uid=*{escaped}*)(cn=*{escaped}*)(mail=*{escaped}*)(givenName=*{escaped}*)(sn=*{escaped}*))";

            var searchResults = connection.Search(
                _usersDn,
                LdapConnection.ScopeSub,
                filter,
                new[] { "uid", "cn", "sn", "givenName", "mail", "departmentNumber" },
                false);

            while (searchResults.HasMore())
            {
                try
                {
                    var entry = searchResults.Next();
                    var user = MapEntry(entry);
                    if (user != null)
                        results.Add(user);
                }
                catch (LdapReferralException)
                {
                    // Skip referrals
                }
            }
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP search failed for term {SearchTerm}", searchTerm);
        }

        return Task.FromResult<IReadOnlyList<LdapUserInfo>>(results);
    }

    public Task<IReadOnlyList<string>> GetDepartmentsAsync()
    {
        var departments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = new LdapConnection();
            connection.Connect(_host, _port);
            connection.Bind(_adminDn, _adminPassword);

            var searchResults = connection.Search(
                _usersDn,
                LdapConnection.ScopeSub,
                "(departmentNumber=*)",
                new[] { "departmentNumber" },
                false);

            while (searchResults.HasMore())
            {
                try
                {
                    var entry = searchResults.Next();
                    var dept = GetAttribute(entry, "departmentNumber");
                    if (!string.IsNullOrWhiteSpace(dept))
                        departments.Add(dept);
                }
                catch (LdapReferralException)
                {
                    // Skip referrals
                }
            }
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Failed to retrieve LDAP departments");
        }

        return Task.FromResult<IReadOnlyList<string>>(departments.OrderBy(d => d).ToList());
    }

    public Task<IReadOnlyList<LdapUserInfo>> GetUsersByDepartmentAsync(string department)
    {
        var results = new List<LdapUserInfo>();

        try
        {
            using var connection = new LdapConnection();
            connection.Connect(_host, _port);
            connection.Bind(_adminDn, _adminPassword);

            var filter = $"(departmentNumber={EscapeLdapFilter(department)})";

            var searchResults = connection.Search(
                _usersDn,
                LdapConnection.ScopeSub,
                filter,
                new[] { "uid", "cn", "sn", "givenName", "mail", "departmentNumber" },
                false);

            while (searchResults.HasMore())
            {
                try
                {
                    var entry = searchResults.Next();
                    var user = MapEntry(entry);
                    if (user != null)
                        results.Add(user);
                }
                catch (LdapReferralException)
                {
                    // Skip referrals
                }
            }
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP department search failed for {Department}", department);
        }

        return Task.FromResult<IReadOnlyList<LdapUserInfo>>(results);
    }

    private static LdapUserInfo? MapEntry(LdapEntry entry)
    {
        var uid = GetAttribute(entry, "uid");
        if (string.IsNullOrWhiteSpace(uid))
            return null;

        return new LdapUserInfo
        {
            Username = uid,
            Email = GetAttribute(entry, "mail") ?? $"{uid}@dotnetdbtasks.local",
            FirstName = GetAttribute(entry, "givenName") ?? "",
            LastName = GetAttribute(entry, "sn") ?? "",
            Department = GetAttribute(entry, "departmentNumber")
        };
    }

    private static string? GetAttribute(LdapEntry entry, string name)
    {
        try
        {
            return entry.GetAttribute(name)?.StringValue;
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeLdapFilter(string input)
    {
        return input
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
