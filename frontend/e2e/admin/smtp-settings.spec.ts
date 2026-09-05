import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { storedSmtpSettings, stubSmtpSettings, stubTestMailRefused } from '../fixtures/stub-smtp-routes'

/**
 * A value no part of the screen may put on screen. It is sent in a field the real
 * `SmtpSettingsDto` does not have, which is what gives this spec something to
 * catch: a form that filled its password field from the response — or a store that
 * kept whatever the panel sent — would show it, and this assertion would go red.
 */
const PROVIDER_SECRET = 'provider-secret-9f2b41'

test('the mail settings form shows that a password is saved without ever rendering one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSmtpSettings(page, { ...storedSmtpSettings, password: PROVIDER_SECRET })

  await page.goto('/settings/smtp')

  // The hint stands in for the value, which is the whole of what the screen is
  // allowed to know about it.
  await expect(page.getByText('A password is saved. Type one here only to replace it.')).toBeVisible()

  // The field is empty: what is typed here REPLACES the stored password, and an
  // empty field is how "leave it alone" is expressed.
  await expect(page.getByLabel('Password', { exact: true })).toHaveValue('')

  // Nowhere on the page, in any element, in either the masked or revealed state.
  await expect(page.locator('body')).not.toContainText(PROVIDER_SECRET)
  await page.getByRole('button', { name: 'Show' }).first().click()
  await expect(page.locator('body')).not.toContainText(PROVIDER_SECRET)
})

test('a refused test message shows the mail server’s own words as the panel relayed them', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubSmtpSettings(page, storedSmtpSettings)
  await stubTestMailRefused(page, 'The mail server refused the credentials: 535 5.7.8 Authentication failed.')

  await page.goto('/settings/smtp')
  // Wait for the settings the panel reported to be on screen before touching the
  // test card: both cards render only once the read has answered, and reaching for
  // a field while the read is still in flight times out on a slow machine without
  // saying anything about the behaviour under test.
  await expect(page.getByLabel('Mail server')).toHaveValue('smtp.example.net')
  await page.getByLabel('Send the test to').fill('ops@example.net')
  await page.getByRole('button', { name: 'Send test message' }).click()

  // Verbatim: the SPA owns no error text, and a generic "sending failed" would hide
  // the one sentence that says which setting is wrong (rules/vue.md).
  await expect(
    page.getByText('The mail server refused the credentials: 535 5.7.8 Authentication failed.'),
  ).toBeVisible()
})
