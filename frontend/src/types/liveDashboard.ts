import type { AuditEvent } from './audit'
import type { DashboardAttention, DashboardCounts, DashboardSection } from './dashboard'
import type { ServiceStatus } from './monitoring'

/**
 * Where the live dashboard's connection stands, as the viewer is told it.
 *
 * - `connecting`: the first connection of this visit has been opened and nothing has arrived yet.
 * - `live`: frames are arriving.
 * - `reconnecting`: the connection ended or broke, and the next attempt is waiting or in flight.
 * - `paused`: the tab is hidden, so the connection was closed on purpose; it reopens on return.
 * - `stopped`: nothing is open (not started yet, closed, or the panel refused the stream).
 */
export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'paused' | 'stopped'

/** Who the stream was opened for, mirroring the `audience` of the `hello` frame. */
export type LiveAudience = 'administrator' | 'customer'

/**
 * The first frame of every stream, mirroring the backend's `hello` event. `protocol` is the
 * additive version of the frame vocabulary: a frame this SPA does not know is ignored, never an
 * error.
 */
export interface LiveHello {
  /** Version of the frame vocabulary; 1 today. */
  protocol: number
  /** Who the panel decided the stream is for. */
  audience: LiveAudience
  /** Milliseconds between two `metrics` frames. */
  intervalMs: number
  /** The panel's clock when the stream opened, an ISO 8601 instant. */
  serverTime: string
  /** Seconds after which the panel ends the stream and the SPA reopens it silently. */
  maxLifetimeSec: number
}

/**
 * One reading of the host, mirroring the backend's `metrics` frame (and each member of `history`).
 *
 * Members that arrive in later releases (per-core load, swap, per-mount figures) are not declared
 * here: this SPA draws only what it declares. A declared figure the agent does not report is
 * `null`, never zero.
 */
export interface LiveMetricsPoint {
  /** When the reading was taken, an ISO 8601 instant. */
  t: string
  /** The processor. */
  cpu: {
    /** Utilisation across all cores, 0.0-100.0. */
    total: number
    /**
     * Load averages over 1, 5 and 15 minutes. An entry is `null` when the source has none: a
     * reading rebuilt from a stored sample carries the 1 minute figure only.
     */
    load: (number | null)[]
  }
  /** Memory. */
  memory: {
    /** Bytes in use. */
    used: number
    /** Bytes installed. */
    total: number
  }
  /** The root filesystem. */
  disk: {
    /** Bytes in use; `null` when the reading does not carry it, never zero. */
    used: number | null
    /** Size of the filesystem, bytes. */
    total: number
  }
  /** Network throughput summed over the physical interfaces. */
  network: {
    /** Bytes per second received; `null` on the first reading, which has nothing to measure against. */
    rxBps: number | null
    /** Bytes per second sent; `null` on the first reading. */
    txBps: number | null
  }
}

/** The `history` frame: the readings the panel holds, oldest first, capped at its ring size. */
export interface LiveHistory {
  /** The readings. */
  points: LiveMetricsPoint[]
}

/** The `services` frame: the watched services, or the fact that they could not be read. */
export interface LiveServices {
  /** The services as a dashboard section. */
  services: DashboardSection<ServiceStatus[]>
}

/**
 * The `totals` frame: what the server holds and what asks to be looked at, pushed when it changes.
 * Both members are the shapes the one-shot dashboard read answers with: the stream serializes with
 * the same rules as every response, so a null member is `null` here exactly as it is there.
 */
export interface LiveTotals {
  /** Accounts, sites and databases. */
  counts: DashboardCounts
  /** What needs looking at. */
  attention: DashboardAttention
}

/**
 * The `activity` frame: the newest journal entries, pushed when they change.
 */
export interface LiveActivity {
  /** The newest audit entries. */
  recentAudit: DashboardSection<AuditEvent[]>
}

/** Why the panel ended a stream, from the `end` frame. */
export type LiveEndReason = 'renew' | 'shutdown'

/** Where each frame of the live stream is delivered. A frame of an unknown name is dropped before it gets here. */
export interface LiveDashboardHandlers {
  /** The stream opened. */
  onHello: (hello: LiveHello) => void
  /** The readings the panel holds (everything after `since` on a reconnect). */
  onHistory: (history: LiveHistory) => void
  /** One new reading. */
  onMetrics: (point: LiveMetricsPoint) => void
  /** The watched services changed (or the 30 second refresh). */
  onServices: (services: LiveServices) => void
  /** The totals changed. */
  onTotals: (totals: LiveTotals) => void
  /** The newest activity changed. */
  onActivity: (activity: LiveActivity) => void
  /** The panel is ending the stream. */
  onEnd: (reason: LiveEndReason) => void
}
