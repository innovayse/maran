import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'

// `UiModal` gained a `dismissible` prop so the credential dialogs could refuse Escape, and the
// default had to stay exactly what it was for every other dialog in the panel. Nothing pinned that
// default before: a mutant flipping it to `false` would have made every modal in the SPA
// undismissable by keyboard with the whole suite still green. This is the pin.
test('the command palette still closes on Escape, which is the kit default for every dialog', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')
  await page.getByRole('button', { name: 'Search or jump to…' }).click()

  const palette = page.getByRole('dialog')
  await expect(palette).toBeVisible()

  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toHaveCount(0)
})
