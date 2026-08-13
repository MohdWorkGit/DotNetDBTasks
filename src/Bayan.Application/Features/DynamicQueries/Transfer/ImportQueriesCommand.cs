using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Transfer;

/// <summary>
/// Restores queries from an export file. Never modifies or deletes anything that already
/// exists: a name clash is imported as a copy ("Name (imported)") for the admin to reconcile,
/// so a mistaken import is undone by deleting what it added.
/// </summary>
public record ImportQueriesCommand(QueryExportFile File) : IRequest<QueryImportResult>;

public class ImportQueriesCommandHandler : IRequestHandler<ImportQueriesCommand, QueryImportResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public ImportQueriesCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<QueryImportResult> Handle(
        ImportQueriesCommand request,
        CancellationToken cancellationToken)
    {
        var file = request.File;
        if (file is null || file.Queries.Count == 0)
            throw new DomainException("The file contains no queries to import.");
        if (file.FormatVersion > QueryExportFile.CurrentFormatVersion)
            throw new DomainException(
                $"This file was written by a newer version (format {file.FormatVersion}); " +
                $"this system reads up to format {QueryExportFile.CurrentFormatVersion}.");

        var result = new QueryImportResult();

        var existingQueries = await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken);
        var takenNames = existingQueries
            .Select(q => q.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roles = (await _unitOfWork.Roles.GetAllAsync(cancellationToken))
            .ToDictionary(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var users = (await _unitOfWork.Users.GetAllAsync(cancellationToken))
            .ToDictionary(u => u.Username, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var databaseUsers = (await _unitOfWork.DatabaseUsers.GetAllAsync(cancellationToken))
            .ToDictionary(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase);
        var userGroups = (await _unitOfWork.UserGroups.GetAllAsync(cancellationToken))
            .ToDictionary(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase);
        var groups = (await _unitOfWork.QueryGroups.GetAllAsync(cancellationToken))
            .ToDictionary(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase);

        // Original name -> the entity it became, so a dropdown referencing another query in the
        // same file still resolves after that query was renamed to avoid a clash.
        var importedByOriginalName = new Dictionary<string, DynamicQuery>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        foreach (var exported in file.Queries)
        {
            if (string.IsNullOrWhiteSpace(exported.Name) || string.IsNullOrWhiteSpace(exported.SqlQuery))
            {
                result.Warnings.Add($"Skipped an entry with no name or SQL.");
                continue;
            }

            var name = UniqueName(exported.Name, takenNames, out var wasRenamed);
            takenNames.Add(name);

            var entity = new DynamicQuery
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = exported.Description,
                SqlQuery = exported.SqlQuery,
                QueryType = QueryTypeClassifier.FromSql(exported.SqlQuery),
                IsEnabled = exported.IsEnabled,
                TimeoutSeconds = exported.TimeoutSeconds,
                IsLongRunning = exported.IsLongRunning,
                AllowRunWithoutConfirmation = exported.AllowRunWithoutConfirmation,
                SaveOldValues = exported.SaveOldValues,
                CreatedByUserId = _currentUser.UserId,
                CreatedAt = now,
                WordTemplateFileName = exported.WordTemplateFileName,
                WordTemplate = DecodeTemplate(exported, result)
            };

            entity.QueryGroupId = await ResolveGroupAsync(exported.QueryGroupName, groups, now, cancellationToken);
            entity.DatabaseUserId = ResolveDatabaseUser(exported, databaseUsers, name, result);

            foreach (var p in exported.Parameters)
            {
                entity.Parameters.Add(new QueryParameter
                {
                    Id = Guid.NewGuid(),
                    DynamicQueryId = entity.Id,
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    ParameterType = p.ParameterType,
                    IsRequired = p.IsRequired,
                    DefaultValue = p.DefaultValue,
                    SortOrder = p.SortOrder,
                    AllowMultiple = p.AllowMultiple,
                    DropdownSourceType = p.DropdownSourceType,
                    DropdownStaticValues = p.DropdownStaticValues,
                    // DropdownQueryId is wired in the second pass below.
                    DropdownQueryValueColumn = p.DropdownQueryValueColumn,
                    DropdownQueryLabelColumn = p.DropdownQueryLabelColumn,
                    CreatedAt = now
                });
            }

            AddAssignments(entity, exported, roles, users, userGroups, name, result);

            await _unitOfWork.DynamicQueries.AddAsync(entity, cancellationToken);
            importedByOriginalName[exported.Name] = entity;

            result.Queries.Add(new ImportedQueryResult
            {
                OriginalName = exported.Name,
                ImportedName = name,
                WasRenamed = wasRenamed
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await ResolveDropdownSourcesAsync(file, importedByOriginalName, result, cancellationToken);

        return result;
    }

    /// <summary>
    /// Second pass: a dropdown's source query is referenced by name, and that query may itself
    /// have just been imported (and renamed), so this can only run once every query exists.
    /// </summary>
    private async Task ResolveDropdownSourcesAsync(
        QueryExportFile file,
        IReadOnlyDictionary<string, DynamicQuery> importedByOriginalName,
        QueryImportResult result,
        CancellationToken cancellationToken)
    {
        var referencing = file.Queries
            .Where(q => q.Parameters.Any(p => !string.IsNullOrWhiteSpace(p.DropdownQueryName)))
            .ToList();
        if (referencing.Count == 0)
            return;

        // Re-read so queries that already existed on this system can also be targets.
        var byName = (await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken))
            .GroupBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var exported in referencing)
        {
            if (!importedByOriginalName.TryGetValue(exported.Name, out var entity))
                continue;

            var parameters = (await _unitOfWork.QueryParameters.FindAsync(
                p => p.DynamicQueryId == entity.Id, cancellationToken)).ToList();

            foreach (var p in exported.Parameters.Where(p => !string.IsNullOrWhiteSpace(p.DropdownQueryName)))
            {
                var target = parameters.FirstOrDefault(x => x.Name == p.Name);
                if (target is null)
                    continue;

                // Prefer a query from this same file — after renaming, its new name is the one
                // in the lookup, so match on the entity captured during the first pass.
                Guid? sourceId = importedByOriginalName.TryGetValue(p.DropdownQueryName!, out var fromFile)
                    ? fromFile.Id
                    : byName.TryGetValue(p.DropdownQueryName!, out var existing) ? existing : null;

                if (sourceId is null)
                {
                    result.Warnings.Add(
                        $"'{entity.Name}': parameter '{p.Name}' gets its options from query " +
                        $"'{p.DropdownQueryName}', which does not exist here — the dropdown will be empty " +
                        $"until you point it at a query.");
                    continue;
                }

                target.DropdownQueryId = sourceId;
                _unitOfWork.QueryParameters.Update(target);
                changed = true;
            }
        }

        if (changed)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static byte[]? DecodeTemplate(ExportedQuery exported, QueryImportResult result)
    {
        if (string.IsNullOrWhiteSpace(exported.WordTemplateBase64))
            return null;
        try
        {
            return Convert.FromBase64String(exported.WordTemplateBase64);
        }
        catch (FormatException)
        {
            // A corrupt template must not cost you the query itself.
            result.Warnings.Add($"'{exported.Name}': the Word template could not be read and was skipped.");
            return null;
        }
    }

    private async Task<Guid?> ResolveGroupAsync(
        string? groupName,
        Dictionary<string, Guid> groups,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return null;
        if (groups.TryGetValue(groupName, out var existing))
            return existing;

        // Groups are just folders, so creating a missing one is safe and keeps the restored
        // queries organised the way they were exported.
        var group = new QueryGroup
        {
            Id = Guid.NewGuid(),
            Name = groupName,
            Description = "Created by query import.",
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = now
        };
        await _unitOfWork.QueryGroups.AddAsync(group, cancellationToken);
        groups[groupName] = group.Id;
        return group.Id;
    }

    /// <summary>
    /// Matches the exported connection name against this system's connections. Deliberately
    /// leaves the query on the default connection when there is no match rather than failing —
    /// credentials are never carried in the file, so the admin must configure it here anyway.
    /// </summary>
    private static Guid? ResolveDatabaseUser(
        ExportedQuery exported,
        IReadOnlyDictionary<string, Guid> databaseUsers,
        string importedName,
        QueryImportResult result)
    {
        if (string.IsNullOrWhiteSpace(exported.DatabaseUserName))
            return null;
        if (databaseUsers.TryGetValue(exported.DatabaseUserName, out var id))
            return id;

        result.Warnings.Add(
            $"'{importedName}': database connection '{exported.DatabaseUserName}' is not configured here — " +
            $"the query was left on the default connection. Set it before running the query.");
        return null;
    }

    private static void AddAssignments(
        DynamicQuery entity,
        ExportedQuery exported,
        IReadOnlyDictionary<string, Guid> roles,
        IReadOnlyDictionary<string, Guid> users,
        IReadOnlyDictionary<string, Guid> userGroups,
        string importedName,
        QueryImportResult result)
    {
        foreach (var roleName in exported.AssignedRoles)
        {
            if (roles.TryGetValue(roleName, out var roleId))
                entity.DynamicQueryRoles.Add(new DynamicQueryRole { DynamicQueryId = entity.Id, RoleId = roleId });
            else
                result.Warnings.Add($"'{importedName}': role '{roleName}' does not exist here and was not assigned.");
        }

        // Groups are matched by name, like roles and users: an export carries no membership,
        // so a group that does not exist here would be an empty grant nobody could see.
        foreach (var groupName in exported.AssignedUserGroups)
        {
            if (userGroups.TryGetValue(groupName, out var userGroupId))
                entity.DynamicQueryUserGroups.Add(
                    new DynamicQueryUserGroup { DynamicQueryId = entity.Id, UserGroupId = userGroupId });
            else
                result.Warnings.Add(
                    $"'{importedName}': user group '{groupName}' does not exist here and was not assigned.");
        }

        foreach (var username in exported.AssignedUsers)
        {
            if (users.TryGetValue(username, out var userId))
                entity.DynamicQueryUsers.Add(new DynamicQueryUser { DynamicQueryId = entity.Id, UserId = userId });
            else
                result.Warnings.Add($"'{importedName}': user '{username}' does not exist here and was not assigned.");
        }
    }

    /// <summary>Name is capped at 200 chars in the schema, so the suffix has to fit inside it.</summary>
    private const int MaxNameLength = 200;

    private static string UniqueName(string desired, ISet<string> taken, out bool wasRenamed)
    {
        wasRenamed = false;
        if (desired.Length > MaxNameLength)
            desired = desired[..MaxNameLength];
        if (!taken.Contains(desired))
            return desired;

        wasRenamed = true;
        var suffix = 1;
        while (true)
        {
            var marker = suffix == 1 ? " (imported)" : $" (imported {suffix})";
            // Trim the base rather than the marker, so the copy is still recognisable.
            var stem = desired.Length + marker.Length > MaxNameLength
                ? desired[..(MaxNameLength - marker.Length)]
                : desired;
            var candidate = stem + marker;
            if (!taken.Contains(candidate))
                return candidate;
            suffix++;
        }
    }
}
