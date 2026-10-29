import 'vue-router'

/**
 * Route meta every route in the router may carry. Registered once here (rather than per route) so
 * the router, the guards and the document-title unit share one typed shape. The `import` above is
 * what makes this file a module: without it the `declare module` below would REPLACE vue-router's
 * own typings instead of augmenting them.
 */
declare module 'vue-router' {
  /**
   * Augments vue-router's route meta with the module and title a route declares.
   */
  interface RouteMeta {
    /** Machine name of the module this route belongs to, if any. Ungated routes (system status, upgrade, 404) omit it. */
    module?: string
    /**
     * i18n key of the screen's name, shown in the browser tab and announced by a screen reader on
     * navigation (WCAG 2.4.2 Page Titled). A KEY and not text, so the title follows the locale.
     * A route that omits it falls back to the product name alone.
     */
    title?: string
  }
}
