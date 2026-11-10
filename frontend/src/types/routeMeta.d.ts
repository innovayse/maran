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
     * The screen's name as TEXT, already localized, read live when it is needed. For the one kind of
     * route whose names are not known when this bundle is built: a packaged module's, which the
     * backend localizes per request and the catalogue carries. It is a function so a language
     * switch (which re-reads the catalogue) changes the title of the screen on display without a
     * navigation. A route declares `title` (a key) or `titleFrom` (text), never both; the tab title
     * and the breadcrumb read either through `utils/routeTitleText.ts`, so there is one place that
     * decides what a screen is called.
     */
    titleFrom?: () => string
    /**
     * Set on the layout record of a packaged module's routes (`/m/<id>`), and so on every route
     * under it: the module's id. The router registration finds the records it owns by it, to
     * replace or remove them when the catalogue changes, and the navigation places the screen in
     * the group of installed modules.
     */
    packaged?: string
    /**
     * Set with `packaged`: a fingerprint of what the registered routes were built from (version,
     * frame address and declared routes), so a catalogue refetch that changed none of it leaves
     * the routes alone.
     */
    packagedSignature?: string
    /**
     * Key of the navigation entry a packaged module's screen is the destination of, so the
     * breadcrumb and the page eyebrow place it in the "Modules" group under the entry's name.
     */
    packagedEntry?: string
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
     * The screen is for resellers only, and for nobody else: an administrator and a customer are
     * both turned away. Advice, never enforcement, exactly like `adminOnly`: the endpoint behind
     * it refuses every other role on its own (issue #179).
     */
    resellerOnly?: boolean
    /**
     * The screen's endpoints refuse a view-as session (`ViewAsAccess.Refused`, issue #334), so an
     * operator viewing as a login is sent to the "not available" screen instead of a page made of
     * refused requests (issue #513). Advice, never enforcement: the panel refuses regardless, and the
     * flag errs permissive (until the panel has confirmed the session nothing is turned away).
     */
    viewAsRefused?: boolean
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
    /**
     * The screen holds something the panel shows once and can never show again (the two-factor
     * recovery codes, whose hashes are all the backend keeps) in its own state. The shell remounts
     * the routed page when the interface language changes (issue #414), which would discard it, so
     * such a screen is kept mounted across the switch.
     */
    holdsOneTimeSecret?: boolean
  }
}
