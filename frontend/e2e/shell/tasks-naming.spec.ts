import { expect, test } from '@playwright/test'
import { mainNavigation, openFullShell } from '../fixtures/open-full-shell'

// Issue #209, point 4: "Tasks" opened "Background tasks" while cron is "Scheduled tasks" — two
// entries a reader could not tell apart, and one whose label did not match its page.

test('the background-work and cron entries are named as their pages are', async ({ page }) => {
  await openFullShell(page)
  const nav = mainNavigation(page)

  await expect(nav.getByRole('link', { name: 'Tasks', exact: true })).toHaveCount(0)

  await nav.getByRole('link', { name: 'Background tasks', exact: true }).click()
  await expect(page).toHaveURL('/tasks')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Background tasks')

  await nav.getByRole('link', { name: 'Scheduled tasks', exact: true }).click()
  await expect(page).toHaveURL('/cron')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Scheduled tasks')
})
