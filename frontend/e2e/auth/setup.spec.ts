import { expect, test } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSetupState } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'

test('a panel with no administrator sends every route to the setup screen', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/accounts')

  await expect(page).toHaveURL('/setup')
  await expect(page.getByRole('heading', { level: 2, name: 'Set up this panel' })).toBeVisible()
})

// The three tests below replace one that asserted the opposite: the screen used to prefill the
// token field from `?token=`. The installer stopped putting it there — a secret in a URL is written
// to the panel's own nginx access log AND to its error log, which takes no format and cannot be
// given one (docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md) — and a screen
// that still honoured the query string would put it back for anyone with an old bookmark.
//
// The needles are deliberately long phrases rather than the word "token": getByText and
// getByRole(name:) match case-insensitive SUBSTRINGS, so a needle like 'token' matches the 'Setup
// token' label, the placeholder and this notice alike, and would pass against any of them.

test('a token in the address is not accepted into the field, and the screen says why', async ({
  page,
}) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/setup?token=one-time-token')

  // The vacuity guard: the form is on screen and the label rendered, so the assertions below are
  // about a page that finished loading rather than about a shell that never got there.
  await expect(page.getByRole('heading', { level: 2, name: 'Set up this panel' })).toBeVisible()

  const field = page.getByRole('textbox', { name: 'Setup token' })
  await expect(field).toBeVisible()
  await expect(field).toHaveValue('')

  // A real sentence telling the operator what to do, not a machine word (rules/vue.md).
  await expect(
    page.getByText('The setup token is not part of this link, so the field below is empty on purpose.'),
  ).toBeVisible()

  // And the secret stops travelling with this tab: the address no longer carries it, so it cannot
  // be recopied out of the address bar or re-sent to a server that logs the request line.
  await expect(page).toHaveURL('/setup')
})

test('the notice appears only for a visitor who arrived with a token in the address', async ({
  page,
}) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/setup')

  // The control for the test above. Without it, a notice rendered unconditionally on every visit
  // would satisfy that assertion while telling an operator who did nothing wrong that they did.
  await expect(page.getByRole('textbox', { name: 'Setup token' })).toHaveValue('')
  await expect(
    page.getByText('The setup token is not part of this link, so the field below is empty on purpose.'),
  ).toHaveCount(0)
})

test('the notice is in the operator\'s own language, in all three', async ({ page }) => {
  // The one sentence a visitor with an old bookmark reads. An English-only notice on a Russian or
  // Armenian panel is the failure this asserts against, and it is invisible in an English run.
  const sentences: Record<'en' | 'ru' | 'hy', string> = {
    en: 'Paste the token the installer printed on its own line',
    ru: 'Вставьте токен, который установщик напечатал отдельной строкой',
    hy: 'Տեղադրեք այն կոդը, որը տեղադրիչը տպել է առանձին տողով',
  }

  for (const locale of ['en', 'ru', 'hy'] as const) {
    await setPersistedLocale(page, locale)
    await stubSetupState(page, { isComplete: false })
    await stubHealthy(page)
    await stubEmptyModules(page)

    await page.goto('/setup?token=one-time-token')

    // toContainText is case-SENSITIVE, unlike getByText, which is what makes a Cyrillic or
    // Armenian needle prove the locale rather than merely prove some text is present.
    await expect(page.getByRole('status')).toContainText(sentences[locale])
  }
})

test('a mismatched confirmation is reported on the field before anything is sent', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/setup')
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill('correct horse battery staple')
  await page.getByRole('textbox', { name: 'Confirm password' }).fill('correct horse battery stapl')

  await expect(page.getByText('The passwords do not match.')).toBeVisible()
})

test('creating the administrator lands on the sign-in screen', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/setup', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: '00000000-0000-0000-0000-000000000001',
        username: 'admin',
        email: 'admin@example.com',
        role: 'admin',
        accountId: null,
      }),
    })
  })

  await page.goto('/setup')
  // Typed, not prefilled: the token no longer travels in the address.
  await page.getByRole('textbox', { name: 'Setup token' }).fill('one-time-token')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Email' }).fill('admin@example.com')
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill('correct horse battery staple')
  await page.getByRole('textbox', { name: 'Confirm password' }).fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Create administrator' }).click()

  // Deliberately not signed in automatically: typing the new password once proves
  // it is the one the operator meant to set.
  await expect(page).toHaveURL('/login')
})

test('a rejected password shows the rule the backend broke it on', async ({ page }) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/setup', async (route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        code: 'PasswordTooWeak',
        detail: 'Choose a password of at least 12 characters that is different from your username.',
      }),
    })
  })

  await page.goto('/setup')
  // Typed, not prefilled: the token no longer travels in the address.
  await page.getByRole('textbox', { name: 'Setup token' }).fill('one-time-token')
  await page.getByRole('textbox', { name: 'Username' }).fill('admin')
  await page.getByRole('textbox', { name: 'Email' }).fill('admin@example.com')
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill('short')
  await page.getByRole('textbox', { name: 'Confirm password' }).fill('short')
  await page.getByRole('button', { name: 'Create administrator' }).click()

  await expect(
    page.getByText('Choose a password of at least 12 characters that is different from your username.'),
  ).toBeVisible()
})

test('generating a password fills the field with a value the server would accept', async ({
  page,
}) => {
  await stubSetupState(page, { isComplete: false })
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/setup')
  // The generator is offered on the password being SET and on nothing else: the
  // confirmation exists to catch a typo, and filling it from the same source
  // would defeat the only thing it is there for.
  const password = page.getByRole('textbox', { name: 'Password', exact: true })
  const generate = page.getByRole('button', { name: 'Generate a password' })
  await expect(password).toHaveValue('')
  await expect(generate).toHaveCount(1)

  await generate.click()

  // Revealed, because a value nobody can read is a value nobody can record.
  await expect(password).toHaveAttribute('type', 'text')

  // One sample cannot police an alphabet: a stray character appears in any
  // given 24 draws only about a third of the time, so a single-value assertion
  // passes most runs even when the alphabet is wrong. Widening the alphabet by
  // one character was tried against this test and survived until the sample
  // grew. Forty presses is about a thousand characters — enough that a single
  // extra character in the set is all but certain to show up.
  const samples: string[] = []
  for (let press = 0; press < 40; press += 1) {
    samples.push(await password.inputValue())
    await generate.click()
  }

  for (const sample of samples) {
    // The alphabet is the one the agent's Password type accepts. A character
    // outside it would pass here and be refused at the far end of the request.
    expect(sample).toMatch(/^[A-Za-z0-9\-_.=+]{24}$/)
  }

  // And every press hands back a different value.
  expect(new Set(samples).size).toBe(samples.length)
})
