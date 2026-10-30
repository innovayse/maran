import { createRouter, createWebHistory, type Router } from 'vue-router'
import DefaultLayout from '../layouts/DefaultLayout.vue'
import NotFoundPage from '../pages/NotFoundPage.vue'
import NotAvailablePage from '../pages/NotAvailablePage.vue'
import SystemStatusPage from '../pages/SystemStatusPage.vue'
import UpgradePage from '../pages/UpgradePage.vue'
import AccountDetailPage from '../pages/accounts/AccountDetailPage.vue'
import AccountsListPage from '../pages/accounts/AccountsListPage.vue'
import AccountFormPage from '../pages/accounts/AccountFormPage.vue'
import MyAccountPage from '../pages/accounts/MyAccountPage.vue'
import PlanFormPage from '../pages/plans/PlanFormPage.vue'
import PlansListPage from '../pages/plans/PlansListPage.vue'
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
import AcceptInvitationPage from '../pages/auth/AcceptInvitationPage.vue'
import AuditPage from '../pages/settings/AuditPage.vue'
import SessionsPage from '../pages/settings/SessionsPage.vue'
import TwoFactorSettingsPage from '../pages/settings/TwoFactorSettingsPage.vue'
import SecurityPolicyPage from '../pages/settings/SecurityPolicyPage.vue'
import PhpVersionsPage from '../pages/settings/PhpVersionsPage.vue'
import SmtpSettingsPage from '../pages/settings/SmtpSettingsPage.vue'
import { createAuthGuard } from './authGuard'
import { createModuleAccessGuard } from './moduleAccessGuard'
import { createRoleGuard } from './roleGuard'

/**
 * Creates the application router. Every route nests under {@link DefaultLayout},
 * which owns the document's single `<main>` landmark and the module-catalogue-driven
 * sidebar; features add their own routes under `src/pages/<feature>/` as they land,
 * tagging `meta.module` when the route belongs to a licensed module so the
 * module-access guard can gate it. Every route also declares `meta.title`, the i18n key of its
 * screen's name, which `useDocumentTitle` turns into the browser tab's title (WCAG 2.4.2); the key
 * is the one the screen's own heading uses, so the tab and the page never name a screen two ways.
 *
 * A route only administrators may use carries `meta.adminOnly: true`. That flag is advice: the
 * endpoints behind those screens refuse a customer on their own and stay the boundary. The role
 * guard reads it so a customer who reaches such a URL sees the "not available" screen rather than
 * administrator chrome around a refusal (issue #202).
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
        children: [{ path: '', name: 'system-status', component: SystemStatusPage, meta: { title: 'app.status.heading' } }],
      },
      {
        path: '/accounts',
        component: DefaultLayout,
        children: [
          { path: '', name: 'accounts', component: AccountsListPage, meta: { module: 'accounts', title: 'accounts.list.heading', adminOnly: true } },
          { path: 'new', name: 'accounts-new', component: AccountFormPage, meta: { module: 'accounts', title: 'accounts.form.heading', adminOnly: true } },
          // Declared after 'new' so the literal segment wins: an :id route placed first would
          // swallow /accounts/new and try to load an account called "new".
          {
            path: ':id',
            name: 'account-detail',
            component: AccountDetailPage,
            props: true,
            meta: { module: 'accounts', title: 'accounts.detail.heading', adminOnly: true },
          },
        ],
      },
      {
        // Plans belong to the Accounts module — they are what an account is created against — so
        // they carry its `meta.module` and fall behind its licence. They are administrators-only:
        // the endpoints refuse a customer, and `adminOnly` sends one to the "not available" screen.
        path: '/plans',
        component: DefaultLayout,
        children: [
          { path: '', name: 'plans', component: PlansListPage, meta: { module: 'accounts', title: 'plans.list.heading', adminOnly: true } },
          { path: 'new', name: 'plans-new', component: PlanFormPage, meta: { module: 'accounts', title: 'plans.form.headingNew', adminOnly: true } },
          // After 'new', for the reason the accounts routes give.
          {
            path: ':id',
            name: 'plans-edit',
            component: PlanFormPage,
            props: true,
            meta: { module: 'accounts', title: 'plans.form.headingEdit', adminOnly: true },
          },
        ],
      },
      {
        path: '/sites',
        component: DefaultLayout,
        children: [
          { path: '', name: 'sites', component: SitesListPage, meta: { module: 'sites', title: 'sites.list.heading' } },
          { path: 'new', name: 'sites-new', component: SiteFormPage, meta: { module: 'sites', title: 'sites.form.heading' } },
          // Declared after 'new' so the literal segment wins: an :id route placed first would
          // swallow /sites/new and try to load a site called "new".
          {
            path: ':id',
            name: 'site-detail',
            component: SiteDetailPage,
            props: true,
            meta: { module: 'sites', title: 'sites.detail.heading' },
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
          { path: '', name: 'databases', component: DatabasesPage, meta: { module: 'databases', title: 'databases.list.heading' } },
          // Administrators only. The endpoint is the boundary; `adminOnly` is advice that spares a
          // customer an administrator's screen around a refusal. It sits under `/databases` rather
          // than under `/settings` because its subject is this server's database grants, which is
          // the Databases module's, and because a panel whose licence excludes that module has no
          // grants of ours to repair.
          {
            path: 'grant-repair',
            name: 'database-grant-repair',
            component: DatabaseGrantRepairPage,
            meta: { module: 'databases', title: 'databases.grantRepair.heading', adminOnly: true },
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
          { path: '', name: 'backups', component: BackupsPage, meta: { module: 'backups', title: 'backups.list.heading' } },
          // Administrators only. The endpoint refuses a customer; `adminOnly` sends one to the
          // "not available" screen instead of showing the refusal inside an administrator's page.
          {
            path: 'schedule',
            name: 'backup-schedule',
            component: BackupSchedulePage,
            meta: { module: 'backups', title: 'backups.schedule.heading', adminOnly: true },
          },
          // Administrators only, by the same two layers. The screen reads and never writes —
          // the panel publishes no way to add or remove a destination that could succeed.
          {
            path: 'destinations',
            name: 'backup-destinations',
            component: BackupDestinationsPage,
            meta: { module: 'backups', title: 'backups.destinations.heading', adminOnly: true },
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
          { path: '', name: 'file-transfer', component: FileTransferPage, meta: { module: 'sftp', title: 'ftp.list.heading' } },
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
          { path: '', name: 'firewall', component: FirewallPage, meta: { module: 'firewall', title: 'firewall.heading', adminOnly: true } },
        ],
      },
      {
        // One route, for the same reason as `/databases`: a cron entry has no detail to open. The
        // row IS the entry, and the one thing beyond it — what its last run left behind — costs a
        // privileged read per entry, so it is a dialog opened on demand rather than a page.
        path: '/cron',
        component: DefaultLayout,
        children: [{ path: '', name: 'cron', component: CronPage, meta: { module: 'cron', title: 'cron.list.heading' } }],
      },
      {
        // One route: a task's detail is a live stream, which belongs beside the row it came from
        // rather than behind a navigation that would tear the stream down and open another.
        path: '/tasks',
        component: DefaultLayout,
        children: [{ path: '', name: 'tasks', component: TasksPage, meta: { module: 'tasks', title: 'tasks.list.heading' } }],
      },
      {
        // One route: the charts, the service states and the per-account disk table are read
        // together, and nothing on the screen has a detail to open — a chart is the whole of what
        // a metric has to say, and an account's own screen belongs to the Accounts module.
        path: '/monitoring',
        component: DefaultLayout,
        children: [
          { path: '', name: 'monitoring', component: MonitoringPage, meta: { module: 'monitoring', title: 'monitoring.heading', adminOnly: true } },
        ],
      },
      {
        // The unauthenticated screens: their own bare layout, no navigation, and no
        // dependency on the module catalogue — a visitor who cannot sign in must still
        // be able to render the screen that lets them.
        path: '/login',
        component: AuthLayout,
        children: [
          { path: '', name: 'login', component: LoginPage, meta: { title: 'app.auth.signInTitle' } },
          { path: 'two-factor', name: 'login-two-factor', component: TwoFactorPage, meta: { title: 'app.auth.twoFactorTitle' } },
        ],
      },
      {
        path: '/setup',
        component: AuthLayout,
        children: [{ path: '', name: 'setup', component: SetupPage, meta: { title: 'app.auth.setupTitle' } }],
      },
      {
        // Asking for a reset link and spending one are both unauthenticated by
        // necessity — the person has forgotten their password — so they share the
        // sign-in screens' bare layout and appear in no navigation at all.
        path: '/forgot-password',
        component: AuthLayout,
        children: [{ path: '', name: 'forgot-password', component: ForgotPasswordPage, meta: { title: 'app.passwordReset.requestTitle' } }],
      },
      {
        // The token arrives in the query string, because that is where the reset
        // mail's link puts it; the route itself carries nothing about the account.
        path: '/reset-password',
        component: AuthLayout,
        children: [{ path: '', name: 'reset-password', component: ResetPasswordPage, meta: { title: 'app.passwordReset.resetTitle' } }],
      },
      {
        // The token arrives in the query string, exactly as `/reset-password`'s does, because
        // that is where the invitation mail's link puts it — the route itself carries nothing
        // about the account. Unauthenticated by necessity: the login this token belongs to has
        // never signed in, so it shares the sign-in screens' bare layout.
        path: '/accept-invitation',
        component: AuthLayout,
        children: [{ path: '', name: 'accept-invitation', component: AcceptInvitationPage, meta: { title: 'app.acceptInvitation.title' } }],
      },
      {
        path: '/my-account',
        component: DefaultLayout,
        children: [
          // No `meta.module`: this is not a licensed module, it is a customer's own account —
          // exactly like `/settings/sessions` above. Not `adminOnly`, and no customer-only flag
          // either: the endpoint (`GET /api/v1/accounts/me`) answers "the account YOU own" off the
          // caller's own token and 404s for an administrator, who owns none.
          { path: '', name: 'my-account', component: MyAccountPage, meta: { title: 'app.shell.menu.myAccount' } },
        ],
      },
      {
        // Enrolment for an administrator the panel is STEERING into it: the same
        // page as `/settings/two-factor`, on the bare layout, because a token that
        // reaches only the enrolment endpoints must not be given a shell whose every
        // other entry answers 403. The auth guard sends such a caller here from any
        // URL; nothing links to it.
        path: '/two-factor-setup',
        component: AuthLayout,
        children: [{ path: '', name: 'two-factor-setup', component: TwoFactorSettingsPage, meta: { title: 'app.twoFactor.heading' } }],
      },
      {
        // Account security, not a licensed module: these two screens belong to
        // whoever is signed in, so they carry no `meta.module` and the licence
        // guard has nothing to say about them.
        path: '/settings',
        component: DefaultLayout,
        children: [
          { path: 'sessions', name: 'sessions', component: SessionsPage, meta: { title: 'app.sessions.heading' } },
          { path: 'two-factor', name: 'two-factor', component: TwoFactorSettingsPage, meta: { title: 'app.twoFactor.heading' } },
          // Administrator-only screens: their endpoints refuse a customer, and
          // `adminOnly` keeps a customer on the "not available" screen instead.
          // Neither carries `meta.module` — the security policy belongs to the panel
          // itself, and mail settings are what alerting AND password reset both
          // depend on, so hiding them behind a licence would disable the panel's
          // ability to send a reset link.
          { path: 'security-policy', name: 'security-policy', component: SecurityPolicyPage, meta: { title: 'app.securityPolicy.heading', adminOnly: true } },
          { path: 'smtp', name: 'smtp-settings', component: SmtpSettingsPage, meta: { title: 'app.smtp.heading', adminOnly: true } },
          // PHP runtimes are server-wide and installing one is administrators' work, so this sits
          // with the other administrator screens and carries no `meta.module`: a host with no PHP
          // version installed is a host where no PHP site can be created at all, which is not a
          // state a licence should be able to produce.
          { path: 'php', name: 'php-versions', component: PhpVersionsPage, meta: { title: 'php.title', adminOnly: true } },
          // The journal is administrators-only: the endpoint is the boundary, `adminOnly` the advice.
          { path: 'audit', name: 'audit', component: AuditPage, meta: { title: 'app.audit.heading', adminOnly: true } },
        ],
      },
      {
        path: '/upgrade/:module',
        component: DefaultLayout,
        children: [{ path: '', name: 'upgrade', component: UpgradePage, props: true, meta: { title: 'app.upgrade.heading' } }],
      },
      {
        // Where the role guard sends a customer who reached an administrator's screen, and where
        // the module guard sends one whose catalogue does not list the module. No `meta.module`
        // and no `adminOnly`, so no guard can redirect away from it.
        path: '/not-available',
        component: DefaultLayout,
        children: [{ path: '', name: 'not-available', component: NotAvailablePage, meta: { title: 'app.notAvailable.heading' } }],
      },
      {
        path: '/:pathMatch(.*)*',
        component: DefaultLayout,
        children: [{ path: '', name: 'not-found', component: NotFoundPage, meta: { title: 'app.notFound.heading' } }],
      },
    ],
  })

  // Order matters: whether a module is licensed is a meaningless question about a
  // visitor who has not signed in, and answering it would tell an anonymous caller
  // which products this server has.
  router.beforeEach(createAuthGuard())
  // The role guard runs before the licence guard: a customer must never be told to buy a screen
  // that is an administrator's, not theirs.
  router.beforeEach(createRoleGuard())
  router.beforeEach(createModuleAccessGuard())

  return router
}
