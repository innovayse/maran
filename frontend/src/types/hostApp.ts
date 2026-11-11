import type { ListRequest, Paged } from './list'

/**
 * What an app is for, as the panel's closed catalogue names it (`docs/apps/README.md`): a site web
 * server (the process that owns ports 80 and 443 for hosted sites) or a runtime sites run on (PHP,
 * Node.js). The API serializes enums as camelCase strings.
 */
export type HostAppKind = 'webServer' | 'runtime'

/**
 * Where an installed app stands. `installing` and `removing` are transient; `failed` is an install
 * that did not finish and is shown with a way to try again or remove what is left.
 */
export type HostAppState = 'installing' | 'installed' | 'failed' | 'removing'

/**
 * What the app's service is doing right now, probed from the host rather than remembered. `none` is
 * an app with no service of its own (a runtime that is only a binary); `unknown` is a probe that
 * could not tell, which is not the same answer as `inactive`.
 */
export type HostAppServiceStatus = 'unknown' | 'active' | 'inactive' | 'failed' | 'none'

/** What the panel can ask an app's service to do (`POST /api/v1/apps/{id}/service/{action}`). */
export type HostAppServiceAction = 'start' | 'stop' | 'restart' | 'reload'

/**
 * The site web server of a host, as the stack names it. Exactly one can be installed at a time.
 */
export type SiteWebServer = 'nginx' | 'apache'

/**
 * One app installed on this host, mirroring the backend's app view field-for-field. The ledger of
 * what the panel installed is reconciled against the host at start, so the service status is the
 * host's own answer.
 */
export interface HostApp {
  /** The catalogue id: `nginx`, `apache`, `php-<version>` or `nodejs-<major>`; the only identifier a request may name. */
  appId: string
  /** The app as an operator reads it, in the request's language (`PHP 8.3`). */
  name: string
  /** What the app is for. */
  kind: HostAppKind
  /** The kind as an operator reads it, in the request's language. */
  kindName: string
  /** The version the package sources installed. */
  version: string
  /** Where the install stands. */
  state: HostAppState
  /** The state as an operator reads it, in the request's language. */
  stateName: string
  /** What the app's service is doing. */
  serviceStatus: HostAppServiceStatus
  /** The service status as an operator reads it, in the request's language. */
  serviceStatusName: string
  /** The instant the app was installed, as an ISO-8601 string, or `null` while an install has not finished. */
  installedAt: string | null
  /** The task carrying an install or removal still in flight, or `null` once it settled; a reload follows it again. */
  taskId: string | null
}

/**
 * One app the host's package sources can install (`GET /api/v1/apps/installable`). The set is
 * whatever the sources offer; the SPA carries no list of apps or versions of its own.
 */
export interface InstallableHostApp {
  /** The catalogue id; the only identifier an install request may name. */
  appId: string
  /** The app as an operator reads it, in the request's language. */
  name: string
  /** What the app is for. */
  kind: HostAppKind
  /** The kind as an operator reads it, in the request's language. */
  kindName: string
  /** Whether the host already has it installed. */
  installed: boolean
}

/**
 * Whether the host is ready for sites, and which runtimes sites can use (`GET /api/v1/apps/stack`,
 * readable by any signed-in role). The backend's stack gate is the enforcement; the SPA only
 * reflects it.
 */
export interface StackStatus {
  /** Whether a site web server is installed. While false the panel refuses to create or change sites. */
  isReady: boolean
  /** The installed site web server, or `null` while none is. */
  webServer: SiteWebServer | null
  /** The installed PHP versions, e.g. `8.3`. */
  phpVersions: string[]
  /** The installed Node.js majors, e.g. `22`. */
  nodeVersions: string[]
}

/** The families of app the wizard and the site form tell apart, derived from an app id. */
export type HostAppFamily = 'webServer' | 'php' | 'nodejs'

/**
 * Why the panel refused to remove an app that sites still use: the localized sentence, how many
 * sites use it in all and the domains of the first of them, read from the refusal's `sites`
 * extension member. The panel lists a bounded number of domains, so `total` can exceed the list.
 */
export interface HostAppInUse {
  /** The app the refusal is about. */
  appId: string
  /** The panel's sentence, already localized. */
  message: string
  /** The domains of the sites that use the app, as many as the panel listed. */
  sites: string[]
  /** How many sites use the app in all. */
  total: number
}

/** What a service control answers: the service's state after the action, as the panel probed it. */
export interface HostAppServiceState {
  /** What the service is doing now. */
  serviceStatus: HostAppServiceStatus
  /** The status as an operator reads it, in the request's language. */
  serviceStatusName: string
}

/** What an install or an uninstall answers (202): the task that carries its progress, as the marketplace's operations do. */
export interface HostAppOperationAccepted {
  /** The task whose progress and result appear in the task list. */
  taskId: string
}

/** The calls the apps endpoints support; called from the stores only (rules/vue.md). */
export interface HostAppsApi {
  /**
   * Lists one page of the apps installed on the host (administrators only).
   * @param request The page, size, search, sort and the `kind` and `state` filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<HostApp>>

  /**
   * Lists the apps the host's package sources can install (administrators only).
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Every installable app, installed ones flagged.
   */
  listInstallable: (signal?: AbortSignal) => Promise<InstallableHostApp[]>

  /**
   * Reads the stack status (any signed-in role).
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether sites can exist yet, and which runtimes they can use.
   */
  stack: (signal?: AbortSignal) => Promise<StackStatus>

  /**
   * Starts an install as a recorded task (administrators only).
   * @param id The catalogue id to install.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The accepted operation, naming the task that carries the install.
   */
  install: (id: string, signal?: AbortSignal) => Promise<HostAppOperationAccepted>

  /**
   * Starts an uninstall as a recorded task (administrators only). Refused while sites use the app.
   * @param id The catalogue id to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The accepted operation, naming the task that carries the removal.
   */
  uninstall: (id: string, signal?: AbortSignal) => Promise<HostAppOperationAccepted>

  /**
   * Starts, stops, restarts or reloads an app's service (administrators only).
   * @param id The catalogue id.
   * @param action What to ask the service to do.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The service's state after the action.
   */
  control: (id: string, action: HostAppServiceAction, signal?: AbortSignal) => Promise<HostAppServiceState>
}
