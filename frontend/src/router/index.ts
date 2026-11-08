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
import SubUsersPage from '../pages/accounts/SubUsersPage.vue'
import ProfilePage from '../pages/ProfilePage.vue'
import PlanFormPage from '../pages/plans/PlanFormPage.vue'
import PlansListPage from '../pages/plans/PlansListPage.vue'
import ResellerOverviewPage from '../pages/reseller/ResellerOverviewPage.vue'
import ResellerDetailPage from '../pages/resellers/ResellerDetailPage.vue'
import ResellerFormPage from '../pages/resellers/ResellerFormPage.vue'
import ResellerProfileFormPage from '../pages/resellers/ResellerProfileFormPage.vue'
import ResellersListPage from '../pages/resellers/ResellersListPage.vue'
import SitesListPage from '../pages/sites/SitesListPage.vue'
import SiteFormPage from '../pages/sites/SiteFormPage.vue'
import SiteDetailPage from '../pages/sites/SiteDetailPage.vue'
import DatabasesPage from '../pages/databases/DatabasesPage.vue'
import DatabaseUsersPage from '../pages/databases/DatabaseUsersPage.vue'
import DatabaseGrantRepairPage from '../pages/databases/DatabaseGrantRepairPage.vue'
import DnsNameserversPage from '../pages/dns/DnsNameserversPage.vue'
import DnsZoneDetailPage from '../pages/dns/DnsZoneDetailPage.vue'
import DnsZoneFormPage from '../pages/dns/DnsZoneFormPage.vue'
import DnsZonesListPage from '../pages/dns/DnsZonesListPage.vue'
import BackupDestinationsPage from '../pages/backups/BackupDestinationsPage.vue'
import BackupSchedulePage from '../pages/backups/BackupSchedulePage.vue'
import MyBackupSchedulePage from '../pages/backups/MyBackupSchedulePage.vue'
import BackupsPage from '../pages/backups/BackupsPage.vue'
import FtpUsersListPage from '../pages/ftp/FtpUsersListPage.vue'
import SftpUsersListPage from '../pages/sftp/SftpUsersListPage.vue'
import MailAliasesListPage from '../pages/mail/MailAliasesListPage.vue'
import MailboxesListPage from '../pages/mail/MailboxesListPage.vue'
import MailDomainsListPage from '../pages/mail/MailDomainsListPage.vue'
import FirewallPage from '../pages/firewall/FirewallPage.vue'
import FirewallBansPage from '../pages/firewall/FirewallBansPage.vue'
import FirewallWhitelistPage from '../pages/firewall/FirewallWhitelistPage.vue'
import FileManagerPage from '../pages/files/FileManagerPage.vue'
import FileEditPage from '../pages/files/FileEditPage.vue'
import FileTrashPage from '../pages/files/FileTrashPage.vue'
import CronPage from '../pages/cron/CronPage.vue'
import CronRunsPage from '../pages/cron/CronRunsPage.vue'
import TasksPage from '../pages/tasks/TasksPage.vue'
import MonitoringPage from '../pages/monitoring/MonitoringPage.vue'
import PendingUpdatesPage from '../pages/monitoring/PendingUpdatesPage.vue'
import AuthLayout from '../layouts/AuthLayout.vue'
import LoginPage from '../pages/auth/LoginPage.vue'
import TwoFactorPage from '../pages/auth/TwoFactorPage.vue'
import SetupPage from '../pages/auth/SetupPage.vue'
import ForgotPasswordPage from '../pages/auth/ForgotPasswordPage.vue'
import ResetPasswordPage from '../pages/auth/ResetPasswordPage.vue'
import AcceptInvitationPage from '../pages/auth/AcceptInvitationPage.vue'
import AuditPage from '../pages/settings/AuditPage.vue'
import SessionsPage from '../pages/settings/SessionsPage.vue'
import ApiKeysPage from '../pages/settings/ApiKeysPage.vue'
import TwoFactorSettingsPage from '../pages/settings/TwoFactorSettingsPage.vue'
import SecurityPolicyPage from '../pages/settings/SecurityPolicyPage.vue'
import PhpSettingsPage from '../pages/sites/PhpSettingsPage.vue'
import PhpVersionsPage from '../pages/settings/PhpVersionsPage.vue'
import PanelUpdatePage from '../pages/settings/PanelUpdatePage.vue'
import PanelCertificatePage from '../pages/settings/PanelCertificatePage.vue'
import ResellerBrandingPage from '../pages/reseller/ResellerBrandingPage.vue'
import AlertSettingsPage from '../pages/settings/AlertSettingsPage.vue'
import SmtpSettingsPage from '../pages/settings/SmtpSettingsPage.vue'
import { createAuthGuard } from './authGuard'
import { createModuleAccessGuard } from './moduleAccessGuard'
import { createRoleGuard } from './roleGuard'

/**
 * A route segment naming a site by its id, which the panel only ever issues as a UUID. A segment of
 * any other shape (`/sites/php`) cannot name a site, so the route does not match it and the visitor
 * lands on the not-found screen without a request being made (issue #204).
 */
const SITE_ID_SEGMENT = ':id([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})'

/**
 * The route segment naming a DNS zone by its id, which the panel only ever issues as a UUID, with the same effect as {@link SITE_ID_SEGMENT}: any other shape is not found
 * without a request being made. Whole string constants, because the route census reads the router
 * as text and resolves a segment by its constant (`e2e/ui-kit/route-coverage.spec.ts`).
 */
const DNS_ZONE_SEGMENT = 'zones/:id([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})'

/**
 * Creates the application router. Every route nests under {@link DefaultLayout},
 * which owns the document's single `<main>` landmark and the module-catalogue-driven
 * sidebar; features add their own routes under `src/pages/<feature>/` as they land,
 * tagging `meta.module` when the route belongs to a licensed module so the
 * module-access guard can gate it. Every route also declares `meta.title`, the i18n key of its
 * screen's name, which `useDocumentTitle` turns into the browser tab's title (WCAG 2.4.2); the key
 * is the one the screen's own heading uses, so the tab and the page never name a screen two ways.
 *
 * A route only administrators may use carries `meta.adminOnly: true`, one for administrators
 * and resellers `meta.managerOnly: true`, and one only a reseller may use `meta.resellerOnly: true`.
 * The flags are advice: the endpoints behind those screens
 * refuse every other role on their own and stay the boundary. The role guard reads them so a caller
 * who reaches such a URL sees the "not available" screen rather than another role's chrome around
 * a refusal (issues #202 and #179).
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
          { path: '', name: 'accounts', component: AccountsListPage, meta: { module: 'accounts', title: 'accounts.list.heading', managerOnly: true } },
          { path: 'new', name: 'accounts-new', component: AccountFormPage, meta: { module: 'accounts', title: 'accounts.form.heading', managerOnly: true } },
          // Declared after 'new' so the literal segment wins: an :id route placed first would
          // swallow /accounts/new and try to load an account called "new".
          {
            path: ':id',
            name: 'account-detail',
            component: AccountDetailPage,
            props: true,
            meta: {
              module: 'accounts',
              title: 'accounts.detail.heading',
              managerOnly: true,
              navigationParent: 'accounts',
              titledByRecord: true,
            },
          },
        ],
      },
      {
        // Plans belong to the Accounts module — they are what an account is created against — so
        // they carry its `meta.module` and fall behind its licence. They are for administrators and
        // resellers: the endpoints refuse a customer, and `managerOnly` sends one to the "not
        // available" screen.
        path: '/plans',
        component: DefaultLayout,
        children: [
          { path: '', name: 'plans', component: PlansListPage, meta: { module: 'accounts', title: 'plans.list.heading', managerOnly: true } },
          { path: 'new', name: 'plans-new', component: PlanFormPage, meta: { module: 'accounts', title: 'plans.form.headingNew', managerOnly: true, navigationParent: 'plans' } },
          // After 'new', for the reason the accounts routes give.
          {
            path: ':id',
            name: 'plans-edit',
            component: PlanFormPage,
            props: true,
            meta: { module: 'accounts', title: 'plans.form.headingEdit', managerOnly: true, navigationParent: 'plans' },
          },
        ],
      },
      {
        // Resellers, managed by an administrator (issue #179). They belong to the Accounts module, so
        // they carry its `meta.module`. Administrators only: the endpoints refuse every other role,
        // and `adminOnly` sends a reseller or a customer to the "not available" screen.
        path: '/resellers',
        component: DefaultLayout,
        children: [
          { path: '', name: 'resellers', component: ResellersListPage, meta: { module: 'accounts', title: 'resellers.list.heading', adminOnly: true } },
          { path: 'new', name: 'resellers-new', component: ResellerFormPage, meta: { module: 'accounts', title: 'resellers.form.headingNew', adminOnly: true, navigationParent: 'resellers' } },
          // After 'new', for the reason the accounts routes give.
          {
            path: ':id',
            name: 'reseller-detail',
            component: ResellerDetailPage,
            props: true,
            meta: {
              module: 'accounts',
              title: 'resellers.detail.heading',
              adminOnly: true,
              navigationParent: 'resellers',
              titledByRecord: true,
            },
          },
          {
            path: ':id/profile',
            name: 'reseller-profile-edit',
            component: ResellerProfileFormPage,
            props: true,
            meta: { module: 'accounts', title: 'resellers.profile.heading', adminOnly: true, navigationParent: 'resellers' },
          },
          {
            path: ':id/edit',
            name: 'reseller-edit',
            component: ResellerFormPage,
            props: true,
            meta: { module: 'accounts', title: 'resellers.form.headingEdit', adminOnly: true, navigationParent: 'resellers' },
          },
          {
            // The reseller's API keys as its administrator oversees them (issue #435): the API keys screen
            // asked for this reseller's keys, which it lists and revokes but cannot create.
            path: ':id/api-keys',
            name: 'reseller-api-keys-oversight',
            component: ApiKeysPage,
            props: true,
            meta: { module: 'accounts', title: 'apiKeys.overseen.heading', adminOnly: true, navigationParent: 'resellers' },
          },
        ],
      },
      {
        // A reseller's own overview: its envelope and what it has used of it (issue #179). It is the
        // reseller's home, in place of the server dashboard, which is not theirs. The endpoint
        // (`GET /api/v1/reseller/me`) refuses every other role, and `resellerOnly` sends an
        // administrator or a customer who opens the address to the "not available" screen.
        path: '/reseller',
        component: DefaultLayout,
        children: [{ path: '', name: 'reseller-overview', component: ResellerOverviewPage, meta: { module: 'accounts', title: 'resellers.overview.heading', resellerOnly: true } }],
      },
      {
        // A reseller's own part of the audit journal: the entries about its accounts and its login
        // (issue #370). It is the administrator's audit page asking the reseller's endpoint
        // (`GET /api/v1/audit/reseller`), which refuses every other role; `resellerOnly` sends an
        // administrator or a customer who opens the address to the "not available" screen.
        path: '/reseller/audit',
        component: DefaultLayout,
        children: [{ path: '', name: 'reseller-audit', component: AuditPage, meta: { title: 'app.audit.heading', resellerOnly: true } }],
      },
      {
        path: '/sites',
        component: DefaultLayout,
        children: [
          { path: '', name: 'sites', component: SitesListPage, meta: { module: 'sites', title: 'sites.list.heading' } },
          { path: 'new', name: 'sites-new', component: SiteFormPage, meta: { module: 'sites', title: 'sites.form.heading' } },
          // The account's PHP settings (issue #138), one page for every site on PHP. A static segment
          // beside the UUID pattern below, which it can never match. The endpoint is the Php
          // feature's, so the navigation offers the item only to a plan that sells it.
          { path: 'php-settings', name: 'php-settings', component: PhpSettingsPage, meta: { module: 'sites', title: 'phpSettings.heading' } },
          // The UUID pattern keeps this route off /sites/new and off any other segment that
          // cannot name a site, so neither reaches the detail page.
          {
            path: SITE_ID_SEGMENT,
            name: 'site-detail',
            component: SiteDetailPage,
            props: true,
            meta: { module: 'sites', title: 'sites.detail.heading', navigationParent: 'sites', titledByRecord: true },
          },
        ],
      },
      {
        // Three routes, and no `:id` child: a database has no detail to open — the row is the
        // database, and both of its actions live on it. A deep link to a single one would lead to a
        // page that could only repeat the row it came from.
        path: '/databases',
        component: DefaultLayout,
        children: [
          { path: '', name: 'databases', component: DatabasesPage, meta: { module: 'databases', title: 'databases.list.heading' } },
          // Database users and their per-database privileges. Not admin-only: a customer manages the
          // users of their own account, and the endpoint scopes every read and write to it.
          {
            path: 'users',
            name: 'database-users',
            component: DatabaseUsersPage,
            meta: { module: 'databases', title: 'databases.users.list.heading' },
          },
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
        // The DNS module (issue #178). One zones list serves the administrator's "Zones" and the
        // customer's "My zones" (the navigation lists both entries under this one route), and a zone
        // has a detail page with its records. The name server settings are administrators only: the
        // endpoint refuses every other role, and `adminOnly` spares a customer an administrator's
        // screen around a refusal. Zone templates and DNSSEC are later phases of the issue and have
        // no route until their endpoints exist.
        path: '/dns',
        component: DefaultLayout,
        children: [
          { path: 'zones', name: 'dns-zones', component: DnsZonesListPage, meta: { module: 'dns', title: 'dns.zones.heading' } },
          {
            path: 'zones/new',
            name: 'dns-zone-new',
            component: DnsZoneFormPage,
            meta: { module: 'dns', title: 'dns.zoneForm.heading', navigationParent: 'dns-zones' },
          },
          // The UUID pattern keeps this route off /dns/zones/new and off any other segment that
          // cannot name a zone, so neither reaches the detail page.
          {
            path: DNS_ZONE_SEGMENT,
            name: 'dns-zone-detail',
            component: DnsZoneDetailPage,
            props: true,
            meta: { module: 'dns', title: 'dns.detail.heading', navigationParent: 'dns-zones', titledByRecord: true },
          },
          {
            path: 'nameservers',
            name: 'dns-nameservers',
            component: DnsNameserversPage,
            meta: { module: 'dns', title: 'dns.nameservers.heading', adminOnly: true },
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
          // The customer's own schedule (issue #147): it names no account, because the endpoint takes
          // it from the login. No role flag: the navigation offers it to customers only, and an
          // administrator who opens the address is answered "account not found" by the endpoint.
          {
            path: 'my-schedule',
            name: 'my-backup-schedule',
            component: MyBackupSchedulePage,
            meta: { module: 'backups', title: 'backups.mySchedule.heading' },
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
        // Two pages, one per module, and no `:id` child: a login has no detail to open — the row IS
        // the login. Each page is guarded by its own module, so a panel whose licence lacks FTPS
        // keeps its SFTP logins. The bare `/file-transfer` is where the single merged screen used
        // to live: every link the panel has rendered points there, so it forwards to the SFTP page,
        // the half every panel has, rather than turning into a 404.
        path: '/file-transfer',
        component: DefaultLayout,
        children: [
          // The router carries the query across a redirect, so `?account=` links keep their filter.
          { path: '', redirect: { name: 'file-transfer-sftp' } },
          { path: 'sftp', name: 'file-transfer-sftp', component: SftpUsersListPage, meta: { module: 'sftp', title: 'sftp.list.heading' } },
          { path: 'ftps', name: 'file-transfer-ftps', component: FtpUsersListPage, meta: { module: 'ftp', title: 'ftp.list.heading' } },
        ],
      },
      {
        // Three pages, one per list, and no `:id` child: a domain, a mailbox and an alias are each
        // the row that shows them, and what a domain needs published in DNS is a dialog off its row.
        // Each list keeps its page, search and sort in the URL, which two lists on one route would
        // fight over. Guarded by the `mail` module; the bare `/mail` forwards to the mailboxes, the
        // page a customer opens most.
        path: '/mail',
        component: DefaultLayout,
        children: [
          { path: '', redirect: { name: 'mailboxes' } },
          { path: 'domains', name: 'mail-domains', component: MailDomainsListPage, meta: { module: 'mail', title: 'mail.domains.heading' } },
          { path: 'mailboxes', name: 'mailboxes', component: MailboxesListPage, meta: { module: 'mail', title: 'mail.mailboxes.heading' } },
          { path: 'aliases', name: 'mail-aliases', component: MailAliasesListPage, meta: { module: 'mail', title: 'mail.aliases.heading' } },
        ],
      },
      {
        // The screen before the merged one. A customer's bookmark still points here, so the path
        // stays and forwards rather than turning into a 404.
        path: '/sftp-users',
        redirect: { name: 'file-transfer-sftp' },
      },
      {
        // Three pages, one per list, and no `:id` child: a rule has no identity beyond its port,
        // protocol and source range — the row IS the rule — and neither a ban nor a whitelist entry
        // is more than the row that shows it. The bans and the whitelist are pages of their own
        // because each is server-paged and keeps its page, search and sort in the URL, which two
        // lists on one route would fight over. The rules stay at the bare `/firewall`, where every
        // link the panel has rendered points.
        path: '/firewall',
        component: DefaultLayout,
        children: [
          { path: '', name: 'firewall', component: FirewallPage, meta: { module: 'firewall', title: 'firewall.heading', adminOnly: true } },
          { path: 'bans', name: 'firewall-bans', component: FirewallBansPage, meta: { module: 'firewall', title: 'firewall.bans.heading', adminOnly: true } },
          { path: 'whitelist', name: 'firewall-whitelist', component: FirewallWhitelistPage, meta: { module: 'firewall', title: 'firewall.whitelist.heading', adminOnly: true } },
        ],
      },
      {
        // The file manager: the browser at the bare `/files`, the text editor and the trash as pages of
        // their own. The folder (`?path=`) and the account (`?account=`) travel in the query, not in
        // the path, because a file name can hold any character and the editor must survive a reload
        // on the same file. The editor and the trash are children of the browser's menu item.
        path: '/files',
        component: DefaultLayout,
        children: [
          { path: '', name: 'file-manager', component: FileManagerPage, meta: { module: 'files', title: 'filesBrowser.heading' } },
          {
            path: 'edit',
            name: 'file-editor',
            component: FileEditPage,
            meta: { module: 'files', title: 'filesEditor.heading', navigationParent: 'file-manager' },
          },
          {
            path: 'trash',
            name: 'file-trash',
            component: FileTrashPage,
            meta: { module: 'files', title: 'filesTrash.heading', navigationParent: 'file-manager' },
          },
        ],
      },
      {
        // A cron entry has no detail to open: the row IS the entry, and what its last run left behind
        // costs a privileged read per entry, so it is a dialog opened on demand. The history of the runs
        // started by hand is the one page beyond the list, because it is a server-paged list of its own.
        path: '/cron',
        component: DefaultLayout,
        children: [
          { path: '', name: 'cron', component: CronPage, meta: { module: 'cron', title: 'cron.list.heading' } },
          {
            path: ':entryId/runs',
            name: 'cron-runs',
            component: CronRunsPage,
            meta: { module: 'cron', title: 'cron.runs.heading' },
          },
        ],
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
        // Servers > Updates lives under /servers, not /monitoring: the menu groups it with the
        // server pages, and the module that answers it is still monitoring (the agent's package
        // cache is read through the monitoring controller).
        path: '/servers/updates',
        component: DefaultLayout,
        children: [
          { path: '', name: 'servers-updates', component: PendingUpdatesPage, meta: { module: 'monitoring', title: 'monitoring.updates.heading', adminOnly: true } },
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
        // The signed-in person's own profile, for every role: the USER, not a hosting account. No
        // `meta.module` (it is not a licensed module) and no role flag — an administrator and a
        // customer each read their own profile off their own token.
        path: '/profile',
        component: DefaultLayout,
        children: [{ path: '', name: 'profile', component: ProfilePage, meta: { title: 'app.profile.heading' } }],
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
          // The account owner's team (issue #315). Not a licensed module and not `adminOnly`: the
          // endpoints refuse everyone but the account's owner, and the page renders that refusal.
          { path: 'sub-users', name: 'sub-users', component: SubUsersPage, meta: { title: 'subUsers.heading' } },
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
          {
            path: 'two-factor',
            name: 'two-factor',
            component: TwoFactorSettingsPage,
            // The recovery codes live in the page's state and are shown once: a language switch
            // must not remount it (issue #414).
            meta: { title: 'app.twoFactor.heading', holdsOneTimeSecret: true },
          },
          // Administrator-only screens: their endpoints refuse a customer, and
          // `adminOnly` keeps a customer on the "not available" screen instead.
          // Neither carries `meta.module` — the security policy belongs to the panel
          // itself, and mail settings are what alerting AND password reset both
          // depend on, so hiding them behind a licence would disable the panel's
          // ability to send a reset link.
          // Administrator-managed API keys (issue #144), the Identity module's own screen.
          { path: 'api-keys', name: 'api-keys', component: ApiKeysPage, meta: { module: 'identity', title: 'apiKeys.heading', adminOnly: true } },
          // A reseller's own API keys (issue #435): the same screen asking the reseller's endpoint, which
          // refuses every other role; `resellerOnly` sends an administrator or a customer to the "not available" screen.
          { path: 'reseller-api-keys', name: 'reseller-api-keys', component: ApiKeysPage, meta: { title: 'apiKeys.heading', resellerOnly: true } },
          { path: 'security-policy', name: 'security-policy', component: SecurityPolicyPage, meta: { title: 'app.securityPolicy.heading', adminOnly: true } },
          // Who is told about an alert and at what level (issue #142); administrators only, like the mail it rides on.
          { path: 'alerts', name: 'alert-settings', component: AlertSettingsPage, meta: { title: 'alertSettings.heading', adminOnly: true } },
          { path: 'smtp', name: 'smtp-settings', component: SmtpSettingsPage, meta: { title: 'app.smtp.heading', adminOnly: true } },
          // The branding a reseller gives its customers' shell (issue #367). The endpoints refuse every other
          // role, and `resellerOnly` sends an administrator or a customer to the "not available" screen.
          { path: 'branding', name: 'reseller-branding', component: ResellerBrandingPage, meta: { title: 'branding.heading', resellerOnly: true } },
          // The panel's own certificate (issue #132): a page of its own under Settings in the menu
          // (`navigationTree.ts`, issue #311), which places it in the breadcrumb too.
          {
            path: 'panel-certificate',
            name: 'panel-certificate',
            component: PanelCertificatePage,
            meta: { title: 'panelCertificate.heading', adminOnly: true },
          },
          // The panel's own release state and the update action (issue #382): administrators only,
          // the endpoint refuses everyone else whatever this flag says.
          {
            path: 'panel-update',
            name: 'panel-update',
            component: PanelUpdatePage,
            meta: { title: 'panelUpdate.heading', adminOnly: true },
          },
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
        // Where the role guard sends a caller who reached another role's screen, and where
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
