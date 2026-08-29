import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for the Maran SPA's end-to-end suite — the
 * only kind of test this frontend has (rules/testing.md: "no colocated unit
 * tests — the SPA is verified end-to-end").
 *
 * Runs with no backend present by default: golden-path specs stub `/health`
 * and `/api/v1/modules` themselves via `e2e/fixtures/`, and the `webServer`
 * below only needs the Vite dev server, not `Maran.Host`. To instead
 * exercise a real running backend (proxied by Vite per `vite.config.ts`),
 * set `E2E_REAL_BACKEND=1` when invoking `npm run test:e2e`; specs that
 * install network stubs are expected to be skipped or adapted by the caller
 * in that mode — this flag only changes what the config itself assumes, it
 * does not alter existing specs.
 */
const port = 5173
const baseURL = `http://127.0.0.1:${port}`
const isCi = process.env.CI === 'true' || process.env.CI === '1'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  workers: isCi ? 1 : undefined,
  reporter: 'list',
  timeout: 30_000,
  expect: {
    timeout: 5_000,
  },
  use: {
    baseURL,
    trace: 'on-first-retry',
    actionTimeout: 10_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    // Golden-path specs stub the network themselves, so no backend needs to be reachable.
    // In CI the built app is served with `preview`: the job has already run `npm run build`, and
    // serving static output starts in a second, whereas the dev server has to boot Vite's
    // pipeline and was timing out on a cold runner. Locally `dev` keeps hot reload.
    command: isCi
      ? 'npm run preview -- --port 5173 --strictPort --host 127.0.0.1'
      : 'npm run dev -- --port 5173 --strictPort --host 127.0.0.1',
    url: baseURL,
    reuseExistingServer: !isCi,
    // Generous on a cold CI runner: a slow start must not read as a broken app.
    timeout: 120_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
