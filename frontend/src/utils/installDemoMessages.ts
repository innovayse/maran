import type { createAppI18n } from '../i18n'
import type { AppLocale } from '../types/app'

/** The application's i18n instance, named by what its factory returns. */
type AppI18n = ReturnType<typeof createAppI18n>

/**
 * Merges the public demo's message files (`locales/<locale>/demo.json`) into the running i18n
 * instance. Only the demo build imports this module (`main.ts`, behind `__MARAN_DEMO__`), which is
 * what keeps the demo's copy out of the normal bundle: the regular loader in `i18n.ts` excludes
 * these files.
 * @param i18n The instance created at boot.
 * @returns Nothing; the messages are merged in place.
 */
export const installDemoMessages = (i18n: AppI18n): void => {
  const modules = import.meta.glob<{ default: Record<string, unknown> }>('../locales/*/demo.json', {
    eager: true,
  })

  for (const [path, module] of Object.entries(modules)) {
    const locale = path.split('/')[2] as AppLocale
    i18n.global.mergeLocaleMessage(locale, module.default)
  }
}
