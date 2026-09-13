import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

/**
 * Vite build/dev configuration for the Maran SPA shell.
 *
 * Dev server proxies `/health` and `/api` to the Maran.Host backend,
 * which listens on http://localhost:5000 when run without a launch profile
 * (verified by running the Host locally and observing Kestrel's bound URL).
 */
/** Origin the API listens on in development (scripts/run-dev.sh). */
const API_ORIGIN = 'http://127.0.0.1:5080'

/**
 * What both servers forward to the panel's API.
 *
 * The API's address in development must match ASPNETCORE_URLS in scripts/run-dev.sh, which in turn
 * matches the port the installer writes and the nginx vhost proxies to in production — one port,
 * stated in four places that have to agree. It once said 5000 while the API listened on 5080, and
 * every request in development answered 502: the e2e suite stubs the network, so nothing failed
 * until someone opened the panel in a browser.
 */
const API_PROXY = {
  '/health': {
    target: API_ORIGIN,
    changeOrigin: true,
  },
  '/api': {
    target: API_ORIGIN,
    changeOrigin: true,
  },
}

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    rollupOptions: {
      input: {
        main: fileURLToPath(new URL('./index.html', import.meta.url)),
        // `UiChart`'s Playwright-only mount point (e2e/fixtures/chart-harness.html):
        // registered as a real build entry so `vite preview` — what CI's e2e run
        // serves — can reach it at the same path the dev server already does,
        // rather than only the dev server's implicit any-.html-file serving.
        chartHarness: fileURLToPath(new URL('./e2e/fixtures/chart-harness.html', import.meta.url)),
      },
    },
  },
  server: {
    proxy: API_PROXY,
  },
  // `preview` serves the built output, and it is what the e2e suite runs against — locally as well
  // as in CI (playwright.config.ts). It therefore has to forward the API exactly as `dev` does:
  // without this, `E2E_REAL_BACKEND=1` would hit a static file server that answers 404 to
  // `/api/...`, and the one spec that needs a live stack would fail for a reason that has nothing
  // to do with the panel. Vite reads `server.proxy` for `dev` only — the two are separate options
  // and the preview server inherits nothing.
  preview: {
    proxy: API_PROXY,
  },
})
