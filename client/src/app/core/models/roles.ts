/**
 * Role names as they arrive in the JWT. Mirrors RoleNames.cs on the API — the two
 * must stay in step, since these strings are compared against the token's role claims.
 */
export const ADMIN = 'Admin';
export const USER = 'User';
export const AUDITOR = 'Auditor';
export const ACCESS_MANAGER = 'AccessManager';

/** Human-readable labels for role chips and pickers. */
export const ROLE_LABELS: Record<string, string> = {
  [ADMIN]: 'Admin',
  [USER]: 'User',
  [AUDITOR]: 'Auditor',
  [ACCESS_MANAGER]: 'Access Manager'
};

export function roleLabel(name: string): string {
  return ROLE_LABELS[name] ?? name;
}

/**
 * Roles that never confer access to a query. Mirrors QueryAccessRoles on the API, which
 * drops them when resolving access — so assigning a query or group to one of these does
 * nothing, and the access pickers filter them out rather than offer a no-op.
 */
const NON_GRANTING_ROLES = [AUDITOR, ACCESS_MANAGER];

export function grantsQueryAccess(name: string): boolean {
  return !NON_GRANTING_ROLES.includes(name);
}
