import { createRouter, createWebHistory, type Router } from 'vue-router'
import DefaultLayout from '../layouts/DefaultLayout.vue'
import NotFoundPage from '../pages/NotFoundPage.vue'
import SystemStatusPage from '../pages/SystemStatusPage.vue'
import UpgradePage from '../pages/UpgradePage.vue'
import AccountDetailPage from '../pages/accounts/AccountDetailPage.vue'
import AccountsListPage from '../pages/accounts/AccountsListPage.vue'
import AccountFormPage from '../pages/accounts/AccountFormPage.vue'
import SitesListPage from '../pages/sites/SitesListPage.vue'
import SiteFormPage from '../pages/sites/SiteFormPage.vue'
import SiteDetailPage from '../pages/sites/SiteDetailPage.vue'
import DatabasesPage from '../pages/databases/DatabasesPage.vue'
import DatabaseGrantRepairPage from '../pages/databases/DatabaseGrantRepairPage.vue'
import BackupDestinationsPage from '../pages/backups/BackupDestinationsPage.vue'
import BackupSchedulePage from '../pages/backups/BackupSchedulePage.vue'
import BackupsPage from '../pages/backups/BackupsPage.vue'
import FileTransferPage from '../pages/ftp/FileTransferPage.vue'
import FirewallPage from '../pages/firewall/FirewallPage.vue'
import CronPage from '../pages/cron/CronPage.vue'
import TasksPage from '../pages/tasks/TasksPage.vue'
import MonitoringPage from '../pages/monitoring/MonitoringPage.vue'
import AuthLayout from '../layouts/AuthLayout.vue'
import LoginPage from '../pages/auth/LoginPage.vue'
import TwoFactorPage from '../pages/auth/TwoFactorPage.vue'
import SetupPage from '../pages/auth/SetupPage.vue'
import ForgotPasswordPage from '../pages/auth/ForgotPasswordPage.vue'
import ResetPasswordPage from '../pages/auth/ResetPasswordPage.vue'
import AuditPage from '../pages/settings/AuditPage.vue'
import SessionsPage from '../pages/settings/SessionsPage.vue'
import TwoFactorSettingsPage from '../pages/settings/TwoFactorSettingsPage.vue'
import SecurityPolicyPage from '../pages/settings/SecurityPolicyPage.vue'
import SmtpSettingsPage from '../pages/settings/SmtpSettingsPage.vue'
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
        path: '/sites',
        component: DefaultLayout,
        children: [
          { path: '', name: 'sites', component: SitesListPage, meta: { module: 'sites' } },
          { path: 'new', name: 'sites-new', component: SiteFormPage, meta: { module: 'sites' } },
          // Declared after 'new' so the literal segment wins: an :id route placed first would
          // swallow /sites/new and try to load a site called "new".
          {
            path: ':id',
            name: 'site-detail',
            component: SiteDetailPage,
            props: true,
            meta: { module: 'sites' },
          },
        ],
      },
      {
        // Two routes, and no `:id` child: a database has no detail to open — the row is the
        // database, and both of its actions live on it. A deep link to a single one would lead to a
        // page that could only repeat the row it came from.
        path: '/databases',
        component: DefaultLayout,
        children: [
          { path: '', name: 'databases', component: DatabasesPage, meta: { module: 'databases' } },
          // Administrators only, and the endpoint is what says so: a customer who types this URL
          // gets the panel's own refusal rendered on the page as a sentence about who may run it.
          // No route guard duplicates that rule — a second copy of an authorization decision is a
          // second place for it to be wrong, and the client's copy is the one that cannot be
          // trusted. It sits under `/databases` rather than under `/settings` because its subject
          // is this server's database grants, which is the Databases module's, and because a panel
          // whose licence excludes that module has no grants of ours to repair.
          {
            path: 'grant-repair',
            name: 'database-grant-repair',
            component: DatabaseGrantRepairPage,
            meta: { module: 'databases' },
          },
        ],
      },
      {
        // Three routes. The list has no detail to open — every field the panel holds about a
        // backup is already a column in it, and `GET /api/v1/backups/{id}` answers the same shape
        // — but the schedule and the destinations are different things from the copies they
        // describe: one edits a server-wide setting, the other reads where the copies live, and
        // neither belongs under a table of rows it did not take.
        path: '/backups',
        component: DefaultLayout,
        children: [
          { path: '', name: 'backups', component: BackupsPage, meta: { module: 'backups' } },
          // Administrators only, and the endpoint is what says so: a customer who types this URL
          // gets the panel's own 403 rendered on the page. No route guard duplicates that rule —
          // a second copy of an authorization decision is a second place for it to be wrong.
          {
            path: 'schedule',
            name: 'backup-schedule',
            component: BackupSchedulePage,
            meta: { module: 'backups' },
          },
          // Administrators only for the same reason and by the same mechanism: the endpoint
          // answers 403 and the page renders that sentence. The screen reads and never writes —
          // the panel publishes no way to add or remove a destination that could succeed.
          {
            path: 'destinations',
            name: 'backup-destinations',
            component: BackupDestinationsPage,
            meta: { module: 'backups' },
          },
        ],
      },
      {
        // One route, for the same reason as `/databases`: a login has no detail to open — the row
        // IS the login. It is guarded by the `sftp` module rather than by `ftp` on purpose: SFTP is
        // the half that is always there, and gating the merged screen on the FTPS module would take
        // the SFTP logins away from a panel whose licence does not include FTPS.
        path: '/file-transfer',
        component: DefaultLayout,
        children: [
          { path: '', name: 'file-transfer', component: FileTransferPage, meta: { module: 'sftp' } },
        ],
      },
      {
        // The screen this replaced. A customer's bookmark and every link the panel has ever
        // rendered point here, so the path stays and forwards rather than turning into a 404 on the
        // day the merged screen shipped.
        path: '/sftp-users',
        redirect: { name: 'file-transfer' },
      },
      {
        // One route, for the same reason as `/databases`: nothing on this screen has a detail to
        // open. A rule has no identity beyond its port, protocol and source range — the row IS the
        // rule — and neither a ban nor a whitelist entry is more than the row that shows it.
        path: '/firewall',
        component: DefaultLayout,
        children: [
          { path: '', name: 'firewall', component: FirewallPage, meta: { module: 'firewall' } },
        ],
      },
      {
        // One route, for the same reason as `/databases`: a cron entry has no detail to open. The
        // row IS the entry, and the one thing beyond it — what its last run left behind — costs a
        // privileged read per entry, so it is a dialog opened on demand rather than a page.
        path: '/cron',
        component: DefaultLayout,
        children: [{ path: '', name: 'cron', component: CronPage, meta: { module: 'cron' } }],
      },
      {
        // One route: a task's detail is a live stream, which belongs beside the row it came from
        // rather than behind a navigation that would tear the stream down and open another.
        path: '/tasks',
        component: DefaultLayout,
        children: [{ path: '', name: 'tasks', component: TasksPage, meta: { module: 'tasks' } }],
      },
      {
        // One route: the charts, the service states and the per-account disk table are read
        // together, and nothing on the screen has a detail to open — a chart is the whole of what
        // a metric has to say, and an account's own screen belongs to the Accounts module.
        path: '/monitoring',
        component: DefaultLayout,
        children: [
          { path: '', name: 'monitoring', component: MonitoringPage, meta: { module: 'monitoring' } },
        ],
      },
      {
        // One route, for the same reason as `/databases`: nothing on this screen has a detail to
        // open. A rule has no identity beyond its port, protocol and source range — the row IS the
        // rule — and neither a ban nor a whitelist entry is more than the row that shows it.
        path: '/firewall',
        component: DefaultLayout,
        children: [
          { path: '', name: 'firewall', component: FirewallPage, meta: { module: 'firewall' } },
        ],
      },
      {
        // One route, for the same reason as `/databases`: a cron entry has no detail to open. The
        // row IS the entry, and the one thing beyond it — what its last run left behind — costs a
        // privileged read per entry, so it is a dialog opened on demand rather than a page.
        path: '/cron',
        component: DefaultLayout,
        children: [{ path: '', name: 'cron', component: CronPage, meta: { module: 'cron' } }],
      },
      {
        // One route: a task's detail is a live stream, which belongs beside the row it came from
        // rather than behind a navigation that would tear the stream down and open another.
        path: '/tasks',
        component: DefaultLayout,
        children: [{ path: '', name: 'tasks', component: TasksPage, meta: { module: 'tasks' } }],
      },
      {
        // One route: the charts, the service states and the per-account disk table are read
        // together, and nothing on the screen has a detail to open — a chart is the whole of what
        // a metric has to say, and an account's own screen belongs to the Accounts module.
        path: '/monitoring',
        component: DefaultLayout,
        children: [
          { path: '', name: 'monitoring', component: MonitoringPage, meta: { module: 'monitoring' } },
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
        // Asking for a reset link and spending one are both unauthenticated by
        // necessity — the person has forgotten their password — so they share the
        // sign-in screens' bare layout and appear in no navigation at all.
        path: '/forgot-password',
        component: AuthLayout,
        children: [{ path: '', name: 'forgot-password', component: ForgotPasswordPage }],
      },
      {
        // The token arrives in the query string, because that is where the reset
        // mail's link puts it; the route itself carries nothing about the account.
        path: '/reset-password',
        component: AuthLayout,
        children: [{ path: '', name: 'reset-password', component: ResetPasswordPage }],
      },
      {
        // Enrolment for an administrator the panel is STEERING into it: the same
        // page as `/settings/two-factor`, on the bare layout, because a token that
        // reaches only the enrolment endpoints must not be given a shell whose every
        // other entry answers 403. The auth guard sends such a caller here from any
        // URL; nothing links to it.
        path: '/two-factor-setup',
        component: AuthLayout,
        children: [{ path: '', name: 'two-factor-setup', component: TwoFactorSettingsPage }],
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
          // Administrator-only screens, gated by their endpoints rather than by a
          // route guard, for the same reason the audit journal below is: a second
          // copy of an authorization decision is a second place for it to be wrong.
          // Neither carries `meta.module` — the security policy belongs to the panel
          // itself, and mail settings are what alerting AND password reset both
          // depend on, so hiding them behind a licence would disable the panel's
          // ability to send a reset link.
          { path: 'security-policy', name: 'security-policy', component: SecurityPolicyPage },
          { path: 'smtp', name: 'smtp-settings', component: SmtpSettingsPage },
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
