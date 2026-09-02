import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import {
  stubSftpUserActions,
  stubSftpUsers,
  stubbedCreatedSftpPassword,
  stubbedResetSftpPassword,
} from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

const LICENSED: PanelModule[] = [{ name: 'sftp', displayName: 'SFTP', tier: 'included', isEnabled: true }]

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

/** Puts the screen in front of an operator, with whatever logins the case needs already created. */
const openScreen = async (page: Page, users: SftpUser[], startAt = '/sftp-users'): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubSftpUsers(page, users)
  await stubSftpUserActions(page, WEB.fullName)
  await page.goto(startAt)
}

/** Fills the create form and submits it, which is the path that mints a password. */
const createSftpUser = async (page: Page): Promise<void> => {
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alice/ }).click()
  await page.getByRole('textbox', { name: 'Login name' }).fill('web')
  await page.getByRole('button', { name: 'Create login' }).click()
}

/**
 * Opens a row's actions menu and chooses one of its commands.
 *
 * The commands moved out of the row and behind a trigger, so a test that
 * clicked a button in the row now has to open the menu first. Written as a
 * helper rather than repeated, because every one of these tests reaches its
 * command the same way, and the next one should not have to rediscover it.
 */
const chooseRowAction = async (page: Page, row: Locator, name: string, command: string): Promise<void> => {
  await row.getByRole('button', { name: `Actions for ${name}` }).click()
  // The menu is queried from the PAGE, not from the row: it is rendered into
  // `body` so that the table's own horizontal scroll cannot clip it, which
  // means it is not a descendant of the row that opened it.
  await page.getByRole('menuitem', { name: command }).click()
}

test('the generated password is shown once and is gone after a reload', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)

  await page.reload()

  await expect(page.getByTestId('sftp-password')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedSftpPassword)).toHaveCount(0)
})

// The store outlives the page, so a route change that happens while the dialog is open — the back
// button here, an expired session redirecting to the sign-in screen in the field — would otherwise
// leave the password in memory for the next visit to this screen to render.
//
// The back button rather than a sidebar click on purpose: the dialog is modal, so it correctly
// intercepts every pointer event on the shell behind it, and a click could not reach the sidebar.
test('the generated password is gone after navigating away and back', async ({ page }) => {
  await openScreen(page, [], '/')
  await page.getByRole('navigation').getByRole('link', { name: 'SFTP' }).click()
  await createSftpUser(page)

  await expect(page.getByTestId('sftp-password')).toBeVisible()

  await page.goBack()
  await page.goForward()

  await expect(page.getByRole('heading', { level: 1, name: 'SFTP' })).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedSftpPassword)).toHaveCount(0)
})

test('the dialog says plainly that the password cannot be shown again', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('This password is shown once.')
  await expect(dialog).toContainText('it cannot be shown again')
  await expect(dialog).toContainText('reset the password from the list')
})

test('the dialog shows the prefixed login, not the bare name that was typed', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

  await expect(page.getByRole('dialog')).toContainText('alice_web')
})

// A form card once clipped its own dropdown, and a plan option rendered but was unclickable, both
// with green tests. A hit test at the control's own centre is what tells those apart.
test('the copy control is genuinely reachable at its own centre, not merely present', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

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
  await createSftpUser(page)

  await page.getByRole('button', { name: /Copy password/ }).click()

  await expect(page.getByRole('button', { name: /Copied/ })).toBeVisible()
  const clipboard = await page.evaluate(() => {
    return navigator.clipboard.readText()
  })
  expect(clipboard).toEqual(stubbedCreatedSftpPassword)
})

// A browser can refuse the clipboard outright. The dialog must not then report a copy that never
// happened: the operator would close it believing the value is saved, and it would be gone.
test('a blocked clipboard leaves the control unchanged and the password on screen', async ({ page }) => {
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
  await createSftpUser(page)

  await page.getByRole('button', { name: /Copy password/ }).click()

  await expect(page.getByRole('button', { name: /Copied/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Copy password' })).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)
})

// The value cannot be recovered, so a mis-aimed click beside the panel must not destroy it.
test('a click on the backdrop does not close the credential dialog', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

  await expect(page.getByRole('dialog')).toBeVisible()
  await page.mouse.click(5, 5)

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)
})

// The defect underneath the Escape one, and the reason that mutant first survived: with `v-if`
// plus a literal `:open="true"` the modal was created with the prop already true, so its
// open-watcher never ran and focus never entered the dialog. Escape went nowhere, the focus trap
// was inert, and a keyboard user was left outside a dialog that looked perfectly correct.
test('opening the credential dialog moves focus into it', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

  await expect(page.getByRole('dialog')).toBeVisible()
  const focusIsInsideTheDialog = await page.evaluate(() => {
    const dialog = document.querySelector('[role="dialog"]')
    return dialog !== null && document.activeElement !== null && dialog.contains(document.activeElement)
  })
  expect(focusIsInsideTheDialog).toBe(true)
})

// The likelier of the two accidents: one keystroke, muscle memory, and nothing to undo it.
// Every other dialog in the panel still closes on Escape — only this one opts out.
test('pressing Escape does not close the credential dialog', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedCreatedSftpPassword)
})

test('closing the dialog ends the only showing the password gets', async ({ page }) => {
  await openScreen(page, [])
  await createSftpUser(page)

  await page.getByRole('button', { name: 'Done' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByText(stubbedCreatedSftpPassword)).toHaveCount(0)
})

// Reset is the only recovery there is, so it has to be on the screen and it has to work.
test('resetting the password is offered on the list and shows a new one once', async ({ page }) => {
  await openScreen(page, [WEB])

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await chooseRowAction(page, row, 'alice_web', 'Reset password')
  await expect(row).toContainText('The current one stops working immediately.')
  await row.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByTestId('sftp-password')).toHaveText(stubbedResetSftpPassword)
  await expect(page.getByRole('dialog')).toContainText('alice_web')

  await page.reload()
  await expect(page.getByText(stubbedResetSftpPassword)).toHaveCount(0)
})

test('removing a login asks for confirmation before it is done', async ({ page }) => {
  await openScreen(page, [WEB])

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await chooseRowAction(page, row, 'alice_web', 'Remove')
  await expect(row).toContainText('nobody can sign in with this name again')

  await row.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
})
