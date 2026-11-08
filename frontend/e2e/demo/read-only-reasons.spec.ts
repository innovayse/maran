import { expect, test } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { signInAs } from './sign-in-as'

// The areas the demo refuses to change (issue #95) say why, in the visitor's language, with the
// sentence the backend gives. Run only through playwright.demo.config.ts against a running demo.

/** The generic sentence the notice falls back to when the backend gave no reason, per language. */
const GENERIC_SENTENCE = {
  en: 'This setting cannot be changed in the demo.',
  ru: 'В демо эту настройку изменить нельзя.',
  hy: 'Դեմոյում այս կարգավորումը հնարավոր չէ փոխել։',
} as const

/** A read-only screen: where it lives. */
const READ_ONLY_PATHS: readonly string[] = [
  '/settings/security-policy',
  '/settings/smtp',
  '/settings/panel-certificate',
]

for (const locale of ['en', 'ru', 'hy'] as const) {
  for (const path of READ_ONLY_PATHS) {
    test(`${path} explains with the backend's own reason in ${locale}`, async ({ page }) => {
      await setPersistedLocale(page, locale)
      await signInAs(page, 'administrator')
      await page.goto(path)

      const note = page.getByTestId('demo-read-only-note')
      await expect(note).toBeVisible()
      // The generic sentence means the status held no reason for the area: the notice would then
      // be the SPA speaking for the backend, which is exactly what it must not do.
      await expect(note).not.toContainText(GENERIC_SENTENCE[locale])
    })
  }
}

test('saving the security policy is refused with a visible error and the policy stays unsaved', async ({ page }) => {
  await signInAs(page, 'administrator')
  await page.goto('/settings/security-policy')
  const main = page.getByRole('main')
  await expect(page.getByTestId('demo-read-only-note')).toBeVisible()

  await main.getByRole('button', { name: 'Save policy' }).click()

  // The note is one status region; the refusal the backend answers with is a second.
  await expect.poll(async () => {
    return main.getByRole('status').count()
  }).toBeGreaterThanOrEqual(2)
  await expect(main.getByText('The policy has been saved.')).toHaveCount(0)
})
