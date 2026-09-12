import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import {
  disabledFtpsStatus,
  runningFtpsStatus,
  stubFtpUsers,
  stubFtpsServer,
  stubFtpsServerForbidden,
} from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { FirewallRule } from '../../src/types/firewall'
import type { FtpsStatus } from '../../src/types/ftpsStatus'
import type { PanelModule } from '../../src/types/module'

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

/** The rules that let the daemon's own ports through, matching the numbers the status reports. */
const FTPS_RULES: FirewallRule[] = [
  { port: 21, portTo: null, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
  { port: 30000, portTo: 30099, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
]

/** A rule that has nothing to do with FTPS, so "some rules exist" cannot be mistaken for coverage. */
const UNRELATED_RULE: FirewallRule = {
  port: 443,
  portTo: null,
  protocol: 'tcp',
  sourceCidr: '0.0.0.0/0',
}

/**
 * Opens the merged screen for an administrator with a given daemon state and firewall.
 * @param page The Playwright page under test.
 * @param status The FTPS daemon state the panel reports.
 * @param rules The port rules the firewall is running.
 * @returns Resolves once the screen has been opened.
 */
/**
 * The server panel's own heading — the positive control every negative assertion below needs.
 *
 * Without it a `toHaveCount(0)` on a line INSIDE the panel passes whenever the panel itself failed
 * to render, which is the vacuity this file's first version actually had: a mutation that rendered
 * the IPv4 line unconditionally survived, because the assertion that should have caught it was
 * looking at a screen with no panel on it at all.
 * @param page The Playwright page under test.
 * @returns The heading locator.
 */
const SERVER_PANEL = (page: Page): Locator => {
  return page.getByRole('heading', { level: 2, name: 'FTPS service' })
}

const openScreen = async (
  page: Page,
  status: FtpsStatus,
  rules: FirewallRule[],
): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules, bans: [], whitelist: [] })
  await stubFtpsServer(page, status)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer')
}

// Settled row 6's last mile: the host decided, the agent reported, the panel persisted, and this is
// the line the operator actually reads. Both directions, because a line that is always there says
// nothing.
test('the server panel states the IPv4-only mode exactly when the status reports it', async ({
  page,
}) => {
  await openScreen(page, { ...runningFtpsStatus, ipv4Only: true }, FTPS_RULES)
  await expect(page.getByTestId('ftps-ipv4-only')).toHaveText(
    'This host has no IPv6 address, so the daemon is bound to IPv4 only.',
  )

  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await openScreen(page, { ...runningFtpsStatus, ipv4Only: false }, FTPS_RULES)
  await expect(SERVER_PANEL(page)).toBeVisible()
  await expect(page.getByTestId('ftps-ipv4-only')).toHaveCount(0)
})

// Task 11's SPA-side composition. Neither module can answer this alone: the Ftp module reads no
// firewall and the Firewall module knows nothing about which ports FTPS wants.
test('the listening-and-unreachable warning follows the composed state in both directions', async ({
  page,
}) => {
  // Answering on the host, and the only rule open is for something else entirely.
  await openScreen(page, runningFtpsStatus, [UNRELATED_RULE])
  await expect(page.getByTestId('ftps-unreachable')).toHaveText(
    'The service is answering on this host, but no firewall rule lets its ports through. Nobody outside the server can connect.',
  )

  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await openScreen(page, runningFtpsStatus, FTPS_RULES)
  await expect(SERVER_PANEL(page)).toBeVisible()
  await expect(page.getByTestId('ftps-unreachable')).toHaveCount(0)
})

// `DisableFtps` never touches the firewall — stopping a service is not editing a firewall — so this
// warning is the only place the leftover surface becomes visible.
test('the disabled-but-open warning follows the composed state in both directions', async ({
  page,
}) => {
  await openScreen(page, disabledFtpsStatus, FTPS_RULES)
  await expect(page.getByTestId('ftps-lingering')).toHaveText(
    'FTPS is turned off and its ports are still open in the firewall. Stopping a service does not close a port.',
  )

  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await openScreen(page, disabledFtpsStatus, [UNRELATED_RULE])
  await expect(SERVER_PANEL(page)).toBeVisible()
  await expect(page.getByTestId('ftps-lingering')).toHaveCount(0)
})

// A disabled daemon is a normal state. It is reported as a setting, not as a failure, and no error
// banner appears beside it.
test('a turned-off daemon reads as a setting rather than as an error', async ({ page }) => {
  await openScreen(page, disabledFtpsStatus, [])

  await expect(SERVER_PANEL(page)).toBeVisible()
  await expect(page.getByRole('term').filter({ hasText: 'Panel setting' })).toBeVisible()
  await expect(page.getByText('Turned off', { exact: true })).toBeVisible()
  await expect(page.getByText('Stopped', { exact: true })).toBeVisible()
  await expect(page.getByTestId('ftps-unreachable')).toHaveCount(0)
  await expect(page.getByTestId('ftps-lingering')).toHaveCount(0)
})

// Every number on this screen arrives in the response. A panel that wrote its own ports would still
// look right against a server configured with different ones.
test('the ports on screen are the ones the server reported, not ones the SPA holds', async ({
  page,
}) => {
  const MOVED: FtpsStatus = {
    ...runningFtpsStatus,
    controlPort: 2121,
    passivePortMin: 40000,
    passivePortMax: 40099,
  }
  await openScreen(page, MOVED, FTPS_RULES)

  await expect(page.getByText('2121', { exact: true })).toBeVisible()
  await expect(page.getByText('40000–40099', { exact: true })).toBeVisible()
  // And the composition follows them: the rules that covered 21 and 30000–30099 do not cover these.
  await expect(page.getByTestId('ftps-unreachable')).toBeVisible()
})

// Both directions, on the panel as well as in the credential dialog.
test('the placeholder-certificate line on the server panel follows the status', async ({ page }) => {
  await openScreen(page, { ...runningFtpsStatus, certificateIsSelfSigned: true }, FTPS_RULES)
  await expect(page.getByTestId('ftps-self-signed')).toContainText(
    'The certificate is a placeholder this panel generated.',
  )

  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await openScreen(page, runningFtpsStatus, FTPS_RULES)
  await expect(SERVER_PANEL(page)).toBeVisible()
  await expect(page.getByTestId('ftps-self-signed')).toHaveCount(0)
})

// The panel refuses the status to a customer, and that refusal is not a failure: the screen must
// still render the customer's own half rather than a red banner about a correct 403.
test('a caller refused the status sees no server panel and no error', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServerForbidden(page)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer')

  // The positive control, and it is not optional: both assertions below are absences, and an
  // absence on a screen that has not finished loading is true of every screen. Measured here — a
  // mutation that stored the 403 as a failure SURVIVED until this line existed, because the two
  // checks ran while the page still read "Loading logins…".
  await expect(page.getByText('No file transfer logins yet')).toBeVisible()
  await expect(SERVER_PANEL(page)).toHaveCount(0)
  await expect(page.getByText('Administrators only.')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Create login' })).toBeVisible()
})

// The offer beside the warning has to do something. It sends exactly the ports the status named.
test('the open-ports offer installs rules for the ports the server reported', async ({ page }) => {
  await openScreen(page, runningFtpsStatus, [UNRELATED_RULE])

  const posted: unknown[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/firewall/rules')) {
      posted.push(request.postDataJSON())
    }
  })

  await page.getByRole('button', { name: 'Open the FTPS ports' }).click()

  await expect(page.getByTestId('ftps-unreachable')).toHaveCount(0)
  expect(posted).toEqual([
    { port: 21, portTo: null, protocol: 'tcp', sourceCidr: '' },
    { port: 30000, portTo: 30099, protocol: 'tcp', sourceCidr: '' },
  ])
})

// The field a screen built from the plan's own text would be blind to: the plan calls the status
// "eight facts" in four places and it is nine, the ninth being forced TLS — the one that says
// whether the daemon actually requires encryption, which is the reason this protocol is offered.
//
// It renders false when UNKNOWN by design (no live configuration, an unreadable file, an older
// agent), so the two states are "required" and "not confirmed as required" — never "off", which
// would be a claim the agent did not make. It sits beside the panel's own `Enabled` intention so an
// operator can see the two disagree rather than be shown one of them as the truth.
test('the forced-TLS fact is stated in both directions and never as the panel intention', async ({
  page,
}) => {
  await openScreen(page, { ...runningFtpsStatus, forcedTls: true }, FTPS_RULES)
  await expect(SERVER_PANEL(page)).toBeVisible()
  await expect(page.getByText('TLS required for logins and transfers', { exact: true })).toBeVisible()
  await expect(page.getByText('Not confirmed as required', { exact: true })).toHaveCount(0)

  // The panel still believes FTPS is enabled here. A screen that took the intention as the answer
  // would keep reporting enforced encryption over a daemon reporting that it enforces none.
  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await openScreen(page, { ...runningFtpsStatus, enabled: true, forcedTls: false }, FTPS_RULES)
  await expect(SERVER_PANEL(page)).toBeVisible()
  await expect(page.getByText('Not confirmed as required', { exact: true })).toBeVisible()
  await expect(page.getByText('TLS required for logins and transfers', { exact: true })).toHaveCount(0)
  await expect(page.getByText('Turned on', { exact: true })).toBeVisible()
})

// Measured on a live stack, not imagined: a server with no certificate installed answers
// `certificatePath: ""`, and the panel rendered that cell as nothing at all. A blank cell is
// indistinguishable from a cell that failed to render, which is the one reading this screen may
// never allow — every other unknown here says so with `common.emptyValue`. The suite could not
// have caught it: every status fixture carries a real path, so the empty case had never been
// rendered. Note the guard must test emptiness and not absence — the wire sends "", never null,
// so a `??` fallback would have left the blank cell exactly where it was.
test('a server with no certificate installed says so rather than rendering nothing', async ({
  page,
}) => {
  await openScreen(page, { ...runningFtpsStatus, certificatePresent: false, certificatePath: '' }, FTPS_RULES)
  await expect(SERVER_PANEL(page)).toBeVisible()
  // Counted rather than merely found: every other field in this fixture has a value, so the one
  // dash on the screen is the certificate's. Asserting mere presence would also pass on a screen
  // that had turned every cell into a dash, which is the failure the inverse control below rules
  // out from the other side.
  await expect(page.getByText('—', { exact: true })).toHaveCount(1)

  // The inverse control: a path that IS there is still printed, so the fallback has not swallowed
  // the real value. Without this, rendering every certificate cell as a dash would pass.
  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await openScreen(page, runningFtpsStatus, FTPS_RULES)
  await expect(
    page.getByText('/etc/maran/certificates/panel.example.net/fullchain.pem', { exact: true }),
  ).toBeVisible()
  await expect(page.getByText('—', { exact: true })).toHaveCount(0)
})
