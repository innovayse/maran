import type { Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from './stub-accounts-route'
import { stubSignedIn } from './stub-auth-routes'
import { stubDns, type DnsStubState } from './stub-dns-routes'
import { stubHealthy } from './stub-health-route'
import { stubModules } from './stub-modules-route'
import { stubMyAccount } from './stub-my-account-route'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { DnsZone } from '../../src/types/dns'
import type { PanelModule } from '../../src/types/module'

/** The modules the DNS screens need licensed. */
export const DNS_MODULES: PanelModule[] = [
  { name: 'dns', displayName: 'DNS', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

/** A customer account owning zones. */
export const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/** A second account, so a list holds rows of more than one owner. */
export const BOB: Account = {
  ...ALICE,
  id: '33333333-3333-3333-3333-333333333333',
  name: 'bob',
  primaryDomain: 'bob.example.com',
}

/** The signed-in customer who owns {@link ALICE}. */
export const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: ALICE.id,
}

/** What the customer's own-account read answers; the zone pages read only its identity. */
const OWN_ACCOUNT: MyAccount = {
  id: ALICE.id,
  name: ALICE.name,
  primaryDomain: ALICE.primaryDomain,
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

/** Alice's zone. */
export const EXAMPLE_ZONE: DnsZone = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  accountName: ALICE.name,
  name: 'example.com',
  serial: 2026110701,
  recordCount: 3,
  createdAt: '2026-08-01T10:00:00Z',
  updatedAt: '2026-11-07T09:00:00Z',
  nameservers: ['ns1.panel.example', 'ns2.panel.example'],
}

/** Bob's zone. */
export const SHOP_ZONE: DnsZone = {
  ...EXAMPLE_ZONE,
  id: '55555555-5555-5555-5555-555555555555',
  accountId: BOB.id,
  accountName: BOB.name,
  name: 'shop.example.org',
  serial: 2026110502,
  recordCount: 0,
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-11-05T09:00:00Z',
}

/**
 * Signs a person in and installs the stubs every DNS screen reads.
 * @param page The Playwright page under test.
 * @param seed What the panel holds; see {@link stubDns}.
 * @param user The signed-in user; the administrator when omitted.
 * @returns The stub's live state.
 */
export const openDnsScreen = async (
  page: Page,
  seed: Partial<DnsStubState> = {},
  user?: AuthenticatedUser,
): Promise<DnsStubState> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, DNS_MODULES)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  await stubMyAccount(page, OWN_ACCOUNT)
  return stubDns(page, seed)
}
