using DotNetDBTasks.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// LDAP / Microsoft Active Directory service for user authentication and directory queries.
/// Attribute names are configurable to support both real AD (production) and OpenLDAP (dev).
///
/// Production AD defaults: sAMAccountName, department, (&amp;(objectClass=user)(objectCategory=person))
/// Dev OpenLDAP overrides via env: uid, department, (objectClass=inetOrgPerson)
/// </summary>
public class LdapService : ILdapService
{
    private readonly ILogger<LdapService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly string _usersDn;
    private readonly string _adminDn;
    private readonly string _adminPassword;
    private readonly string _usernameAttr;
    private readonly string _deptAttr;
    private readonly string _userObjectFilter;

    public LdapService(IConfiguration configuration, ILogger<LdapService> logger)
    {
        _logger = logger;
        _host = configuration["Ldap:Host"] ?? "localhost";
        _port = int.Parse(configuration["Ldap:Port"] ?? "389");
        _usersDn = configuration["Ldap:UsersDn"] ?? "ou=users,dc=dotnetdbtasks,dc=local";
        _adminDn = configuration["Ldap:AdminDn"] ?? "cn=admin,dc=dotnetdbtasks,dc=local";
        _adminPassword = configuration["Ldap:AdminPassword"] ?? "";

        // Configurable attribute names — defaults target Microsoft Active Directory
        _usernameAttr = configuration["Ldap:UsernameAttribute"] ?? "sAMAccountName";
        _deptAttr = configuration["Ldap:DepartmentAttribute"] ?? "department";
        _userObjectFilter = configuration["Ldap:UserObjectFilter"]
            ?? "(&(objectClass=user)(objectCategory=person))";
    }

    private string[] UserAttributes => new[]
    {
        "dn", _usernameAttr, "cn", "sn", "givenName", "mail", _deptAttr
    };

    public Task<LdapUserInfo?> AuthenticateAsync(string username, string password)
    {
        try
        {
            using var connection = new LdapConnection();
            connection.Connect(_host, _port);

            // First find the user DN using admin bind
            connection.Bind(_adminDn, _adminPassword);

            var filter = $"(&{_userObjectFilter}({_usernameAttr}={EscapeLdapFilter(username)}))";
            var searchResults = connection.Search(
                _usersDn, LdapConnection.ScopeSub, filter, UserAttributes, false);

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

    public Task<LdapUserInfo?> GetUserByUsernameAsync(string username)
    {
        try
        {
            using var connection = new LdapConnection();
            connection.Connect(_host, _port);
            connection.Bind(_adminDn, _adminPassword);

            var filter = $"(&{_userObjectFilter}({_usernameAttr}={EscapeLdapFilter(username)}))";
            var searchResults = connection.Search(
                _usersDn, LdapConnection.ScopeSub, filter, UserAttributes, false);

            if (!searchResults.HasMore())
                return Task.FromResult<LdapUserInfo?>(null);

            return Task.FromResult(MapEntry(searchResults.Next()));
        }
        catch (LdapException ex)
        {
            _logger.LogDebug(ex, "LDAP lookup failed for user {Username}", username);
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
            var filter = $"(&{_userObjectFilter}(|({_usernameAttr}=*{escaped}*)(cn=*{escaped}*)(mail=*{escaped}*)(givenName=*{escaped}*)(sn=*{escaped}*)))";

            var searchResults = connection.Search(
                _usersDn, LdapConnection.ScopeSub, filter, UserAttributes, false);

            while (searchResults.HasMore())
            {
                try
                {
                    var entry = searchResults.Next();
                    var user = MapEntry(entry);
                    if (user != null)
                        results.Add(user);
                }
                catch (LdapReferralException) { }
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

            var filter = $"(&{_userObjectFilter}({_deptAttr}=*))";
            var searchResults = connection.Search(
                _usersDn, LdapConnection.ScopeSub, filter, new[] { _deptAttr }, false);

            while (searchResults.HasMore())
            {
                try
                {
                    var entry = searchResults.Next();
                    var dept = GetAttribute(entry, _deptAttr);
                    if (!string.IsNullOrWhiteSpace(dept))
                        departments.Add(dept);
                }
                catch (LdapReferralException) { }
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

            var filter = $"(&{_userObjectFilter}({_deptAttr}={EscapeLdapFilter(department)}))";
            var searchResults = connection.Search(
                _usersDn, LdapConnection.ScopeSub, filter, UserAttributes, false);

            while (searchResults.HasMore())
            {
                try
                {
                    var entry = searchResults.Next();
                    var user = MapEntry(entry);
                    if (user != null)
                        results.Add(user);
                }
                catch (LdapReferralException) { }
            }
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP department search failed for {Department}", department);
        }

        return Task.FromResult<IReadOnlyList<LdapUserInfo>>(results);
    }

    private LdapUserInfo? MapEntry(LdapEntry entry)
    {
        var username = GetAttribute(entry, _usernameAttr);
        if (string.IsNullOrWhiteSpace(username))
            return null;

        return new LdapUserInfo
        {
            Username = username,
            Email = GetAttribute(entry, "mail") ?? $"{username}@dotnetdbtasks.local",
            FirstName = GetAttribute(entry, "givenName") ?? "",
            LastName = GetAttribute(entry, "sn") ?? "",
            Department = GetAttribute(entry, _deptAttr)
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
