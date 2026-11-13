import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { signInAs } from './sign-in-as'

// The demo publishes what visitors type, so it filters abusive text and tells every visitor where to report
// abuse (issue #560). Run only through playwright.demo.config.ts against a running demo. The contact is read
// from the status endpoint, the one place the operator's configuration is reported, and the banner is held
// against it: a banner that printed a stale or invented address would fail here.

/** The label in front of the contact, per language: the exact sentence the banner shows. */
const REPORT_ABUSE_LABEL = {
  en: 'Report abusive content:',
  ru: 'Сообщить о недопустимом содержимом:',
  hy: 'Հաղորդել անթույլատրելի բովանդակության մասին՝',
} as const

/** The sentence the backend answers a refused text with, per language (its first words are enough). */
const CONTENT_REFUSED_START = {
  en: 'This text is not allowed in the demo',
  ru: 'Этот текст недопустим в демо-версии',
  hy: 'Այս տեքստը թույլատրելի չէ դեմոյում',
} as const

/** Whether a configured contact has the shape the backend accepts: one bare address or one https URL. */
const isContact = (value: string): boolean => {
  return /^[^\s@<>,;]+@[^\s@<>,;]+\.[^\s@<>,;]+$/.test(value) || /^https:\/\/[^\s/]+\S*$/.test(value)
}

/** Reads the abuse contact the demo reports about itself. */
const readReportedContact = async (page: Page): Promise<string> => {
  const response = await page.request.get('/api/v1/demo/status')
  expect(response.ok(), 'the demo status endpoint did not answer').toBe(true)
  const status = (await response.json()) as { abuseContact?: string }
  return status.abuseContact ?? ''
}

test('the demo reports a well-formed abuse contact', async ({ page }) => {
  const contact = await readReportedContact(page)

  expect(contact, 'the demo reports no abuse contact, so it must not have started').not.toBe('')
  expect(isContact(contact), `"${contact}" is neither an address nor an https URL`).toBe(true)
})

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`the sign-in banner names where to report abuse, in ${locale}`, async ({ page }) => {
    const contact = await readReportedContact(page)
    await setPersistedLocale(page, locale)
    await page.goto('/login')

    const abuse = page.getByTestId('demo-abuse')
    await expect(abuse).toBeVisible()
    await expect(abuse).toContainText(REPORT_ABUSE_LABEL[locale])
    await expect(page.getByTestId('demo-abuse-contact')).toHaveText(contact)
    // The inverse control: another language's label is not what this one shows.
    const other = locale === 'en' ? REPORT_ABUSE_LABEL.ru : REPORT_ABUSE_LABEL.en
    await expect(abuse).not.toContainText(other)
  })
}

/** A status answer the banner is held against, with or without a recorded reset. */
const statusWith = (nextResetAt: string | null): Record<string, unknown> => {
  return {
    intervalHours: 24,
    lastResetAt: nextResetAt,
    nextResetAt,
    identities: [],
    refusalReasons: [],
    abuseContact: 'abuse@example.com',
  }
}

/** Answers the demo status endpoint with a fixed body, whatever the real demo would say. */
const answerStatusWith = async (page: Page, body: Record<string, unknown>): Promise<void> => {
  await page.route('**/api/v1/demo/status', async (route) => {
    await route.fulfill({ json: body })
  })
}

test('the banner shows the filter notice and the contact before any reset is recorded', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await answerStatusWith(page, statusWith(null))
  await page.goto('/login')

  await expect(page.getByTestId('demo-banner')).toBeVisible()
  await expect(page.getByTestId('demo-banner')).toContainText('Offensive words and links to outside sites are refused.')
  await expect(page.getByTestId('demo-abuse-contact')).toHaveText('abuse@example.com')
  await expect(page.getByTestId('demo-next-reset')).toHaveCount(0)
})

test('the banner adds the next reset once one is recorded', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await answerStatusWith(page, statusWith('2030-01-01T00:00:00Z'))
  await page.goto('/login')

  await expect(page.getByTestId('demo-next-reset')).toBeVisible()
  await expect(page.getByTestId('demo-abuse-contact')).toHaveText('abuse@example.com')
})

test('the banner on a signed-in screen carries the same contact', async ({ page }) => {
  const contact = await readReportedContact(page)

  await signInAs(page, 'administrator')

  await expect(page.getByTestId('demo-abuse-contact')).toHaveText(contact)
})

for (const locale of ['en', 'ru', 'hy'] as const) {
  test(`a plan named with a blocked word is refused in ${locale} and nothing is saved`, async ({ page }) => {
    await setPersistedLocale(page, locale)
    await signInAs(page, 'administrator')
    await page.goto('/plans/new')
    const main = page.getByRole('main')

    await main.getByRole('textbox').first().fill('Casino deluxe')
    await main.locator('button[type="submit"]').click()

    await expect(main.getByText(CONTENT_REFUSED_START[locale])).toBeVisible()
    // Still on the form: a saved plan would have returned to the list.
    await expect(page).toHaveURL(/\/plans\/new/)
  })
}

test('a plan with a clean name is not stopped by the filter', async ({ page }) => {
  await signInAs(page, 'administrator')
  await page.goto('/plans/new')
  const main = page.getByRole('main')

  await main.getByRole('textbox').first().fill(`Probe ${Date.now()}`)
  await main.locator('button[type="submit"]').click()

  await expect(main.getByText(CONTENT_REFUSED_START.en)).toHaveCount(0)
  await expect(page).not.toHaveURL(/\/plans\/new/)
})
