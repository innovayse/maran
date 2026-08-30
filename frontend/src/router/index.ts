import { createRouter, createWebHistory, type Router } from 'vue-router'
import DefaultLayout from '../layouts/DefaultLayout.vue'
import NotFoundPage from '../pages/NotFoundPage.vue'
import SystemStatusPage from '../pages/SystemStatusPage.vue'
import UpgradePage from '../pages/UpgradePage.vue'
import AccountDetailPage from '../pages/accounts/AccountDetailPage.vue'
import AccountsListPage from '../pages/accounts/AccountsListPage.vue'
import AccountFormPage from '../pages/accounts/AccountFormPage.vue'
import AuthLayout from '../layouts/AuthLayout.vue'
import LoginPage from '../pages/auth/LoginPage.vue'
import TwoFactorPage from '../pages/auth/TwoFactorPage.vue'
import SetupPage from '../pages/auth/SetupPage.vue'
import AuditPage from '../pages/settings/AuditPage.vue'
import SessionsPage from '../pages/settings/SessionsPage.vue'
import TwoFactorSettingsPage from '../pages/settings/TwoFactorSettingsPage.vue'
import { createAuthGuard } from './authGuard'
import { createModuleAccessGuard } from './moduleAccessGuard'

/**
 * Creates the application router. Every route nests under {@link DefaultLayout},
 * which owns the document's single `<main>` landmark and the module-catalogue-driven
 * sidebar; features add their own routes under `src/pages/<feature>/` as they land,
 * tagging `meta.module` when the route belongs to a licensed module so the
 * module-access guard can gate it.
 * @returns A fresh {@link Router} instance to install with `app.use()`.
 */
export const createAppRouter = (): Router => {
  const router = createRouter({
    // HTML5 history mode: the reverse proxy in front of the SPA must serve
    // index.html for unknown paths (deep links), which is a deploy-time
    // concern, not something this router config can enforce.
    history: createWebHistory(),
    routes: [
      {
        path: '/',
        component: DefaultLayout,
        children: [{ path: '', name: 'system-status', component: SystemStatusPage }],
      },
      {
        path: '/accounts',
        component: DefaultLayout,
        children: [
          { path: '', name: 'accounts', component: AccountsListPage, meta: { module: 'accounts' } },
          { path: 'new', name: 'accounts-new', component: AccountFormPage, meta: { module: 'accounts' } },
          // Declared after 'new' so the literal segment wins: an :id route placed first would
          // swallow /accounts/new and try to load an account called "new".
          {
            path: ':id',
            name: 'account-detail',
            component: AccountDetailPage,
            props: true,
            meta: { module: 'accounts' },
          },
        ],
      },
      {
        // The unauthenticated screens: their own bare layout, no navigation, and no
        // dependency on the module catalogue — a visitor who cannot sign in must still
        // be able to render the screen that lets them.
        path: '/login',
        component: AuthLayout,
        children: [
          { path: '', name: 'login', component: LoginPage },
          { path: 'two-factor', name: 'login-two-factor', component: TwoFactorPage },
        ],
      },
      {
        path: '/setup',
        component: AuthLayout,
        children: [{ path: '', name: 'setup', component: SetupPage }],
      },
      {
        // Account security, not a licensed module: these two screens belong to
        // whoever is signed in, so they carry no `meta.module` and the licence
        // guard has nothing to say about them.
        path: '/settings',
        component: DefaultLayout,
        children: [
          { path: 'sessions', name: 'sessions', component: SessionsPage },
          { path: 'two-factor', name: 'two-factor', component: TwoFactorSettingsPage },
          // The journal is administrators-only, and the endpoint is what says so: a customer who
          // types this URL gets the panel's own refusal rendered on the page. No route guard
          // duplicates that rule here — a second copy of an authorization decision is a second
          // place for it to be wrong, and the client's copy is the one that cannot be trusted.
          { path: 'audit', name: 'audit', component: AuditPage },
        ],
      },
      {
        path: '/upgrade/:module',
        component: DefaultLayout,
        children: [{ path: '', name: 'upgrade', component: UpgradePage, props: true }],
      },
      {
        path: '/:pathMatch(.*)*',
        component: DefaultLayout,
        children: [{ path: '', name: 'not-found', component: NotFoundPage }],
      },
    ],
  })

  // Order matters: whether a module is licensed is a meaningless question about a
  // visitor who has not signed in, and answering it would tell an anonymous caller
  // which products this server has.
  router.beforeEach(createAuthGuard())
  router.beforeEach(createModuleAccessGuard())

  return router
}
