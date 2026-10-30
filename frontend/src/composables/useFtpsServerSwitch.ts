import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useFirewallStore } from '../stores/firewall'
import { useFtpsServerStore } from '../stores/ftpsServer'
import type { FirewallRule } from '../types/firewall'
import type { EnableFtpsRequest, FtpsAvailability, FtpsStatus } from '../types/ftpsStatus'

/**
 * What a page hands {@link useFtpsServerSwitch} to tell the operator how a switch ended: the
 * sentence, and whether the state the panel measured afterwards agrees with what was asked for. The
 * page picks the tone from that fact; the tone is the toast's vocabulary, not this composable's.
 */
export type FtpsSwitchAnnouncer = (text: string, agreed: boolean) => void

/** The FTPS service card's state and the actions behind its buttons. */
export interface FtpsServerSwitch {
  /** What this caller has been told about the daemon: on, off, or not said. */
  availability: ComputedRef<FtpsAvailability>
  /**
   * Why the FTPS status could not be read, worded — the backend's own sentence or the generic one —
   * or `null` when it was read (or, for a customer, deliberately not disclosed).
   */
  loadFailureText: ComputedRef<string | null>
  /**
   * Reads the daemon's status and the firewall, for an administrator only.
   * @returns Resolves once both reads have settled.
   */
  load: () => Promise<void>
  /**
   * Turns the daemon on, re-reads the firewall, and announces what the panel measured afterwards.
   * @param request The hostname and passive address the service card collected.
   * @returns Resolves once both requests have settled.
   */
  enable: (request: EnableFtpsRequest) => Promise<void>
  /**
   * Turns the daemon off, re-reads the firewall, and announces what the panel measured afterwards.
   * @returns Resolves once both requests have settled.
   */
  disable: () => Promise<void>
  /**
   * Installs the rules the daemon needs.
   * @param rules The rules the service card derived from the backend-supplied ports.
   * @returns Resolves once the change has settled.
   */
  openPorts: (rules: FirewallRule[]) => Promise<void>
  /**
   * Removes the rules a stopped daemon left behind.
   * @param rules The rules the service card derived from the backend-supplied ports.
   * @returns Resolves once the change has settled.
   */
  closePorts: (rules: FirewallRule[]) => Promise<void>
}

/**
 * The FTPS service card's orchestration: the daemon's status store and the firewall store, worked
 * together the way the card needs them.
 *
 * **Every switch answers.** Turning FTPS on or off is announced in words chosen from the state the
 * panel measured AFTER the change, never from which button was pressed — "turned off, but the service
 * is still running" is a different sentence from "turned off", and a success toast over the first
 * would be a silent button in a friendlier colour. A failed switch is shown on the card itself by
 * the store's `actionFailure`, so it is not announced here.
 *
 * **The firewall is re-read after every switch, whether it succeeded or not:** both of the card's
 * firewall warnings depend on the daemon's new state, and a warning computed against the old one is
 * the screen disagreeing with itself. Turning the daemon off deliberately leaves the firewall alone;
 * the lingering-ports warning is what makes the ports a stopped daemon left open visible.
 *
 * Both reads are administrator-only endpoints; {@link FtpsServerSwitch.load} is called for an
 * administrator only, and a customer's availability is `unknown`, on which the create form errs
 * permissive (rules/security.md: enforcement is the backend's).
 * @param announce Shows the operator a sentence, told whether the measured state agrees.
 * @returns The card's state and actions.
 */
export const useFtpsServerSwitch = (announce: FtpsSwitchAnnouncer): FtpsServerSwitch => {
  const { t } = useI18n()
  const ftpsServerStore = useFtpsServerStore()
  const firewallStore = useFirewallStore()

  const availability: ComputedRef<FtpsAvailability> = computed(() => {
    if (!ftpsServerStore.isDisclosed || ftpsServerStore.status === null) {
      return 'unknown'
    }
    return ftpsServerStore.status.desiredState === 'on' ? 'available' : 'off'
  })

  const loadFailureText: ComputedRef<string | null> = computed(() => {
    const failure = ftpsServerStore.loadFailure
    if (failure === null) {
      return null
    }
    return failure.kind === 'server' ? failure.message : t('ftp.server.feedback.unexpectedFailure')
  })

  /**
   * Words the outcome of a switch from the backend's verdict on the state it measured.
   * @param status The status the switch answered with.
   * @returns The sentence to show.
   */
  const outcomeText = (status: FtpsStatus): string => {
    switch (status.agreement) {
      case 'runningButOff':
        return t('ftp.server.feedback.disabledStillRunning')
      case 'onButNotRunning':
        return t('ftp.server.feedback.enabledNotRunning')
      case 'runningButNotAnswering':
        return t('ftp.server.feedback.enabledNotAnswering')
      case 'consistent':
        break
    }
    if (status.desiredState === 'on') {
      return t('ftp.server.feedback.enabled', { port: status.controlPort })
    }
    return status.desiredState === 'off' ? t('ftp.server.feedback.disabled') : t('ftp.server.feedback.stopped')
  }

  /**
   * Re-reads the firewall after a switch, then announces what the switch answered with.
   * @param accepted Whether the panel answered the switch with a status.
   * @returns Resolves once the firewall has been re-read.
   */
  const settle = async (accepted: boolean): Promise<void> => {
    await firewallStore.load()
    const status = ftpsServerStore.status
    if (accepted && status !== null) {
      announce(outcomeText(status), status.agreement === 'consistent')
    }
  }

  /**
   * Reads the daemon's status and the firewall.
   * @returns Resolves once both reads have settled.
   */
  const load = async (): Promise<void> => {
    await Promise.all([ftpsServerStore.load(), firewallStore.load()])
  }

  /**
   * Turns the daemon on and settles the switch.
   * @param request The hostname and passive address.
   * @returns Resolves once both requests have settled.
   */
  const enable = async (request: EnableFtpsRequest): Promise<void> => {
    await settle(await ftpsServerStore.enable(request))
  }

  /**
   * Turns the daemon off and settles the switch.
   * @returns Resolves once both requests have settled.
   */
  const disable = async (): Promise<void> => {
    await settle(await ftpsServerStore.disable())
  }

  /**
   * Installs the rules the daemon needs.
   * @param rules The rules to allow.
   * @returns Resolves once the change has settled.
   */
  const openPorts = async (rules: FirewallRule[]): Promise<void> => {
    await firewallStore.allowPorts(rules)
  }

  /**
   * Removes the rules a stopped daemon left behind.
   * @param rules The rules to deny.
   * @returns Resolves once the change has settled.
   */
  const closePorts = async (rules: FirewallRule[]): Promise<void> => {
    await firewallStore.denyPorts(rules)
  }

  return { availability, loadFailureText, load, enable, disable, openPorts, closePorts }
}
