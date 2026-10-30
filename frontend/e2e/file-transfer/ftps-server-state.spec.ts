import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import {
  disabledFtpsStatus,
  neverConfiguredFtpsStatus,
  runningFtpsStatus,
  stubFtpUsers,
  stubFtpsServer,
  stubFtpsServerSwitch,
  stubFtpsServerSwitchProblem,
  stubFtpsServerSwitchUnreachable,
} from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { FtpsStatus } from '../../src/types/ftpsStatus'
import type { PanelModule } from '../../src/types/module'

// Issue #215: the card used to say "Turned off" beside "Running", leave "Turn FTPS off" enabled in
// every state, and answer a click with nothing. One case per state the backend's verdict can take,
// then the feedback a switch owes.

const LICENSED: PanelModule[] = [
  { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
  { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** The service runs although the panel has FTPS off — the state the issue was filed against. */
const RUNNING_BUT_OFF: FtpsStatus = {
  ...runningFtpsStatus,
  desiredState: 'off',
  agreement: 'runningButOff',
}

/** The panel wants FTPS on and the unit is not running. */
const ON_BUT_NOT_RUNNING: FtpsStatus = {
  ...runningFtpsStatus,
  agreement: 'onButNotRunning',
  running: false,
  controlPortAnswered: false,
}

/** The unit is active and the control port gave no greeting. */
const RUNNING_BUT_NOT_ANSWERING: FtpsStatus = {
  ...runningFtpsStatus,
  agreement: 'runningButNotAnswering',
  controlPortAnswered: false,
}

/**
 * The server panel's own heading — the positive control every absence below needs, since an absence
 * on a screen that never drew the panel is true of every screen.
 * @param page The Playwright page under test.
 * @returns The heading locator.
 */
const SERVER_PANEL = (page: Page): Locator => {
  return page.getByRole('heading', { level: 2, name: 'FTPS service' })
}

/**
 * Opens the FTPS page for an administrator with a given daemon state and no firewall rules.
 * @param page The Playwright page under test.
 * @param status The FTPS daemon state the panel reports.
 * @returns Resolves once the screen has been opened.
 */
const openScreen = async (page: Page, status: FtpsStatus): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, status)
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer/ftps')
  await expect(SERVER_PANEL(page)).toBeVisible()
}

test('a server nobody configured says so, and the off switch is disabled with its reason', async ({
  page,
}) => {
  await openScreen(page, neverConfiguredFtpsStatus)

  await expect(page.getByText('Never configured', { exact: true })).toBeVisible()
  await expect(page.getByText('Turned off', { exact: true })).toHaveCount(0)
  await expect(page.getByText('Stopped', { exact: true })).toBeVisible()
  await expect(page.getByTestId('ftps-agreement')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Turn FTPS off' })).toBeDisabled()
  await expect(page.getByTestId('ftps-disable-reason')).toHaveText(
    'FTPS has never been turned on on this server, and the service is not running.',
  )
})

test('a server turned off and stopped disables the off switch and says it is already off', async ({
  page,
}) => {
  await openScreen(page, disabledFtpsStatus)

  await expect(page.getByText('Turned off', { exact: true })).toBeVisible()
  await expect(page.getByTestId('ftps-agreement')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Turn FTPS off' })).toBeDisabled()
  await expect(page.getByTestId('ftps-disable-reason')).toHaveText(
    'FTPS is already turned off and the service is not running.',
  )
})

test('a server that is on and serving reads as running, with the off switch available', async ({
  page,
}) => {
  await openScreen(page, runningFtpsStatus)

  await expect(page.getByText('Turned on', { exact: true })).toBeVisible()
  await expect(page.getByText('Running', { exact: true })).toBeVisible()
  await expect(page.getByTestId('ftps-agreement')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Turn FTPS off' })).toBeEnabled()
  await expect(page.getByTestId('ftps-disable-reason')).toHaveCount(0)
})

test('a service running against an off setting is explained and the switch becomes its repair', async ({
  page,
}) => {
  await openScreen(page, RUNNING_BUT_OFF)

  await expect(page.getByText('Turned off', { exact: true })).toBeVisible()
  await expect(page.getByTestId('ftps-agreement')).toHaveText(
    'The service is running although FTPS is turned off in the panel. Customers can still reach it; stop the service to match the setting.',
  )
  await expect(page.getByRole('button', { name: 'Stop the service' })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Turn FTPS off' })).toHaveCount(0)
  await expect(page.getByTestId('ftps-disable-reason')).toHaveCount(0)
})

test('a server on but not running is explained and reads as stopped', async ({ page }) => {
  await openScreen(page, ON_BUT_NOT_RUNNING)

  await expect(page.getByText('Turned on', { exact: true })).toBeVisible()
  await expect(page.getByText('Stopped', { exact: true })).toBeVisible()
  await expect(page.getByTestId('ftps-agreement')).toHaveText(
    'FTPS is turned on in the panel, but the service is not running. Turning FTPS on again re-applies the configuration and starts it.',
  )
  await expect(page.getByRole('button', { name: 'Turn FTPS off' })).toBeEnabled()
})

test('an active unit whose control port is silent is never called running', async ({ page }) => {
  await openScreen(page, RUNNING_BUT_NOT_ANSWERING)

  await expect(page.getByText('Not answering', { exact: true })).toBeVisible()
  await expect(page.getByText('Running', { exact: true })).toHaveCount(0)
  await expect(page.getByTestId('ftps-agreement')).toHaveText(
    'The service is running, but its control port gave no greeting on this host, so no client can be served.',
  )
})

test('turning FTPS off answers with a toast worded from the state measured afterwards', async ({
  page,
}) => {
  await openScreen(page, runningFtpsStatus)
  await stubFtpsServerSwitch(page, disabledFtpsStatus)

  await page.getByRole('button', { name: 'Turn FTPS off' }).click()

  await expect(page.getByText('FTPS is turned off and the service is stopped.')).toBeVisible()
  await expect(page.getByText('Turned off', { exact: true })).toBeVisible()
})

test('turning FTPS off over a service that keeps running says it is still running', async ({
  page,
}) => {
  await openScreen(page, runningFtpsStatus)
  await stubFtpsServerSwitch(page, RUNNING_BUT_OFF)

  await page.getByRole('button', { name: 'Turn FTPS off' }).click()

  // The inverse of the toast above: the same click, a different measurement, a different sentence.
  await expect(page.getByText('FTPS is turned off, but the service is still running.')).toBeVisible()
  await expect(page.getByText('FTPS is turned off and the service is stopped.')).toHaveCount(0)
})

test('turning FTPS off re-reads the firewall, as turning it on does', async ({ page }) => {
  // Counted from before the screen opens, so the mount's own read is the baseline rather than a
  // late arrival that could be mistaken for the re-read.
  const reads: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'GET' && request.url().includes('/api/v1/firewall/rules')) {
      reads.push(request.url())
    }
  })

  await openScreen(page, runningFtpsStatus)
  await expect
    .poll(() => {
      return reads.length
    })
    .toBe(1)
  await stubFtpsServerSwitch(page, disabledFtpsStatus)

  await page.getByRole('button', { name: 'Turn FTPS off' }).click()

  await expect(page.getByText('FTPS is turned off and the service is stopped.')).toBeVisible()
  expect(reads).toHaveLength(2)
})

test('a refused switch shows the backend sentence on the card', async ({ page }) => {
  await openScreen(page, runningFtpsStatus)
  await stubFtpsServerSwitchProblem(page, 'The agent could not stop the FTPS service.')

  await page.getByRole('button', { name: 'Turn FTPS off' }).click()

  await expect(page.getByTestId('ftps-action-failure')).toHaveText(
    'The agent could not stop the FTPS service.',
  )
})

test('a switch that never reached the panel still says so, in the generic sentence', async ({
  page,
}) => {
  await openScreen(page, runningFtpsStatus)
  await stubFtpsServerSwitchUnreachable(page)

  await page.getByRole('button', { name: 'Turn FTPS off' }).click()

  await expect(page.getByTestId('ftps-action-failure')).toHaveText(
    'The request did not complete. Check that the panel is reachable and try again.',
  )
})
