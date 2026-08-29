import { createRouter, createWebHistory } from 'vue-router'
import type { Router } from 'vue-router'
import DefaultLayout from '../layouts/DefaultLayout.vue'
import NotFoundPage from '../pages/NotFoundPage.vue'
import SystemStatusPage from '../pages/SystemStatusPage.vue'
import UpgradePage from '../pages/UpgradePage.vue'
import AccountsListPage from '../pages/accounts/AccountsListPage.vue'
import AccountFormPage from '../pages/accounts/AccountFormPage.vue'
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

  router.beforeEach(createModuleAccessGuard())

  return router
}
