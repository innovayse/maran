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
    /**
     * The screen is for administrators only, and for nobody else: a reseller and a customer are both
     * turned away. Advice, never enforcement: the endpoints behind it refuse every other role on
     * their own. The role guard uses it to send them to the "not available" screen instead of
     * showing administrator chrome around a refusal (issues #202 and #179).
     */
    adminOnly?: boolean
    /**
     * The screen is for administrators and resellers, the two roles that manage accounts, and a
     * customer is turned away. Advice, never enforcement, exactly like `adminOnly` (issue #179).
     */
    managerOnly?: boolean
    /**
     * Route name of the navigation destination this screen sits beneath, for a screen the
     * navigation does not list itself (a record, a form under a list). It places the screen in the
     * breadcrumb, under the right page eyebrow, one level below that destination (issue #209).
     */
    navigationParent?: string
    /**
     * The screen's breadcrumb leaf is the name of the record it shows, not its title. The page
     * reports that name through `useBreadcrumbRecord`; until it does the leaf is a placeholder,
     * never the raw identifier from the address.
     */
    titledByRecord?: boolean
  }
}
