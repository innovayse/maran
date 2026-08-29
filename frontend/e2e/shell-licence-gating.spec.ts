import { expect, test } from '@playwright/test'
import { stubModules } from './fixtures/stub-modules-route'
import { stubHealthy } from './fixtures/stub-health-route'

// No route in the shell yet carries `meta.module` (the router guard is
// wired but nothing exercises it in production routes today — see
// src/router/index.ts), so the redirect-on-deep-link half of licence
// gating cannot be driven through a real gated route without adding one,
// which is out of scope for this pass (production code is not to be
// touched to make a spec pass). This file therefore covers what the shell
// does today: the navigation rendering both an enabled and a locked module,
// and the upgrade page rendering correctly when reached directly.

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
