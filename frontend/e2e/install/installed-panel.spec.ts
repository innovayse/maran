import { expect, test } from '@playwright/test'

// The installed-panel browser stage (issue #68). Run only through playwright.install.config.ts,
// normally by `maran installer --browser`; the everyday suite ignores this folder.
//
// It drives the first-run screen and the sign-in screen through a real Chromium against a panel a
// real installer set up, because two defects were invisible to every curl-based check: a TLS
// certificate Chromium refuses outright (#67, `ERR_SSL_VERSION_OR_CIPHER_MISMATCH`, which
// `ignoreHTTPSErrors` does not bypass) and a panel whose database had no schema (#66).

// Only the environment is read, so it is declared here rather than adding Node's global types to
// a program that also compiles browser code (same arrangement as e2e/golden-path).
declare const process: { readonly env: Readonly<Record<string, string | undefined>> }

/** The installed panel's origin. Required: an unset value is a failure, never a skip. */
const INSTALLED_URL = process.env.MARAN_INSTALLED_URL ?? ''

/** The installer's one-time setup token, read from /etc/maran/panel.env by the caller. */
const SETUP_TOKEN = process.env.MARAN_SETUP_TOKEN ?? ''

/** The administrator to create. The polygon passes its own so its later stages can sign in. */
const ADMIN_USERNAME = process.env.MARAN_ADMIN_USERNAME ?? 'browseradmin'

/** The administrator's contact address. */
const ADMIN_EMAIL = process.env.MARAN_ADMIN_EMAIL ?? 'browser.admin@example.test'

/** The administrator's password; at least the twelve characters the backend's validator asks. */
const ADMIN_PASSWORD = process.env.MARAN_ADMIN_PASSWORD ?? 'Browser-Stage-Password-1'

test('a real browser completes first-run setup and signs in to the installed panel over its TLS listener', async ({
  page,
}) => {
  // Vacuity guards on the axes that can go blind. Without a URL every navigation is relative to
  // nothing; without https the stage would never negotiate TLS and could not see #67; without a
  // token the setup is refused and the failure would read as a UI defect.
  expect(INSTALLED_URL, 'MARAN_INSTALLED_URL is unset, so there is no installed panel to open').not.toBe('')
  expect(INSTALLED_URL.startsWith('https://'), `MARAN_INSTALLED_URL must be https, got '${INSTALLED_URL}'`).toBe(true)
  expect(
    SETUP_TOKEN.length,
    'MARAN_SETUP_TOKEN is shorter than 8 characters, so setup cannot succeed',
  ).toBeGreaterThanOrEqual(8)

  // A handshake Chromium refuses throws here, before any assertion: that is the #67 failure.
  const response = await page.goto('/setup')
  expect(response, 'navigation to /setup produced no response').not.toBeNull()
  expect(response?.status(), '/setup did not answer 200').toBe(200)
  // The positive observation that TLS was actually negotiated by the browser on this response.
  const security = await response?.securityDetails()
  expect(security?.protocol ?? '', 'Chromium reports no TLS protocol for /setup').toMatch(/^TLS/)

  await expect(page.getByRole('heading', { level: 2, name: 'Set up this panel' })).toBeVisible()
  await page.getByRole('textbox', { name: 'Setup token' }).fill(SETUP_TOKEN)
  await page.getByRole('textbox', { name: 'Username' }).fill(ADMIN_USERNAME)
  await page.getByRole('textbox', { name: 'Email' }).fill(ADMIN_EMAIL)
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill(ADMIN_PASSWORD)
  await page.getByRole('textbox', { name: 'Confirm password' }).fill(ADMIN_PASSWORD)
  await page.getByRole('button', { name: 'Create administrator' }).click()

  // Setup sends the operator to sign in rather than signing them in, so the sign-in heading is
  // the proof the server created the administrator (a refusal keeps the form and shows an alert).
  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()

  await page.getByRole('textbox', { name: 'Username' }).fill(ADMIN_USERNAME)
  await page.getByRole('textbox', { name: 'Password' }).fill(ADMIN_PASSWORD)
  await page.getByRole('button', { name: 'Sign in' }).click()

  // The signed-in shell and the landing screen's own heading, both rendered from the API.
  await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: 'System status' })).toBeVisible()
})
