import type { Page, Request } from '@playwright/test'

/**
 * Answers every `POST /api/v1/accounts` with a refusal titled "Stop here.", so the create form stays
 * on screen and the body it sent can be read. Other methods on the same URL fall through to the
 * routes installed earlier (the paged list).
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubCreateRefused = async (page: Page): Promise<void> => {
  await page.route('**/api/v1/accounts', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Stop here.' }),
    })
  })
}

/**
 * Records the URL of every create request the browser attempts, so a spec can assert that none was
 * made.
 * @param page The Playwright page to listen on.
 * @returns The live list of create-request URLs, oldest first.
 */
export const recordCreateRequests = (page: Page): string[] => {
  const createRequests: string[] = []
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/accounts')) {
      createRequests.push(request.url())
    }
  })
  return createRequests
}

/**
 * Presses "Create account" and returns the create request it sent.
 * @param page The Playwright page.
 * @returns The request.
 */
export const submitCreate = async (page: Page): Promise<Request> => {
  const [request] = await Promise.all([
    page.waitForRequest((candidate) => {
      return candidate.method() === 'POST' && candidate.url().includes('/api/v1/accounts')
    }),
    page.getByRole('button', { name: 'Create account' }).click(),
  ])
  return request
}
