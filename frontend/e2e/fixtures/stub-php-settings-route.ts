import type { Page } from '@playwright/test'
import { FULL_CATALOGUE } from './open-full-shell'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { stubModules } from './stub-modules-route'
import { stubMyAccount } from './stub-my-account-route'
import { OWNER_PROFILE, stubProfile } from './stub-profile-route'
import { stubShellReads } from './stub-shell-reads'
import type { MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PhpSettingItem, PhpSettingsDto, SavePhpSettingsRequest } from '../../src/types/phpSetting'

/** The one endpoint the customer's PHP settings are read from and written to. */
export const PHP_SETTINGS_PATTERN = '**/api/v1/php-settings'

/** The account the stubbed settings belong to. */
const ACCOUNT_ID = '11111111-1111-1111-1111-111111111111'

/**
 * The settings a panel lists in specs: the memory limit is stored above the plan's maximum (so the
 * notice and the effective value show), the upload size and input variables are stored within it,
 * the execution time was never set and the timezone offers two zones.
 */
export const PHP_SETTING_ITEMS: PhpSettingItem[] = [
  {
    name: 'memory_limit',
    label: 'Memory limit',
    kind: 'size',
    unit: 'MiB',
    value: '512',
    effectiveValue: '256',
    planMaximum: 256,
    exceedsPlan: true,
    choices: null,
  },
  {
    name: 'upload_max_filesize',
    label: 'Upload size',
    kind: 'size',
    unit: 'MiB',
    value: '32',
    effectiveValue: '32',
    planMaximum: 64,
    exceedsPlan: false,
    choices: null,
  },
  {
    name: 'max_execution_time',
    label: 'Execution time',
    kind: 'seconds',
    unit: 'seconds',
    value: null,
    effectiveValue: null,
    planMaximum: 120,
    exceedsPlan: false,
    choices: null,
  },
  {
    name: 'max_input_vars',
    label: 'Input variables',
    kind: 'count',
    unit: null,
    value: '2000',
    effectiveValue: '2000',
    planMaximum: 3000,
    exceedsPlan: false,
    choices: null,
  },
  {
    name: 'date.timezone',
    label: 'Timezone',
    kind: 'timezone',
    unit: null,
    value: null,
    effectiveValue: null,
    planMaximum: null,
    exceedsPlan: false,
    choices: ['Asia/Yerevan', 'Europe/Berlin'],
  },
]

/** The customer's own account, which the shell reads on every screen. */
const OWN_ACCOUNT: MyAccount = {
  id: ACCOUNT_ID,
  name: 'olive',
  primaryDomain: 'olive.example.com',
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

/** The signed-in customer. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: ACCOUNT_ID,
}

/**
 * Signs in as the customer with the shell's quiet reads answered and every module licensed.
 * @param page The Playwright page under test.
 * @returns Resolves once the stubs are installed.
 */
export const signInAsPhpCustomer = async (page: Page): Promise<void> => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubProfile(page, OWNER_PROFILE)
}

/** What the stub recorded about the traffic the screen produced. */
export interface PhpSettingsTraffic {
  /** How many GETs reached the stub. */
  reads: () => number
  /** Every body a PUT carried, oldest first. */
  writes: () => SavePhpSettingsRequest[]
}

/**
 * Answers `GET /api/v1/php-settings` with the given items, and `PUT` by storing what was sent (a
 * setting not sent goes back to unset, as the real panel's full replacement does), recording both.
 * @param page The Playwright page whose network the route is installed on.
 * @param items The settings the panel starts with.
 * @param sitesAffected How many PHP sites the settings reach.
 * @param refusal A sentence to refuse every `PUT` with, or `undefined` to accept them.
 * @returns The traffic recorded so far.
 */
export const stubPhpSettings = async (
  page: Page,
  items: PhpSettingItem[] = PHP_SETTING_ITEMS,
  sitesAffected = 2,
  refusal?: string,
): Promise<PhpSettingsTraffic> => {
  let reads = 0
  const writes: SavePhpSettingsRequest[] = []
  let current: PhpSettingItem[] = items.map((item) => {
    return { ...item }
  })

  await page.route(PHP_SETTINGS_PATTERN, async (route) => {
    if (route.request().method() === 'PUT') {
      const sent = route.request().postDataJSON() as SavePhpSettingsRequest
      writes.push(sent)
      if (refusal !== undefined) {
        await route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({ code: 'PhpSettingAbovePlan', title: 'Invalid', detail: refusal }),
        })
        return
      }
      current = current.map((item) => {
        const stored = sent.settings.find((setting) => {
          return setting.name === item.name
        })
        const value = stored?.value ?? null
        return { ...item, value, effectiveValue: value, exceedsPlan: false }
      })
    } else {
      reads += 1
    }
    const answer: PhpSettingsDto = { accountId: ACCOUNT_ID, items: current, sitesAffected }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer) })
  })

  return {
    reads: (): number => {
      return reads
    },
    writes: (): SavePhpSettingsRequest[] => {
      return writes
    },
  }
}
