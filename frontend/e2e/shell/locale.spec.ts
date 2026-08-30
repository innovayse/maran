import { expect, test } from '@playwright/test'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'

test('shell renders the status heading in English when English is the persisted locale', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'en')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()
})

test('shell renders the status heading in Russian when Russian is the persisted locale', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'ru')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'Состояние системы' })).toBeVisible()
})

test('shell renders the status heading in Armenian when Armenian is the persisted locale', async ({
  page,
}) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'hy')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'Համակարգի կարգավիճակ' })).toBeVisible()
})

test('shell renders the navigation aria label in the persisted locale', async ({ page }) => {
  await stubSignedIn(page)
  await setPersistedLocale(page, 'hy')
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/')

  await expect(page.getByRole('navigation', { name: 'Հիմնական նավիգացիա' })).toBeVisible()
})
