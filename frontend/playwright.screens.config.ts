import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for the SCREENSHOT SET of issue #82 (`e2e/screens/`), and nothing else.
 *
 * Every screen at 1440, 768 and 390 px, dark and light, written to
 * `frontend/test-results/screens/`. It is a tool for the owner and for a closing comment, not a
 * gate, so it has its own file and the everyday config ignores `e2e/screens/`: `maran test spa`
 * and its per-spec-file baseline never collect it, and it is not wired into CI.
 *
 *     npm run screens
 *
 * Like the everyday suite it photographs the BUILT app (`preview` after a fresh build), for the
 * reason `playwright.config.ts` spells out: a dev server transforms modules on first visit and a
 * stale `dist/` reports on code nobody is editing. Its artefacts go to their own folder
 * (`test-results/screens-run`) so a run never wipes the images it just wrote, nor they its.
 */

/** The port the preview server is started on; `E2E_PORT` moves it when 5173 is taken. */
const port = Number(process.env.E2E_PORT ?? 5173)
const baseURL = `http://127.0.0.1:${port.toString()}`

export default defineConfig({
  testDir: './e2e/screens',
  testMatch: '**/capture-every-screen.shots.ts',
  outputDir: './test-results/screens-run',
  fullyParallel: true,
  forbidOnly: true,
  retries: 0,
  reporter: 'list',
  timeout: 30_000,
  expect: {
    timeout: 5_000,
  },
  use: {
    baseURL,
    trace: 'off',
    actionTimeout: 10_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: `npm run build && npm run preview -- --port ${port.toString()} --strictPort --host 127.0.0.1`,
    url: baseURL,
    // False for the reason in playwright.config.ts: never photograph somebody else's dev server.
    reuseExistingServer: false,
    timeout: 120_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
