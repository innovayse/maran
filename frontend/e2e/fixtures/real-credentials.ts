import { expect } from '@playwright/test'

// Only the environment is read, so it is declared here rather than adding Node's global types to a
// program that also compiles browser code (same arrangement as e2e/install).
declare const process: { readonly env: Readonly<Record<string, string | undefined>> }

/** The roles the real-stack lane signs in as. */
export type RealRole = 'administrator' | 'reseller' | 'customer'

/** A username and password for one role. */
export interface RealCredentials {
  /** The login name. */
  username: string
  /** The password. */
  password: string
}

/** The environment variables that carry each role's login; `maran e2e-real` checks them before it starts. */
const VARIABLES: Record<RealRole, { username: string; password: string }> = {
  administrator: { username: 'MARAN_REAL_ADMIN_USERNAME', password: 'MARAN_REAL_ADMIN_PASSWORD' },
  reseller: { username: 'MARAN_REAL_RESELLER_USERNAME', password: 'MARAN_REAL_RESELLER_PASSWORD' },
  customer: { username: 'MARAN_REAL_CUSTOMER_USERNAME', password: 'MARAN_REAL_CUSTOMER_PASSWORD' },
}

/**
 * Reads the login a role signs in with. An unset value is a failure, never a skip: a lane that
 * printed a pass for a role it never signed in as would be the false pass this lane exists to end.
 * @param role The role to sign in as.
 * @returns Its username and password.
 */
export const credentialsFor = (role: RealRole): RealCredentials => {
  const names = VARIABLES[role]
  const username = process.env[names.username] ?? ''
  const password = process.env[names.password] ?? ''
  expect(username, `${names.username} is unset, so there is no ${role} to sign in as`).not.toBe('')
  expect(password, `${names.password} is unset, so there is no ${role} to sign in as`).not.toBe('')
  return { username, password }
}

/**
 * Reads a name that makes this run's data its own, so two runs against one panel never meet.
 * @param prefix A short word saying what the data is.
 * @returns A lower-case name unique to this call.
 */
export const uniqueName = (prefix: string): string => {
  // Letters and digits only: the shortest rule any of the panel's name validators accepts.
  const stamp = Date.now().toString(36)
  const salt = (crypto.getRandomValues(new Uint16Array(1))[0] ?? 0).toString(36)
  return `${prefix}${stamp}${salt}`.toLowerCase()
}
