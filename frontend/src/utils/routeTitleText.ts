import type { RouteMeta } from 'vue-router'

/**
 * The name of a screen, as the tab title, the breadcrumb and a packaged module's heading print it.
 * A route names itself with an i18n key (`meta.title`) or, for a packaged module's route whose
 * name only the backend knows, with live text (`meta.titleFrom`). This is the one place that
 * decides which, so the surfaces cannot name a screen two ways.
 * @param meta The route's meta.
 * @param translate The i18n `t` of the caller.
 * @returns The screen's name; the product name when the route declares none (or the backend text is
 * not available yet).
 */
export const routeTitleText = (meta: RouteMeta, translate: (key: string) => string): string => {
  if (meta.titleFrom !== undefined) {
    const text = meta.titleFrom()
    return text === '' ? translate('app.title') : text
  }
  return translate(meta.title ?? 'app.title')
}
