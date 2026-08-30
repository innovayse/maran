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

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // The API's address in development. It must match ASPNETCORE_URLS in
    // scripts/run-dev.sh, which in turn matches the port the installer writes and
    // the nginx vhost proxies to in production — one port, stated in four places
    // that have to agree. It once said 5000 while the API listened on 5080, and
    // every request in development answered 502: the e2e suite stubs the network,
    // so nothing failed until someone opened the panel in a browser.
    proxy: {
      '/health': {
        target: API_ORIGIN,
        changeOrigin: true,
      },
      '/api': {
        target: API_ORIGIN,
        changeOrigin: true,
      },
    },
  },
})
