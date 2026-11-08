import { expect, test } from '@playwright/test'
import { stubModules, stubModulesUnavailable } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

// Both halves of licence gating are exercised here: the navigation (a locked
// module stays visible but marked, an enabled one links to its own page) and
// the router guard, which is what a deep link, a bookmark or a typed URL
// passes through. `/accounts` carries `meta.module: 'accounts'`, so it is a
// real gated route rather than a fixture invented for the test.

test('navigation shows an enabled module and marks a disabled module as locked', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'backups', displayName: 'Backups', kindDisplayName: 'Core', tier: 'business', state: 'locked' },
  ])

  await page.goto('/')

  // The sidebar names the navigation tree's sections (issue #311), not the catalogue's modules.
  // Scoped to the sidebar: the dashboard's own "Sites" card is a link to the same page. Backups has
  // no usable page left, so its section is one locked row rather than a submenu of locked doors.
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const enabledLink = nav.getByRole('link', { name: 'Sites and domains', exact: true })
  await expect(enabledLink).toBeVisible()
  await expect(enabledLink).not.toHaveAttribute('aria-disabled', 'true')

  const lockedLink = nav.getByRole('link', { name: /Backups/ })
  await expect(lockedLink).toBeVisible()
  await expect(lockedLink).toHaveAttribute('aria-disabled', 'true')
  await expect(lockedLink).toContainText('Locked')
  await expect(lockedLink).toHaveAttribute('href', '/upgrade/backups')
})

test('visiting the upgrade page for a locked module names the module and its licence tier', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  // `tierDisplayName` is the tier in the backend's own words; the SPA owns no list of tiers, so a
  // catalogue without it makes the page say nothing about the tier (e2e/upgrade/tier-name.spec.ts).
  await stubModules(page, [
    { name: 'backups', kindDisplayName: 'Core', tier: 'business', tierDisplayName: 'Business', state: 'locked' },
  ])

  await page.goto('/upgrade/backups')

  await expect(page.getByRole('heading', { level: 1, name: 'Upgrade required' })).toBeVisible()
  // A catalogue entry without `displayName` must not put the machine id on the screen (#218).
  await expect(page.getByText('This module is not included in your current licence.')).toBeVisible()
  await expect(page.locator('body')).not.toContainText('"backups"')
  // `exact: true`, and the reason is the whole of what this line asserts. `getByText` matches a
  // SUBSTRING, CASE-INSENSITIVELY, unless told otherwise — so the defect this line exists to catch,
  // the wire constant `business` reaching the screen where `tierDisplayName` belongs, rendered
  // "Licence tier: business." and matched the loose form perfectly. The line read as a locale check
  // and could not see a locale defect (rules/testing.md: a check must be able to observe what it
  // reports on). Exact matching is case-sensitive and reads the element's whole text, which is the
  // sentence `UiEmptyState` renders in its own `<p>`.
  await expect(page.getByText('Licence tier: Business.', { exact: true })).toBeVisible()
})

test('deep link to a gated route whose module the licence does not cover lands on the upgrade page', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'business', state: 'locked' }])

  await page.goto('/accounts')

  await expect(page).toHaveURL('/upgrade/accounts')
  await expect(page.getByRole('heading', { level: 1, name: 'Upgrade required' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toHaveCount(0)
})

test('deep link to a gated child route of an unlicensed module is redirected too', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'business', state: 'locked' }])

  await page.goto('/accounts/new')

  await expect(page).toHaveURL('/upgrade/accounts')
})

test('a gated route the licence covers is reachable and renders its own page', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubAccounts(page, [])

  await page.goto('/accounts')

  await expect(page).toHaveURL('/accounts')
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible()
})

test('an unknown module the panel never reported is treated as unlicensed by the guard', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [])

  await page.goto('/accounts')

  await expect(page).toHaveURL('/upgrade/accounts')
})

test('navigating from the sidebar into an enabled module reaches its page rather than the upgrade prompt', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }])
  await stubAccounts(page, [])

  await page.goto('/')
  // The sidebar's link, by its landmark: the landing page draws an "Accounts" figure tile of its own,
  // a link to the same place, from the first frame while the dashboard is read.
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Accounts' }).click()

  await expect(page).toHaveURL('/accounts')
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible()
})

test('a licensed module whose screen exists is not sent to the upgrade wall', async ({ page }) => {
  // `GET /api/v1/modules` reports identity and ssl as included and enabled, and the sidebar linked
  // both to `/upgrade/<name>` — an operator being told to buy something the licence already
  // includes. The cause was the sidebar guessing that a module named `x` has a route named `x`;
  // where a module's screen lives is now stated in the SPA's own router map. Since issue #311 the
  // identity module's screen is the signed-in person's own, reached from the account menu.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'identity', displayName: 'Users and access', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  ])

  await page.goto('/')

  // Named for where it leads, not for the module (issue #209): "Users and access" promised user
  // management the identity module does not have, and nothing in the sidebar walls it off.
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(nav.locator('[data-nav-section]').first()).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Users and access' })).toHaveCount(0)
  await expect(nav.locator('a[href="/upgrade/identity"]')).toHaveCount(0)

  // Reachability, not presence: the menu item is followed and the screen it names is the one that
  // arrives. An item that merely is not `/upgrade/...` could still be a route nothing serves.
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Sessions' }).click()
  await expect(page).toHaveURL('/settings/sessions')
  await expect(page.getByRole('heading', { level: 1, name: 'Signed-in devices' })).toBeVisible()
})

test('a licensed module whose interface lives inside another screen gets no sidebar entry', async ({
  page,
}) => {
  // SSL is that module: a certificate belongs to a site, so its interface is a tab on the site it
  // protects. There is nowhere for a sidebar entry to lead, and the entry it used to have led to
  // an upgrade wall for a feature that exists, works, and was serving the operator's traffic.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    { name: 'ssl', displayName: 'SSL certificates', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  ])

  await page.goto('/')

  // Scoped to the sidebar: the dashboard's own Sites tile is also a link named "Sites", and which
  // of the two has rendered when this asserts is a race the claim is not about.
  const sidebar = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(sidebar.getByRole('link', { name: 'Sites' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'SSL certificates' })).toHaveCount(0)
})

test('a module the licence does not permit still shows inside its section and leads to the upgrade page', async ({
  page,
}) => {
  // The upgrade page keeps the job it is honest for: a locked module's existence is still worth
  // showing, which is what the licence-gating rule asks for. The firewall's pages share the
  // Security section with the security policy, which needs no module, so the section stays a
  // submenu and the firewall's pages are drawn in it, locked.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'firewall', displayName: 'Firewall', kindDisplayName: 'Core', tier: 'business', state: 'locked' },
  ])

  await page.goto('/')

  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('button', { name: 'Show the Security pages' }).click()
  const locked = nav.getByRole('link', { name: /^Bans/ })
  await expect(locked).toBeVisible()
  await expect(locked).toContainText('Locked')
  await expect(locked).toHaveAttribute('href', '/upgrade/firewall')
  await expect(nav.getByRole('link', { name: 'Security policy', exact: true })).toHaveAttribute(
    'href',
    '/settings/security-policy',
  )
})

test('a gated route stays reachable when the module catalogue could not be loaded', async ({ page }) => {
  // The SPA's licence gate is cosmetic; the backend checks the licence on every request. So when
  // the catalogue call fails the guard must step aside, not refuse. It used to refuse: an empty
  // catalogue and an unloadable one were the same state, so one 500 sent EVERY gated route to the
  // upgrade wall and told a fully licensed operator to buy back what they already own.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModulesUnavailable(page)
  await stubAccounts(page, [])

  await page.goto('/accounts')

  await expect(page).toHaveURL('/accounts')
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible()
})
