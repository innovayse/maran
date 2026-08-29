import { createApp, watchEffect } from 'vue'
import App from './App.vue'
import { createAppI18n } from './i18n'
import { createAppPinia } from './pinia'
import { createAppRouter } from './router'
import { useLocaleStore } from './stores/locale'
import './assets/css/main.css'

// Application entry point: wires router, Pinia and i18n, then mounts the
// SPA shell into `#app` (see index.html). This is a script, not a function,
// so there is nothing here to attach a JSDoc block to beyond this comment.
const app = createApp(App)

app.use(createAppPinia())
app.use(createAppRouter())

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

app.mount('#app')
