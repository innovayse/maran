import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import { stubPhpVersions, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

// Every select in the panel that opens with nothing chosen is asserted here on ITS EXACT TRIGGER
// TEXT, not on presence. The defect this file exists for rendered `00000000-0000-0000-0000-
// 000000000000` on the plan chooser — a nil GUID left behind by an older free-text field, offered
// to `UiSelect` as its placeholder — and every "the select renders" assertion in the suite passed
// while it was on screen. `toHaveText` with the whole string is what can see it; `toContainText`
// or a visibility check cannot.

/** An account for the pickers that offer one. */
const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Installs the session, health and module catalogue every screen here needs.
 * @param page The Playwright page whose network the routes are installed on.
 * @param modules The catalogue that licenses the screen under test.
 * @returns Resolves once every route is installed.
 */
const stubShell = async (page: Page, modules: PanelModule[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, modules)
}

test('the plan chooser prompts for a choice instead of showing an identifier', async ({ page }) => {
  await stubShell(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubAccounts(page, [])
  await stubPlans(page)

  await page.goto('/accounts/new')

  // Positive control: the probe is looking at the create-account form, so a blank page or a
  // redirect cannot pass this spec by having nothing to find.
  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()

  const chooser = page.getByRole('combobox', { name: 'Plan ID' })
  await expect(chooser).toHaveText('Choose a plan')
  // Named separately from the text assertion above, because this is the regression itself: the
  // form must not offer a nil GUID anywhere on the screen, opened or closed.
  await expect(page.getByText('00000000-0000-0000-0000-000000000000')).toHaveCount(0)
})

test('the account pickers of every create form prompt for a choice instead of standing blank', async ({
  page,
}) => {
  await stubShell(page, [
    { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
    { name: 'sftp', displayName: 'SFTP', tier: 'included', isEnabled: true },
    { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
    { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  ])
  await stubAccounts(page, [ALICE])
  await stubDatabases(page, [])
  await stubSftpUsers(page, [])
  await stubBackups(page, [])
  await stubSites(page, [])
  await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])

  for (const path of ['/databases', '/sftp-users', '/backups', '/sites/new']) {
    await page.goto(path)

    const picker = page.getByRole('combobox', { name: 'Account' })
    // Positive control per screen: the picker is there to be read before its text is judged, so a
    // screen that failed to render cannot pass the assertion below by matching nothing.
    await expect(picker, `no account picker on ${path}`).toBeVisible()
    await expect(picker, `wrong empty state on ${path}`).toHaveText('Choose an account')
  }
})

test('the PHP version picker prompts for a choice instead of standing blank', async ({ page }) => {
  await stubShell(page, [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }])
  await stubAccounts(page, [ALICE])
  await stubSites(page, [])
  await stubPhpVersions(page, [
    { version: '8.2', isDefault: false },
    { version: '8.3', isDefault: true },
  ])

  await page.goto('/sites/new')

  await page.getByRole('combobox', { name: 'Backend' }).click()
  await page.getByRole('option', { name: 'PHP', exact: true }).click()

  const picker = page.getByRole('combobox', { name: 'PHP version' })
  // Positive control: the field only exists once the backend is PHP, so seeing it proves the
  // click landed and the assertion below is reading the real control.
  await expect(picker).toBeVisible()
  await expect(picker).toHaveText('Choose a PHP version')
})
