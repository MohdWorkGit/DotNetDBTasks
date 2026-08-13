/**
 * The capability names the API checks. Mirrors Permissions.cs — the two must stay in step,
 * since these strings are compared against what /api/auth/me returns.
 *
 * <p>Nothing here is a security boundary. The server decides; these only keep the UI from
 * offering a control that would come back 403.</p>
 */
export const PERM = {
  queriesView: 'queries.view',
  queriesReadSql: 'queries.readSql',
  queriesManage: 'queries.manage',
  queriesTransfer: 'queries.transfer',
  queriesRun: 'queries.run',
  accessManageQuery: 'access.manageQuery',
  accessManageGroup: 'access.manageGroup',
  queryGroupsManage: 'queryGroups.manage',
  userGroupsView: 'userGroups.view',
  userGroupsManage: 'userGroups.manage',
  usersView: 'users.view',
  usersManage: 'users.manage',
  directoryView: 'directory.view',
  directoryManage: 'directory.manage',
  databaseUsersManage: 'databaseUsers.manage',
  scheduledTasksViewAll: 'scheduledTasks.viewAll',
  scheduledTasksManage: 'scheduledTasks.manage',
  scheduledTasksDownload: 'scheduledTasks.download',
  logsView: 'logs.view',
  auditView: 'audit.view',
  brandingManage: 'branding.manage',
  settingsManage: 'settings.manage',
  rolesManage: 'roles.manage'
} as const;

export type PermissionName = typeof PERM[keyof typeof PERM];

/**
 * The order the Permissions tab lists them in, with the heading each falls under. Groups the
 * grid by area so a 23-row matrix reads as six short lists rather than one long one.
 */
export const PERMISSION_GROUPS: { titleKey: string; permissions: string[] }[] = [
  {
    titleKey: 'admin.permissions.groups.queries',
    permissions: [PERM.queriesView, PERM.queriesReadSql, PERM.queriesManage,
                  PERM.queriesTransfer, PERM.queriesRun]
  },
  {
    titleKey: 'admin.permissions.groups.access',
    permissions: [PERM.accessManageQuery, PERM.accessManageGroup, PERM.queryGroupsManage]
  },
  {
    titleKey: 'admin.permissions.groups.people',
    permissions: [PERM.usersView, PERM.usersManage, PERM.userGroupsView, PERM.userGroupsManage,
                  PERM.directoryView, PERM.directoryManage]
  },
  {
    titleKey: 'admin.permissions.groups.data',
    permissions: [PERM.databaseUsersManage, PERM.scheduledTasksViewAll,
                  PERM.scheduledTasksManage, PERM.scheduledTasksDownload]
  },
  {
    titleKey: 'admin.permissions.groups.oversight',
    permissions: [PERM.logsView, PERM.auditView]
  },
  {
    titleKey: 'admin.permissions.groups.system',
    permissions: [PERM.brandingManage, PERM.settingsManage, PERM.rolesManage]
  }
];

/** The i18n key for a permission's label and its explanation. */
export function permissionLabelKey(permission: string): string {
  return 'admin.permissions.names.' + permission;
}

export function permissionHintKey(permission: string): string {
  return 'admin.permissions.hints.' + permission;
}
