import { expect, test as base } from '@playwright/test'
import { installNetworkGuard, type NetworkGuard } from './network-guard'

/**
 * The real-stack lane's `test` (issue #519): no stub, a panel and an agent that really answer, and
 * a guard that fails the test on a 4xx/5xx the journey did not announce, a console error, an
 * uncaught page error, a failed API request, or a body that disagrees with
 * `docs/api/openapi-baseline.json`.
 *
 * A journey that expects a refusal (a validation message, a refused view-as) announces it:
 *
 *     guard.allow(400, /\/api\/v1\/accounts$/)
 *
 * Untranslated text is not part of the guard, because it depends on the language a journey is in;
 * a journey calls `findUntranslatedText` after it switches language.
 */
export const test = base.extend<{ guard: NetworkGuard }>({
  guard: [
    async ({ page }, use): Promise<void> => {
      const guard = await installNetworkGuard(page, 'real')
      await use(guard)
      expect(await guard.problems(), 'the panel answered, or the page did, something it should not have').toEqual([])
    },
    { auto: true },
  ],
})

export { expect }
