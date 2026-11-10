import { BRIDGE_VERSION } from '../constants/bridgeLimits'
import type { ShellMessage } from '../types/moduleBridge'
import { isRecord } from './isRecord'

/**
 * Validates a message the shell sent over the port.
 *
 * @param data The message data received from the port.
 * @returns The message, or null when it is not one of `api-result`, `locale` or `theme` in the right shape.
 */
export const parseShellMessage = (data: unknown): ShellMessage | null => {
  if (!isRecord(data) || data.v !== BRIDGE_VERSION) {
    return null
  }

  if (data.type === 'api-result') {
    if (typeof data.id !== 'string' || typeof data.status !== 'number' || !Number.isInteger(data.status)) {
      return null
    }

    return { v: 1, type: 'api-result', id: data.id, status: data.status, body: data.body }
  }

  if (data.type === 'locale') {
    return typeof data.locale === 'string' && data.locale.length > 0
      ? { v: 1, type: 'locale', locale: data.locale }
      : null
  }

  if (data.type === 'theme') {
    return data.theme === 'dark' || data.theme === 'light' ? { v: 1, type: 'theme', theme: data.theme } : null
  }

  return null
}
