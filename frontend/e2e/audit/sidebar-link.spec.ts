import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'

/**
 * The measured defect: `/settings/audit` was routed, rendered and tested, and no LINK in the
 * shell led to it — the sidebar had no entry and nothing else rendered an `<a>` to it, so a
 * person clicking could never arrive. These specs observe the RENDERED sidebar link, which is
 * the axis the static reachability gate declares it cannot see (an entry built but never
 * returned looks identical to it).
 */

/**
 * Signs a person in with the given role, in Russian, with the shell's endpoints answered.
 * @param page The Playwright page whose network the routes are installed on.
 * @param role The role the panel reports for the signed-in user.
 * @returns Resolves once the routes are installed.
 */
const stubRussianShell = async (page: Page, role: 'admin' | 'customer'): Promise<void> => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page, { ...stubbedAdministrator, role })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
}

test('an administrator reaches the journal from the sidebar by clicking its link', async ({ page }) => {
  await stubRussianShell(page, 'admin')

  await page.goto('/')

  // A real link, not a programmatic push: an <a> the browser can middle-click and a crawler of
  // clicks can find — asserted by VALUE in the non-English locale the defect was measured in.
  const link = page.getByRole('navigation', { name: 'Основная навигация' }).getByRole('link', { name: 'Журнал аудита' })
  await expect(link).toBeVisible()
  await expect(link).toHaveAttribute('href', '/settings/audit')

  await link.click()

  await expect(page).toHaveURL('/settings/audit')
  await expect(page.getByText('Журнал аудита').first()).toBeVisible()
})

test('a customer is not offered the journal link in the sidebar', async ({ page }) => {
  // Presentation, not authorization: the endpoint refuses a customer whatever the sidebar shows,
  // and a link that only ever answers 403 is a worse answer than no link. Any role the SPA does
  // not recognise keeps the link — the check errs permissive by design.
  await stubRussianShell(page, 'customer')

  await page.goto('/')

  const navigation = page.getByRole('navigation', { name: 'Основная навигация' })
  // Positive control on the same locator scope: the sidebar rendered its entries, so an empty
  // navigation cannot be what passes the absence assertion below.
  await expect(navigation.getByRole('link', { name: 'Состояние системы' })).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'Журнал аудита' })).toHaveCount(0)
})
