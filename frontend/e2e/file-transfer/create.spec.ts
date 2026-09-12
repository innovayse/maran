import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import {
  disabledFtpsStatus,
  runningFtpsStatus,
  stubCreateFtpUserProblem,
  stubFtpUsers,
  stubFtpsServer,
} from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCreateSftpUserProblem, stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
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

/** The sentence the form shows when the panel has said the daemon is off. */
const FTPS_OFF_NOTE =
  'FTPS is turned off on this server, so no FTPS login can be created yet. An administrator turns the service on; SFTP is unaffected.'

/**
 * Opens the screen with both lists empty and the daemon in the given state.
 * @param page The Playwright page under test.
 * @param status The FTPS daemon state the panel reports.
 * @returns Resolves once the screen has been opened.
 */
const openScreen = async (page: Page, status: FtpsStatus = runningFtpsStatus): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, status)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])
  await page.goto('/file-transfer')
}

/**
 * Fills the create form's three fields.
 * @param page The Playwright page under test.
 * @param protocol The protocol label to choose.
 * @param name The login suffix to type.
 * @returns Resolves once the form is filled.
 */
const fillForm = async (page: Page, protocol: string, name: string): Promise<void> => {
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  // The radio input itself is `sr-only`, so a direct click lands on a 1px element the document
  // intercepts. Clicking the visible LABEL is what a person does, and it exercises the `for`/`id`
  // binding the kit's accessibility rules require. The state is then asserted, because a click that
  // silently missed would otherwise leave the default protocol selected and the spec still green.
  await page.getByRole('radiogroup').getByText(protocol, { exact: true }).click()
  await expect(page.getByRole('radio', { name: protocol, exact: true })).toBeChecked()
  await page.getByRole('textbox', { name: 'Login name' }).fill(name)
}

// The client rules mirror the server's validator. They are advice that saves a round trip, and the
// check that matters is that the round trip really is saved.
test('the create form refuses a login name the panel can reject without asking the server', async ({
  page,
}) => {
  await openScreen(page)

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && /\/api\/v1\/(s?ftp)-users/.test(request.url())) {
      posts.push(request.url())
    }
  })

  // An underscore is refused on purpose: account names may contain one, so a suffix that could hold
  // one would let `alice` ask for `bob_deploy` and be handed a login that reads as `bob`'s.
  await fillForm(page, 'SFTP', 'bob_deploy')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(
    page.getByText('Use lowercase letters and digits only, up to 30 characters.'),
  ).toBeVisible()
  expect(posts).toEqual([])
})

test('the create form refuses an empty login name and never asks the server about it', async ({
  page,
}) => {
  await openScreen(page)

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && /\/api\/v1\/(s?ftp)-users/.test(request.url())) {
      posts.push(request.url())
    }
  })

  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByText('Choose the account that will own the login.')).toBeVisible()
  await expect(page.getByText('Login name is required.')).toBeVisible()
  expect(posts).toEqual([])
})

// The protocol is a routing decision made here and sent nowhere: each module's own request body
// carries only the account and the name.
test('choosing SFTP posts to the sftp module and adds the row it answered with', async ({ page }) => {
  await openScreen(page)

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('-users')) {
      posts.push(new URL(request.url()).pathname)
    }
  })

  await fillForm(page, 'SFTP', 'web')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByRole('dialog')).toContainText('alice_web')
  expect(posts).toEqual(['/api/v1/sftp-users'])
})

test('choosing FTPS posts to the ftp module and adds the row it answered with', async ({ page }) => {
  await openScreen(page)

  const posts: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('-users')) {
      posts.push(new URL(request.url()).pathname)
    }
  })

  await fillForm(page, 'FTPS', 'upload')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByRole('dialog')).toContainText('alice_upload')
  expect(posts).toEqual(['/api/v1/ftp-users'])
})

// A disabled daemon is a normal state, not an error. The choice stays visible and says why it
// cannot be taken; nothing on the screen reports a failure.
test('FTPS cannot be chosen while the server has it turned off, and says why', async ({ page }) => {
  await openScreen(page, disabledFtpsStatus)

  await expect(page.getByRole('radio', { name: 'FTPS', exact: true })).toBeDisabled()
  await expect(page.getByRole('radio', { name: 'SFTP', exact: true })).toBeEnabled()
  await expect(page.getByTestId('ftps-off-note')).toHaveText(FTPS_OFF_NOTE)
})

// The other direction, because a note that is always shown is not a note.
test('FTPS is choosable and unexplained while the server has it turned on', async ({ page }) => {
  await openScreen(page, runningFtpsStatus)

  await expect(page.getByRole('radio', { name: 'FTPS', exact: true })).toBeEnabled()
  await expect(page.getByTestId('ftps-off-note')).toHaveCount(0)
})

// rules/vue.md: the SPA never invents an error message for a server outcome.
test('a rejected SFTP create renders the backend own message rather than frontend copy', async ({
  page,
}) => {
  const backendDetail = 'A login with that name already exists for this account.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubFtpUsers(page, [])
  await stubCreateSftpUserProblem(page, backendDetail)
  await page.goto('/file-transfer')

  await fillForm(page, 'SFTP', 'web')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
})

// The refusal case. When the agent refuses because the account is busy, the wire carries nothing
// that distinguishes it from a host failure — so the screen renders the server's sentence and
// invents no verdict of its own.
test('a refused FTPS create renders the backend own sentence and no verdict of its own', async ({
  page,
}) => {
  const backendDetail = 'The account is busy with another operation. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubCreateFtpUserProblem(page, backendDetail)
  await page.goto('/file-transfer')

  await fillForm(page, 'FTPS', 'upload')
  await page.getByRole('button', { name: 'Create login' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})
