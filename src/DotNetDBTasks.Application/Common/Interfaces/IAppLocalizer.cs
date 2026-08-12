namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Resolves a user-facing message in the current request's language.
///
/// <para>
/// A thin interface rather than <c>IStringLocalizer&lt;T&gt;</c> directly so the Application
/// layer keeps depending only on abstractions — the same reason <see cref="ICurrentUserService"/>
/// exists. The Infrastructure implementation wraps the ASP.NET localizer over
/// <c>Resources/Messages.resx</c>.
/// </para>
///
/// <para>
/// Only strings a user reads belong here. Diagnostic text — "Role with ID '&lt;guid&gt;' does not
/// exist", raw Oracle or LDAP detail — stays in English, because it is read by whoever is
/// debugging, not by the person clicking the button.
/// </para>
/// </summary>
public interface IAppLocalizer
{
    /// <summary>The message for <paramref name="key"/>, with any <paramref name="args"/> substituted.</summary>
    string this[string key, params object[] args] { get; }
}

/// <summary>
/// Resource keys, so a typo is a compile error rather than an English string leaking into an
/// Arabic page. Names match the <c>data name=</c> entries in Messages.resx.
/// </summary>
public static class MessageKeys
{
    public const string UsernameTaken = nameof(UsernameTaken);
    public const string EmailInUse = nameof(EmailInUse);
    public const string CannotChangeOwnRoles = nameof(CannotChangeOwnRoles);
    public const string OnlyAdminCanModifyAdmin = nameof(OnlyAdminCanModifyAdmin);
    public const string OnlyAdminCanGrantAdmin = nameof(OnlyAdminCanGrantAdmin);
    public const string CannotRemoveLastAdmin = nameof(CannotRemoveLastAdmin);
    public const string LdapUsernameImmutable = nameof(LdapUsernameImmutable);
    public const string LdapPasswordImmutable = nameof(LdapPasswordImmutable);
    public const string LdapPasswordResetUnavailable = nameof(LdapPasswordResetUnavailable);

    public const string NoAccessToQuery = nameof(NoAccessToQuery);
    public const string QueryDisabled = nameof(QueryDisabled);
    public const string NoAccessToDatabaseUser = nameof(NoAccessToDatabaseUser);
    public const string NoAccessToScheduledTask = nameof(NoAccessToScheduledTask);
    public const string NoPermissionToDownloadTaskFiles = nameof(NoPermissionToDownloadTaskFiles);

    public const string SaveFailed = nameof(SaveFailed);
    public const string SaveFailedDetail = nameof(SaveFailedDetail);
    public const string DuplicateValue = nameof(DuplicateValue);
    public const string RequiredFieldEmpty = nameof(RequiredFieldEmpty);
    public const string UnexpectedError = nameof(UnexpectedError);
    public const string ValidationFailed = nameof(ValidationFailed);
    public const string OperationCancelled = nameof(OperationCancelled);

    public const string AdUnreachable = nameof(AdUnreachable);
    public const string AdOpSearching = nameof(AdOpSearching);
    public const string AdOpListingDepartments = nameof(AdOpListingDepartments);
    public const string AdOpListingMembers = nameof(AdOpListingMembers);
    public const string ImportLocalAccountExists = nameof(ImportLocalAccountExists);
    public const string ImportEmailClashInBatch = nameof(ImportEmailClashInBatch);
    public const string ImportEmailTaken = nameof(ImportEmailTaken);
    public const string ImportSummaryImported = nameof(ImportSummaryImported);
    public const string ImportSummaryAlreadyPresent = nameof(ImportSummaryAlreadyPresent);
    public const string ImportSummaryNotFound = nameof(ImportSummaryNotFound);
    public const string ImportSummarySkipped = nameof(ImportSummarySkipped);
    public const string ImportSummaryMore = nameof(ImportSummaryMore);
    public const string ImportNoDepartmentMatches = nameof(ImportNoDepartmentMatches);
}
