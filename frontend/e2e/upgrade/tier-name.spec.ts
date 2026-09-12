import { expect, test } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import type { PanelModule } from '../../src/types/module'

// The defect this file pins, measured on a Russian panel: the upgrade screen read
// `t('app.upgrade.tier', { tier: entry.tier })` and printed "Он доступен в тарифе addOn." — the
// contract constant verbatim inside a translated sentence. The tier's words now travel from the
// backend as `tierDisplayName` beside the machine `tier`, and these specs assert the exact
// sentence in a NON-English locale, because a Russian operator reading an English constant is the
// whole of what was broken.

/** A locked module whose tier the panel named in Russian, as the backend does under `Accept-Language: ru`. */
const LOCKED_IN_RUSSIAN: PanelModule[] = [
  {
    name: 'backups',
    displayName: 'Резервные копии',
    tier: 'addOn',
    tierDisplayName: 'Дополнительный модуль',
    isEnabled: false,
  },
]

test('the upgrade page names the licence tier in Russian and never prints the wire constant', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LOCKED_IN_RUSSIAN)

  await page.goto('/upgrade/backups')

  await expect(page.getByText('Тариф лицензии: Дополнительный модуль.')).toBeVisible()
  await expect(page.getByText('Модуль «Резервные копии» не входит в вашу текущую лицензию.')).toBeVisible()

  // Positive control on the probe: `tierDisplayName` IS rendered above, so the same body-text probe
  // that must NOT find `addOn` is proved able to find something the page really carries. Without
  // it, a page that had stopped rendering the description at all would report the same silence as
  // a page that renders it correctly.
  await expect(page.locator('body')).toContainText('Дополнительный модуль')
  await expect(page.locator('body')).not.toContainText('addOn')
})

test('a catalogue that names no tier shows no tier sentence rather than the constant', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  // A panel older than `tierDisplayName` answers exactly like this. The screen must degrade to
  // saying nothing about the tier — showing the machine value would be the original defect again.
  await stubModules(page, [{ name: 'backups', displayName: 'Резервные копии', tier: 'planGated', isEnabled: false }])

  await page.goto('/upgrade/backups')

  await expect(page.getByText('Модуль «Резервные копии» не входит в вашу текущую лицензию.')).toBeVisible()
  await expect(page.locator('body')).not.toContainText('planGated')
  await expect(page.locator('body')).not.toContainText('Тариф лицензии')
})

test('the English upgrade page names the tier in the backend words it was given', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'backups', displayName: 'Backups', tier: 'addOn', tierDisplayName: 'Add-on', isEnabled: false },
  ])

  await page.goto('/upgrade/backups')

  await expect(page.getByText('Licence tier: Add-on.')).toBeVisible()
  await expect(page.getByText('The "Backups" module is not included in your current licence.')).toBeVisible()
})
