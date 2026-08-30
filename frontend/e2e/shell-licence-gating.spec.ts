import { expect, test } from '@playwright/test'
import { stubModules } from './fixtures/stub-modules-route'
import { stubHealthy } from './fixtures/stub-health-route'
import { stubAccounts } from './fixtures/stub-accounts-route'

// Both halves of licence gating are exercised here: the navigation (a locked
// module stays visible but marked, an enabled one links to its own page) and
// the router guard, which is what a deep link, a bookmark or a typed URL
// passes through. `/accounts` carries `meta.module: 'accounts'`, so it is a
// real gated route rather than a fixture invented for the test.

test('navigation shows an enabled module and marks a disabled module as locked', async ({ page }) => {
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
    { name: 'backups', displayName: 'Backups', tier: 'business', isEnabled: false },
  ])

  await page.goto('/')

  // Module labels come from the panel, already localized (the stub supplies displayName); the SPA
  // never owns translations for modules it learns about at runtime.
  const enabledLink = page.getByRole('link', { name: 'Sites' })
  await expect(enabledLink).toBeVisible()
  await expect(enabledLink).not.toHaveAttribute('aria-disabled', 'true')

  const lockedLink = page.getByRole('link', { name: /Backups/ })
  await expect(lockedLink).toBeVisible()
  await expect(lockedLink).toHaveAttribute('aria-disabled', 'true')
  await expect(lockedLink).toContainText('Locked')
  await expect(lockedLink).toHaveAttribute('href', '/upgrade/backups')
})

test('visiting the upgrade page for a locked module names the module and its licence tier', async ({ page }) => {
  await stubHealthy(page)
  await stubModules(page, [{ name: 'backups', tier: 'business', isEnabled: false }])

  await page.goto('/upgrade/backups')

  await expect(page.getByRole('heading', { level: 1, name: 'Upgrade required' })).toBeVisible()
  await expect(page.getByText('The "backups" module is not included in your current licence.')).toBeVisible()
  await expect(page.getByText('It is available on the business tier.')).toBeVisible()
})

test('deep link to a gated route whose module the licence does not cover lands on the upgrade page', async ({
  page,
}) => {
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'business', isEnabled: false }])

  await page.goto('/accounts')

  await expect(page).toHaveURL('/upgrade/accounts')
  await expect(page.getByRole('heading', { level: 1, name: 'Upgrade required' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toHaveCount(0)
})

test('deep link to a gated child route of an unlicensed module is redirected too', async ({ page }) => {
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'business', isEnabled: false }])

  await page.goto('/accounts/new')

  await expect(page).toHaveURL('/upgrade/accounts')
})

test('a gated route the licence covers is reachable and renders its own page', async ({ page }) => {
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubAccounts(page, [])

  await page.goto('/accounts')

  await expect(page).toHaveURL('/accounts')
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible()
})

test('an unknown module the panel never reported is treated as unlicensed by the guard', async ({ page }) => {
  await stubHealthy(page)
  await stubModules(page, [])

  await page.goto('/accounts')

  await expect(page).toHaveURL('/upgrade/accounts')
})

test('navigating from the sidebar into an enabled module reaches its page rather than the upgrade prompt', async ({
  page,
}) => {
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await stubAccounts(page, [])

  await page.goto('/')
  await page.getByRole('link', { name: 'Accounts' }).click()

  await expect(page).toHaveURL('/accounts')
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible()
})
