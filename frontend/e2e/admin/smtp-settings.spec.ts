import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubHealthy } from '../fixtures/stub-health-route'
import { storedSmtpSettings, stubSmtpSettings, stubTestMailRefused } from '../fixtures/stub-smtp-routes'

/**
 * A password the administrator types in this spec. It is the positive control of the assertions
 * below: the field is shown to hold a typed value once revealed, so the empty field a stored
 * password produces is proof the form was not filled, not a probe that cannot see a value.
 */
const TYPED_PASSWORD = 'typed-replacement-7c41d9'

test('the mail settings form shows that a password is saved without ever rendering one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  // The contract-shaped body: `SmtpSettingsDto` has `hasPassword` and no field that could carry the value.
  await stubSmtpSettings(page, storedSmtpSettings)

  await page.goto('/settings/smtp')

  // The hint stands in for the value, which is the whole of what the screen is
  // allowed to know about it.
  await expect(page.getByText('A password is saved. Type one here only to replace it.')).toBeVisible()

  // The field is empty: what is typed here REPLACES the stored password, and an
  // empty field is how "leave it alone" is expressed. The same holds once revealed.
  const password = page.getByLabel('Password', { exact: true })
  await expect(password).toHaveValue('')
  await page.getByRole('button', { name: 'Show' }).first().click()
  await expect(password).toHaveValue('')

  // Positive control: a value typed into the revealed field IS visible to the same assertion.
  await password.fill(TYPED_PASSWORD)
  await expect(password).toHaveValue(TYPED_PASSWORD)
})

test('a refused test message shows the mail server’s own words as the panel relayed them', async ({ page, guard }) => {
  // The refusal is the subject: the panel relays the mail server's rejection as a 400.
  guard.allow(400, /\/api\/v1\/notifications\/smtp\/test$/)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
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

test('the connection security is chosen from the modes the panel lists and sent as the panel spelled it', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubShellReads(page)
  await stubSmtpSettings(page, storedSmtpSettings)
  // The panel answers a save with 204 and no body; the shared stub answers every method with the settings.
  await page.route('**/api/v1/notifications/smtp', async (route) => {
    if (route.request().method() === 'PUT') {
      await route.fulfill({ status: 204 })
      return
    }
    await route.fallback()
  })

  await page.goto('/settings/smtp')
  await expect(page.getByLabel('Mail server')).toHaveValue('smtp.example.net')

  // The picker shows the stored mode under the panel's own name for it.
  const picker = page.getByRole('combobox', { name: 'Connection security' })
  await expect(picker).toContainText('STARTTLS (upgrade the connection)')

  // Every option is one the panel listed, in its order and its words; the bundle owns none of them.
  await picker.click()
  await expect(page.getByRole('option')).toHaveText([
    'STARTTLS (upgrade the connection)',
    'Implicit TLS (encrypted from the start)',
    'No encryption',
  ])
  await page.getByRole('option', { name: 'Implicit TLS (encrypted from the start)' }).click()

  const saving = page.waitForRequest((request) => {
    return request.method() === 'PUT' && request.url().endsWith('/api/v1/notifications/smtp')
  })
  await page.getByRole('button', { name: 'Save mail settings' }).click()

  const body = (await saving).postDataJSON() as { security: string }
  expect(body.security).toBe('implicitTls')
})
