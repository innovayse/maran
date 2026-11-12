/**
 * The module marketplace (issues #186, #187), from the angles the Marketplace screens need it
 * (rules/vue.md "Types" — one domain per file). Every shape mirrors the panel's
 * `api/v1/marketplace` contract field for field. Every text that describes a server-side thing (a
 * module's name and summary, a state's name, a reason an install is refused, a "what is sent" line)
 * arrives already localized in the request's language and is rendered as received; the SPA derives
 * no permission from any of it, `canInstall` and its siblings are the panel's answer.
 */

/** Where a module stands in the catalogue relative to this panel. */
export type MarketplaceModuleState = 'available' | 'installed' | 'updateAvailable' | 'revoked' | 'incompatible'

/** Whether the panel's licence entitles the administrator to install a module. */
export type MarketplaceEntitlementState = 'notRequired' | 'entitled' | 'notPurchased' | 'leaseExpired' | 'unknown'

/** Where an installed module is in the panel's lifecycle. */
export type InstalledModuleLifecycle = 'installed' | 'active' | 'degraded' | 'failed' | 'removed'

/**
 * What `GET /api/v1/marketplace/status` and `PUT /api/v1/marketplace/settings` answer: the
 * marketplace switch and the facts the switch card prints. This is the only marketplace read the SPA
 * makes while the switch is off.
 */
export interface MarketplaceStatus {
  /** Whether the marketplace is switched on. Off by default. */
  enabled: boolean
  /** Whether a marketplace host is configured on this server. */
  hostConfigured: boolean
  /** The marketplace host name only (no scheme or path), or `null` when none is configured. */
  host: string | null
  /** The "what is sent" lines, localized by the panel, printed on the switch card. */
  sentItems: string[]
  /** Ids of the modules staged by an install or update and waiting for a restart of the panel. */
  restartRequiredModules: string[]
}

/** A price as the marketplace states it. */
export interface MarketplacePrice {
  /** The price in the words to print, localized by the panel. */
  display: string
  /** The cabinet address to buy at; present only when the panel marked it a trusted `https` link, else `null`. */
  purchaseUrl: string | null
}

/** A module's licence standing for this panel. */
export interface MarketplaceEntitlement {
  /** The machine-stable state; it picks a tone, never a text. */
  state: MarketplaceEntitlementState
  /** The state as an operator reads it, localized by the panel. */
  stateDisplayName: string
  /** Until when the entitlement holds, as an ISO 8601 timestamp, or `null`. */
  until: string | null
}

/** One module of the catalogue, as the list and the detail page's header show it. */
export interface MarketplaceModule {
  /** The module's machine id; the only identifier a request may name. */
  id: string
  /** The module's name, localized by the panel. */
  displayName: string
  /** One-line description, plain text. */
  summary: string
  /** The publisher's display name, as the panel prints it; never an identifier. */
  publisher: string
  /** The publisher's stable id, kept for reference and never printed; `null` when the service sent none. */
  publisherId: string | null
  /** The licence tier, machine-stable. */
  tier: string
  /** The tier as an operator reads it, localized by the panel. */
  tierDisplayName: string
  /** The price, or `null` for a module that has none. */
  price: MarketplacePrice | null
  /** The newest version the catalogue offers, or `null` when it offers none. */
  latestVersion: string | null
  /** The version installed on this panel, or `null` when none is. */
  installedVersion: string | null
  /** Where the module stands relative to this panel. */
  state: MarketplaceModuleState
  /** The licence standing. */
  entitlement: MarketplaceEntitlement
  /** The panel's verdict on whether an install may be started now; the SPA never derives it. */
  canInstall: boolean
  /** Why an install is refused, localized by the panel; `null` while `canInstall` is true. */
  cannotInstallMessage: string | null
  /** True when the module is staged and waits for a restart of the panel. */
  restartRequired: boolean
}

/**
 * What `GET /api/v1/marketplace/catalogue` answers: one page of the catalogue and whether its
 * signature checked out. An unverified catalogue is still a 200 so the installed list can be shown.
 */
export interface MarketplaceCatalogue {
  /** This page's modules. */
  items: MarketplaceModule[]
  /** The cursor of the next page, or `null` on the last one. */
  nextCursor: string | null
  /** Whether the catalogue's signature was verified. When false, installing is refused. */
  verified: boolean
  /** When the panel fetched the catalogue, as an ISO 8601 timestamp, or `null`. */
  fetchedAt: string | null
  /** Why the catalogue is not verified, localized by the panel; `null` while `verified` is true. */
  unverifiedMessage: string | null
}

/** One published version of a module. */
export interface MarketplaceVersion {
  /** The version. */
  version: string
  /** The oldest panel version it runs on. */
  minPanelVersion: string
  /** The newest panel version it runs on, or `null` for no upper bound. */
  maxPanelVersion: string | null
  /** The package size in bytes. */
  sizeBytes: number
  /** When it was published, as an ISO 8601 timestamp. */
  publishedAt: string
  /** The release notes, plain text, never markup. */
  releaseNotes: string
  /** True when the marketplace withdrew this version. */
  revoked: boolean
  /** Why it was withdrawn, localized by the panel; `null` unless `revoked`. */
  revokedMessage: string | null
  /** The panel's verdict on whether this version may be installed now. */
  installable: boolean
  /** Why it may not be installed, localized by the panel; `null` while `installable`. */
  notInstallableMessage: string | null
}

/** A named thing a module declares it needs (an agent capability, a permission). */
export interface ModuleDeclaration {
  /** The machine-stable key. */
  key: string
  /** The declaration as an operator reads it, localized by the panel. */
  displayName: string
}

/**
 * The panel's copy of the signed manifest, summarised for the install acknowledgement. It is the
 * server's text, never text from the package's own bundle.
 */
export interface ModuleManifestSummary {
  /** The publisher's display name, as the dialog prints it; never an identifier. */
  publisher: string
  /** The publisher's stable id, kept for reference and never printed; `null` when the service sent none. */
  publisherId: string | null
  /** The licence tier as an operator reads it. */
  tierDisplayName: string
  /** The oldest panel version the module runs on. */
  minPanelVersion: string
  /** The database schema the module owns, or `null` when it has none. */
  schemaName: string | null
  /** Ids of the modules it needs. */
  dependencies: string[]
  /** The parts of the server's root agent the module reaches. */
  agentCapabilities: ModuleDeclaration[]
  /** The permissions the module declares. */
  permissions: ModuleDeclaration[]
}

/** What `GET /api/v1/marketplace/modules/{id}` answers: the module, its versions and its manifest. */
export interface MarketplaceModuleDetail {
  /** The module, as the list shows it. */
  module: MarketplaceModule
  /** Every published version, newest first. */
  versions: MarketplaceVersion[]
  /** The manifest summary, or `null` when the panel holds none to show. */
  manifest: ModuleManifestSummary | null
}

/** One module installed on this panel, from `GET /api/v1/marketplace/installed`. */
export interface InstalledModule {
  /** The module's machine id. */
  id: string
  /** The module's name, localized by the panel. */
  displayName: string
  /** The installed version. */
  version: string
  /** Where it is in the panel's lifecycle. */
  lifecycle: InstalledModuleLifecycle
  /** Why it is degraded or failed, localized by the panel; `null` otherwise. */
  failureMessage: string | null
  /** Where it came from, localized by the panel (bundled with the panel, installed from the marketplace). */
  origin: string
  /** When it was installed, as an ISO 8601 timestamp, or `null` for a module bundled with the panel. */
  installedAt: string | null
  /** The version a rollback would restore, or `null`. */
  previousVersion: string | null
  /** True when it is staged and the panel has not yet been restarted to start it. */
  restartRequired: boolean
  /** True when it was removed but its data is kept. */
  dataKept: boolean
  /** True when the marketplace withdrew the installed version. */
  revoked: boolean
  /** The panel's verdict on whether a rollback may be started. */
  canRollback: boolean
  /** The panel's verdict on whether a removal may be started. */
  canRemove: boolean
}

/** One module with a newer version available, from `GET /api/v1/marketplace/updates`. */
export interface ModuleUpdate {
  /** The module's machine id. */
  id: string
  /** The module's name, localized by the panel. */
  displayName: string
  /** The installed version. */
  installedVersion: string
  /** The version an update would install, or `null` when none is offered. */
  availableVersion: string | null
  /** True when the installed version was withdrawn. */
  revoked: boolean
  /** Why it was withdrawn, localized by the panel; `null` unless `revoked`. */
  revokedMessage: string | null
  /** The panel's verdict on whether an update may be started. */
  canUpdate: boolean
  /** Why it may not be started, localized by the panel; `null` while `canUpdate`. */
  cannotUpdateMessage: string | null
}

/** What `GET /api/v1/marketplace/updates` answers: the update check. */
export interface ModuleUpdates {
  /** The modules with a newer version. */
  items: ModuleUpdate[]
  /** Whether the answer's signature was verified. */
  verified: boolean
  /** When the check ran, as an ISO 8601 timestamp, or `null`. */
  checkedAt: string | null
  /** Why the answer is not verified, localized by the panel; `null` while `verified` is true. */
  unverifiedMessage: string | null
}

/** What an install, update, remove or rollback answers (202): the task that carries its progress. */
export interface ModuleOperationAccepted {
  /** The task whose progress and result appear in the task list. */
  taskId: string
}

/** The kind of operation a task carries, as the panel names its task kinds. */
export type ModuleOperationKind = 'ModuleInstall' | 'ModuleUpdate' | 'ModuleRemove' | 'ModuleRollback'

/** The body of `POST /api/v1/marketplace/modules/{id}/install`. */
export interface InstallModuleRequest {
  /** The version to install. */
  version: string
  /** True once the administrator ticked the acknowledgement of what the module can reach. */
  acknowledged: boolean
  /** The administrator's current password, re-entered; the panel checks it. */
  password: string
}

/** The body of `POST /api/v1/marketplace/modules/{id}/update`; the same shape as an install. */
export type UpdateModuleRequest = InstallModuleRequest

/** The body of `POST /api/v1/marketplace/modules/{id}/remove`. The module's data is kept. */
export interface RemoveModuleRequest {
  /** The administrator's current password, re-entered. */
  password: string
}

/** The body of `POST /api/v1/marketplace/modules/{id}/rollback`; the same shape as a removal. */
export type RollbackModuleRequest = RemoveModuleRequest

/** The body of `PUT /api/v1/marketplace/settings`. */
export interface SetMarketplaceEnabledRequest {
  /** The new position of the switch. */
  enabled: boolean
  /** The administrator's current password, re-entered. */
  password: string
}

/** How a wait for a restarted panel ended: the state changed, two minutes passed, or a newer wait took over. */
export type ModuleWaitOutcome = 'changed' | 'timeout' | 'cancelled'

/** What the marketplace store's `loadCatalogue` is asked for. */
export interface CatalogueQuery {
  /** The search text; omitted keeps the one last used, an empty string clears it. */
  search?: string
  /**
   * True (the default) empties the held list first, so the screen shows its loading state; false
   * keeps the held rows on screen until the fresh first page arrives (a silent refresh).
   */
  reset?: boolean
}

/** Public surface of the marketplace API composable: typed calls and nothing else. */
export interface MarketplaceApi {
  /** Reads the switch and the facts of the switch card. The only call made while the switch is off. */
  status: (signal?: AbortSignal) => Promise<MarketplaceStatus>
  /** Moves the switch; answers the new status. */
  setEnabled: (request: SetMarketplaceEnabledRequest, signal?: AbortSignal) => Promise<MarketplaceStatus>
  /** Reads one page of the catalogue; `cursor` is the previous page's `nextCursor`. */
  listCatalogue: (
    channel: string | null,
    search: string,
    cursor: string | null,
    signal?: AbortSignal,
  ) => Promise<MarketplaceCatalogue>
  /** Reads one module with its versions and manifest summary. */
  getModule: (id: string, signal?: AbortSignal) => Promise<MarketplaceModuleDetail>
  /** Lists the modules installed on this panel. */
  listInstalled: (signal?: AbortSignal) => Promise<InstalledModule[]>
  /** Checks for newer versions of the installed modules. */
  checkUpdates: (signal?: AbortSignal) => Promise<ModuleUpdates>
  /** Starts an install; answers the task that carries it. */
  install: (id: string, request: InstallModuleRequest, signal?: AbortSignal) => Promise<ModuleOperationAccepted>
  /** Starts an update; answers the task that carries it. */
  update: (id: string, request: UpdateModuleRequest, signal?: AbortSignal) => Promise<ModuleOperationAccepted>
  /** Starts a removal; answers the task that carries it. */
  remove: (id: string, request: RemoveModuleRequest, signal?: AbortSignal) => Promise<ModuleOperationAccepted>
  /** Starts a rollback; answers the task that carries it. */
  rollback: (id: string, request: RollbackModuleRequest, signal?: AbortSignal) => Promise<ModuleOperationAccepted>
  /** Restarts the panel so a staged module starts; the answer has no body. */
  restart: (id: string, signal?: AbortSignal) => Promise<void>
}

/** The operations a module can be put through, as the confirmation dialog names them. */
export type ModuleOperationName = 'install' | 'update' | 'remove' | 'rollback'

/** What a screen asks the confirmation dialog to confirm and start. */
export interface ModuleOperationRequest {
  /** Which operation. */
  kind: ModuleOperationName
  /** The module's machine id. */
  id: string
  /** The module's name, localized by the panel, for the dialog's heading. */
  name: string
  /** The version to install or update to; `null` takes the newest the catalogue offers. */
  version: string | null
}
