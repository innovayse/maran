import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import {
  runningFtpsStatus,
  stubFtpUserActions,
  stubFtpUsers,
  stubFtpsServer,
  stubFtpsServerForbidden,
  stubbedCreatedFtpPassword,
} from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import {
  stubSftpUserActions,
  stubSftpUsers,
  stubbedCreatedSftpPassword,
  stubbedResetSftpPassword,
} from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { FtpUser } from '../../src/types/ftpUser'
import type { FtpsStatus } from '../../src/types/ftpsStatus'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

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

const WEB: SftpUser = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  name: 'web',
  fullName: 'alice_web',
  createdAt: '2026-08-01T10:00:00Z',
}

const UPLOAD: FtpUser = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  name: 'upload',
  fullName: 'alice_upload',
  protocol: 'Ftps',
  createdAt: '2026-08-02T10:00:00Z',
}

/**
 * Puts the screen in front of an operator, with whatever logins the case needs already created.
 * @param page The Playwright page under test.
 * @param sftpUsers The SFTP logins the panel reports.
 * @param ftpUsers The FTPS logins the panel reports.
 * @param startAt The path to open.
 * @returns Resolves once the screen has been opened.
 */
const openScreen = async (
  page: Page,
  sftpUsers: SftpUser[],
  ftpUsers: FtpUser[] = [],
  startAt = '/file-transfer',
): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, sftpUsers)
  await stubFtpUsers(page, ftpUsers)
  await stubSftpUserActions(page, WEB.fullName)
  await stubFtpUserActions(page, UPLOAD.fullName)
  await page.goto(startAt)
}

/**
 * Fills the create form and submits it, which is the path that mints a password.
 * @param page The Playwright page under test.
 * @param protocol The protocol label to choose.
 * @param name The login suffix to type.
 * @returns Resolves once the create has been submitted.
 */
const createLogin = async (page: Page, protocol: string, name: string): Promise<void> => {
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  // The radio input itself is `sr-only`, so a direct click lands on a 1px element the document
  // intercepts. Clicking the visible LABEL is what a person does, and it exercises the `for`/`id`
  // binding the kit's accessibility rules require. The state is then asserted, because a click that
  // silently missed would otherwise leave the default protocol selected and the spec still green.
  await page.getByRole('radiogroup').getByText(protocol, { exact: true }).click()
  await expect(page.getByRole('radio', { name: protocol, exact: true })).toBeChecked()
  await page.getByRole('textbox', { name: 'Login name' }).fill(name)
  await page.getByRole('button', { name: 'Create login' }).click()
}

/**
 * Opens a row's actions menu and chooses one of its commands.
 * @param page The Playwright page under test.
 * @param row The row whose menu is opened.
 * @param name The login the row is about, which names its trigger.
 * @param command The menu item to choose.
 * @returns Resolves once the command has been chosen.
 */
const chooseRowAction = async (
  page: Page,
  row: Locator,
  name: string,
  command: string,
): Promise<void> => {
  await row.getByRole('button', { name: `Actions for ${name}` }).click()
  // The menu is queried from the PAGE, not from the row: it is rendered into `body` so the table's
  // own horizontal scroll cannot clip it.
  await page.getByRole('menuitem', { name: command }).click()
}

test('the generated SFTP password is shown once and is gone after a reload', async ({ page }) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)

  await page.reload()

  await expect(page.getByTestId('sftp-password')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedSftpPassword)).toHaveCount(0)
})

// The store outlives the page, so a route change while the dialog is open would otherwise leave the
// password in memory for the next visit to render. The back button rather than a sidebar click on
// purpose: the dialog is modal and correctly intercepts every pointer event behind it.
test('the generated password is gone after navigating away and back', async ({ page }) => {
  await openScreen(page, [], [], '/')
  await page.getByRole('navigation').getByRole('link', { name: 'File transfer', exact: true }).click()
  await createLogin(page, 'SFTP', 'web')

  await expect(page.getByTestId('sftp-password')).toBeVisible()

  await page.goBack()
  await page.goForward()

  await expect(page.getByRole('heading', { level: 1, name: 'File transfer' })).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedSftpPassword)).toHaveCount(0)
})

test('the dialog says plainly that the password cannot be shown again', async ({ page }) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('This password is shown once.')
  await expect(dialog).toContainText('it cannot be shown again')
  await expect(dialog).toContainText('reset the password from the list')
})

test('the dialog shows the prefixed login, not the bare name that was typed', async ({ page }) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await expect(page.getByRole('dialog')).toContainText('alice_web')
})

// The regression this exists for: a dialog that helpfully filled in the customer's own site domain
// would send every customer into a certificate mismatch, because one certificate serves the whole
// panel host. The rendered value must be the server's hostname, and the customer's domain must not
// appear at all.
test('the FTPS credential dialog names the panel host and never the customer domain', async ({
  page,
}) => {
  await openScreen(page, [])
  await createLogin(page, 'FTPS', 'upload')

  const dialog = page.getByRole('dialog')
  await expect(page.getByTestId('ftp-credential-host')).toHaveText('panel.example.net')
  await expect(page.getByTestId('ftp-credential-port')).toHaveText('21')
  await expect(dialog).not.toContainText(ALICE.primaryDomain)
  await expect(dialog).toContainText('FTPS — explicit TLS')
  await expect(dialog).toContainText('Connect to this host name exactly as written.')
})

// Both directions. A warning that is always shown is not a warning.
test('the placeholder-certificate warning appears only when the status says the certificate is self-signed', async ({
  page,
}) => {
  const SELF_SIGNED: FtpsStatus = { ...runningFtpsStatus, certificateIsSelfSigned: true }
  const PLACEHOLDER_SENTENCE = "The server's certificate is a placeholder the panel generated"

  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer')
  await createLogin(page, 'FTPS', 'upload')

  await expect(page.getByRole('dialog')).not.toContainText(PLACEHOLDER_SENTENCE)

  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, SELF_SIGNED)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer')
  await createLogin(page, 'FTPS', 'upload')

  await expect(page.getByRole('dialog')).toContainText(PLACEHOLDER_SENTENCE)
})

// A customer cannot read the FTPS status: it carries a host path and is administrator-only. The
// dialog must say that rather than composing a host name of its own — `window.location.host` is the
// tempting wrong answer, and it is the one that produces a certificate mismatch.
test('a caller who may not read the status is told to ask, not given a guessed host', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServerForbidden(page)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer')

  await createLogin(page, 'FTPS', 'upload')

  const dialog = page.getByRole('dialog')
  await expect(page.getByTestId('ftp-credential-host')).toHaveText('—')
  await expect(dialog).toContainText('does not disclose the transfer host to your account')
  await expect(dialog).toContainText('alice_upload')
  await expect(dialog).toContainText(stubbedCreatedFtpPassword)
})

// A form card once clipped its own dropdown, and a plan option rendered but was unclickable, both
// with green tests. A hit test at the control's own centre is what tells those apart.
test('the copy control is genuinely reachable at its own centre, not merely present', async ({
  page,
}) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  const copyButton = page.getByRole('button', { name: /Copy password/ })
  await expect(copyButton).toBeVisible()
  const box = await copyButton.boundingBox()
  expect(box).not.toBeNull()

  const hitsTheButton = await page.evaluate(
    ({ x, y }) => {
      const hit = document.elementFromPoint(x, y)
      return hit !== null && hit.closest('button') !== null
    },
    { x: (box?.x ?? 0) + (box?.width ?? 0) / 2, y: (box?.y ?? 0) + (box?.height ?? 0) / 2 },
  )
  expect(hitsTheButton).toBe(true)
})

test('the copy control puts the password on the clipboard', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await page.getByRole('button', { name: /Copy password/ }).click()

  await expect(page.getByRole('button', { name: /Copied/ })).toBeVisible()
  const clipboard = await page.evaluate(() => {
    return navigator.clipboard.readText()
  })
  expect(clipboard).toEqual(stubbedCreatedSftpPassword)
})

// A browser can refuse the clipboard outright. The dialog must not then report a copy that never
// happened: the operator would close it believing the value is saved, and it would be gone.
test('a blocked clipboard leaves the control unchanged and the password on screen', async ({
  page,
}) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: () => {
          return Promise.reject(new Error('clipboard blocked'))
        },
      },
    })
  })
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await page.getByRole('button', { name: /Copy password/ }).click()

  await expect(page.getByRole('button', { name: /Copied/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Copy password' })).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)
})

// The value cannot be recovered, so a mis-aimed click beside the panel must not destroy it.
test('a click on the backdrop does not close the credential dialog', async ({ page }) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await expect(page.getByRole('dialog')).toBeVisible()
  await page.mouse.click(5, 5)

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)
})

// With `v-if` plus a literal `:open="true"` the modal was created with the prop already true, so its
// open-watcher never ran: focus never entered the dialog and the trap sat inert.
test('opening the credential dialog moves focus into it', async ({ page }) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await expect(page.getByRole('dialog')).toBeVisible()
  const focusIsInsideTheDialog = await page.evaluate(() => {
    const dialog = document.querySelector('[role="dialog"]')
    return (
      dialog !== null && document.activeElement !== null && dialog.contains(document.activeElement)
    )
  })
  expect(focusIsInsideTheDialog).toBe(true)
})

// One keystroke, muscle memory, and nothing to undo it. Every other dialog in the panel still
// closes on Escape — only this one opts out.
test('pressing Escape does not close the credential dialog', async ({ page }) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)
})

test('closing the dialog ends the only showing the password gets', async ({ page }) => {
  await openScreen(page, [])
  await createLogin(page, 'SFTP', 'web')

  await page.getByRole('button', { name: 'Done' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedSftpPassword)).toHaveCount(0)
})

// Reset is the only recovery there is, so it has to be on the screen, it has to work, and on a
// merged table it has to ask the module that owns the row.
test('resetting an SFTP password is offered on the list and shows a new one once', async ({
  page,
}) => {
  await openScreen(page, [WEB], [UPLOAD])

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await chooseRowAction(page, row, 'alice_web', 'Reset password')
  const confirmation = page.getByRole('dialog', { name: 'Reset the password for alice_web' })
  await expect(confirmation).toContainText('The current one stops working immediately.')
  await confirmation.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedResetSftpPassword)
  await expect(page.getByRole('dialog')).toContainText('alice_web')

  await page.reload()
  await expect(page.getByText(stubbedResetSftpPassword)).toHaveCount(0)
})

// The routing half of the merged table: an FTPS row's action must reach the FTPS module. A page
// that sent every row to the SFTP module would answer with the wrong login's password.
test('an FTPS row reset asks the ftp module and shows the FTPS dialog', async ({ page }) => {
  await openScreen(page, [WEB], [UPLOAD])

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/password')) {
      posts.push(new URL(request.url()).pathname)
    }
  })

  const row = page.getByRole('row').filter({ hasText: 'alice_upload' })
  await chooseRowAction(page, row, 'alice_upload', 'Reset password')
  const confirmation = page.getByRole('dialog', { name: 'Reset the password for alice_upload' })
  await confirmation.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByTestId('ftp-credential-host')).toHaveText('panel.example.net')
  expect(posts).toEqual([`/api/v1/ftp-users/${UPLOAD.id}/password`])
})

test('removing a login asks in a dialog, and a dismissal removes nothing', async ({ page }) => {
  await openScreen(page, [WEB])
  let destructiveRequests = 0
  // Registered last, so it wins Playwright's ordering over the fixture's own route: a dismissal
  // that merely leaves the row on screen would also pass against a removal the stub re-listed.
  await page.route(`**/api/v1/sftp-users/${WEB.id}`, async (route) => {
    destructiveRequests += 1
    await route.fulfill({ status: 204, body: '' })
  })

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await chooseRowAction(page, row, 'alice_web', 'Remove')

  const confirmation = page.getByRole('dialog', { name: 'Remove login alice_web' })
  await expect(confirmation).toContainText('nobody can sign in with this name again')
  await expect(confirmation.locator(':focus')).toHaveCount(1)
  await expect(confirmation.getByRole('button', { name: 'Yes, do it' })).not.toBeFocused()

  await confirmation.getByRole('button', { name: 'Cancel' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(destructiveRequests).toBe(0)
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  await expect(row.getByRole('button', { name: 'Actions for alice_web' })).toBeFocused()
})
