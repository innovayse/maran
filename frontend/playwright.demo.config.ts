import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for the PUBLIC DEMO's smoke suite (`e2e/demo/`), and nothing else.
 *
 * The everyday suite (`playwright.config.ts`) stubs the network and serves the normal build from
 * Vite's preview, so it can say nothing about the demo: the demo flavour of the SPA (banner,
 * published logins, read-only notes) is a separate build (`npm run build:demo`), and its screens
 * are only meaningful against a running `Maran.Host.Demo` that serves real, seeded data. This
 * configuration therefore points a real Chromium at that demo and has no `webServer` of its own.
 * The suite is started by `scripts/maran demo smoke`, which brings the demo up, sets
 * `MARAN_DEMO_URL` and calls `npm run test:e2e:demo`; it never joins `maran test spa` or the
 * baseline (rules/testing.md, the same exception `e2e/install/` is).
 *
 * The sign-in details are never configured here: the specs read them off the login page, which is
 * the only place the demo publishes them.
 */

/** The demo's origin, e.g. `http://127.0.0.1:5090`. Empty when unset; the specs fail on that. */
const demoUrl = process.env.MARAN_DEMO_URL ?? ''

export default defineConfig({
  testDir: './e2e/demo',
  // One shared pair of logins on a shared, resettable stack: parallel specs would meet each
  // other's sessions and the mutation budget, so the suite runs one test at a time.
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  reporter: 'list',
  timeout: 60_000,
  expect: {
    timeout: 15_000,
  },
  use: {
    baseURL: demoUrl,
    trace: 'off',
    actionTimeout: 15_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
