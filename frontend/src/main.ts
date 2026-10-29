import { createApp, watchEffect } from 'vue'
import App from './App.vue'
import { createAppI18n } from './i18n'
import { createAppPinia } from './pinia'
import { useDocumentTitle } from './composables/useDocumentTitle'
import { createAppRouter } from './router'
import { useLocaleStore } from './stores/locale'
import { useThemeStore } from './stores/theme'
import './assets/css/main.css'

// Application entry point: wires router, Pinia and i18n, then mounts the
// SPA shell into `#app` (see index.html). This is a script, not a function,
// so there is nothing here to attach a JSDoc block to beyond this comment.
const app = createApp(App)

app.use(createAppPinia())
const router = createAppRouter()
app.use(router)

// The theme store applies the stored (or system-preferred) theme to `<html data-theme>` the moment
// it is created, so it is created here, at boot, before the first component mounts. It used to be
// created only by DefaultLayout, which left every signed-out page (login, setup, password reset,
// invitation, two-factor enrolment) on index.html's baseline dark whatever the user had chosen.
// This is the one place the choice is resolved: no inline script in index.html repeats it, because
// the panel's CSP is `script-src 'self'` and an inline script would be refused.
useThemeStore()

const i18n = createAppI18n()
app.use(i18n)

// The locale store owns the language; i18n follows it. Wiring it here (rather
// than inside i18n.ts) keeps the i18n factory free of store dependencies, so
// tests can build an instance without a Pinia context.
const localeStore = useLocaleStore()
watchEffect(() => {
  i18n.global.locale.value = localeStore.current
  // Keep the document's declared language in step for assistive tech and
  // correct hyphenation/quoting; index.html's static lang only covers boot.
  document.documentElement.lang = localeStore.current
})

// The tab title follows the current route and the active language (WCAG 2.4.2). Started after the
// i18n instance exists because it translates through it, and before mount so it is already live
// when the first navigation resolves and the tab never keeps index.html's static title.
useDocumentTitle(router, i18n.global)

app.mount('#app')
