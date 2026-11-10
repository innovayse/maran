import { expect, test } from '../fixtures/guarded-test'
import { API_SCOPE_SCRIPT, GOOD_SCRIPT, PROBE_CONTROL_SCRIPT, PROBE_SCRIPT } from '../fixtures/module-frame-scripts'
import { openPackagedShell, packagedMenuLink } from '../fixtures/open-packaged-module'
import { PACKAGED_ID } from '../fixtures/stub-packaged-module'
import type { Page } from '@playwright/test'

// Issue #185: the isolation claims, observed rather than assumed. The frame runs with
// `sandbox="allow-scripts"` and no `allow-same-origin`, so it has an opaque origin: it cannot read
// the panel's cookie or local storage or touch its document, and it can only reach the API through
// the shell, which names the module on every call and gets a server-side refusal when the module
// reaches past its own endpoints.

/** A cookie value the shell holds and a framed module must never be able to read. */
const SHELL_COOKIE_VALUE = 'cookie-secret-4711'

/** A local-storage value the shell holds and a framed module must never be able to read. */
const SHELL_STORAGE_VALUE = 'storage-secret-4711'

/**
 * Gives the shell origin a cookie and a local-storage item worth stealing.
 * @param page The page under test.
 * @param baseURL The origin the suite serves the panel from.
 * @returns Resolves once both are in place for every document the page loads.
 */
const seedShellSecrets = async (page: Page, baseURL: string | undefined): Promise<void> => {
  await page.context().addCookies([{ name: 'shellcookie', value: SHELL_COOKIE_VALUE, url: baseURL ?? 'http://127.0.0.1' }])
  await page.addInitScript((value: string) => {
    try {
      window.localStorage.setItem('shell-secret', value)
    } catch {
      // A sandboxed frame has no storage to seed; only the shell's own origin is meant to hold it.
    }
  }, SHELL_STORAGE_VALUE)
}

test('a sandboxed frame cannot read the shell cookie, local storage or document', async ({ page, baseURL }) => {
  await seedShellSecrets(page, baseURL)
  await openPackagedShell(page, { script: PROBE_SCRIPT })
  await packagedMenuLink(page).click()

  const toast = page.getByText(/^cookie=/)
  await expect(toast).toBeVisible()
  const report = (await toast.textContent()) ?? ''
  expect(report).toMatch(/cookie=blocked:SecurityError/)
  expect(report).toMatch(/storage=blocked:SecurityError/)
  expect(report).toMatch(/parent=blocked:SecurityError/)
  // The absence claim, by value: neither secret is anywhere in what the frame reported.
  expect(report).not.toContain(SHELL_COOKIE_VALUE)
  expect(report).not.toContain(SHELL_STORAGE_VALUE)
  await expect(page.locator('iframe')).toHaveAttribute('sandbox', 'allow-scripts')
})

test('positive control: the same probe in a page that is not sandboxed reads the shell cookie and storage', async ({
  page,
  baseURL,
}) => {
  await seedShellSecrets(page, baseURL)
  // A same-origin document with no sandbox: if the probe could not see here, the test above would
  // report "blocked" for a broken probe as happily as for a working sandbox.
  await page.route('**/__probe/control', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'text/html',
      body: '<!doctype html><html><head><meta charset="utf-8"><title>pending</title></head><body><script src="/__probe/control.js"></script></body></html>',
    })
  })
  await page.route('**/__probe/control.js', async (route) => {
    await route.fulfill({ status: 200, contentType: 'text/javascript', body: PROBE_CONTROL_SCRIPT })
  })

  await page.goto('/__probe/control')

  await expect(page).toHaveTitle(
    new RegExp(`cookie=read:.*shellcookie=${SHELL_COOKIE_VALUE}.*;storage=read:${SHELL_STORAGE_VALUE};parent=read:`),
  )
})

test('a framed API call is relayed with the module name, and the server refusal comes back as data', async ({
  page,
  guard,
}) => {
  // The refusal of the endpoint the module does not own is the subject: the shell relays it as data.
  guard.allow(403, /\/api\/v1\/accounts$/)
  const seen: Array<{ path: string; frame: string | undefined; csrf: string | undefined; auth: string | undefined }> = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (path.startsWith('/api/v1/')) {
      const headers = request.headers()
      seen.push({ path, frame: headers['x-maran-frame'], csrf: headers['x-maran-request'], auth: headers.authorization })
    }
  })
  await openPackagedShell(page, {
    script: API_SCOPE_SCRIPT,
    stubs: async (stubbed): Promise<void> => {
      await stubbed.route('**/api/v1/hello/greeting', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ greeting: 'Hello' }) })
      })
      await stubbed.route('**/api/v1/hello/items', async (route) => {
        await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 1 }) })
      })
      // The stubbed server enforces what the real middleware does: a framed call to an endpoint the
      // module does not own is refused with `ModuleFrameOutOfScope`; the same endpoint without the
      // header (an administrator at work) is answered normally.
      await stubbed.route('**/api/v1/accounts', async (route) => {
        if (route.request().headers()['x-maran-frame'] !== undefined) {
          await route.fulfill({
            status: 403,
            contentType: 'application/problem+json',
            body: JSON.stringify({
              code: 'ModuleFrameOutOfScope',
              title: 'Out of scope',
              detail: 'This endpoint does not belong to the module.',
              status: 403,
            }),
          })
          return
        }
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [], total: 0, page: 1, pageSize: 25 }),
        })
      })
    },
  })
  await packagedMenuLink(page).click()

  const frame = page.frameLocator('iframe')
  await expect(frame.getByText('result own 200 {"greeting":"Hello"}')).toBeVisible()
  await expect(frame.getByText('result write 201 {"id":1}')).toBeVisible()
  // The refusal is DATA in the frame: status and problem body, nothing thrown into the shell.
  await expect(frame.getByText(/result other 403 .*"code":"ModuleFrameOutOfScope"/)).toBeVisible()
  await expect(frame.getByText(/result other 403 .*This endpoint does not belong to the module\./)).toBeVisible()
  await expect(page.getByText('is unavailable')).toHaveCount(0)

  // Every framed call carried the module name, the CSRF header and the shell's own session.
  for (const path of ['/api/v1/hello/greeting', '/api/v1/hello/items', '/api/v1/accounts']) {
    const calls = seen.filter((request) => {return request.path === path})
    expect(calls.length, path).toBeGreaterThan(0)
    for (const call of calls) {
      expect(call.frame, path).toBe(PACKAGED_ID)
      expect(call.csrf, path).toBe('1')
      expect(call.auth, path).toBeDefined()
    }
  }
  // The inverse: the shell's own calls never declare a module.
  const catalogueReads = seen.filter((request) => {return request.path === '/api/v1/modules'})
  expect(catalogueReads.length).toBeGreaterThan(0)
  for (const read of catalogueReads) {
    expect(read.frame).toBeUndefined()
  }
  // And the frame was never handed the shell's token.
  await expect(frame.locator('body')).not.toContainText('stub.access.token')

  // The stub's own control: the endpoint the module was refused on answers the shell normally.
  const direct = await page.evaluate(async () => {
    const response = await fetch('/api/v1/accounts')
    return response.status
  })
  expect(direct).toBe(200)
})

test('a good module is not refused: the accepting control for the scope stub', async ({ page }) => {
  await openPackagedShell(page, {
    script: GOOD_SCRIPT,
    stubs: async (stubbed): Promise<void> => {
      await stubbed.route('**/api/v1/hello/greeting', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ greeting: 'Hello' }) })
      })
    },
  })
  await packagedMenuLink(page).click()

  await expect(page.frameLocator('iframe').getByText('result greeting 200 {"greeting":"Hello"}')).toBeVisible()
})
