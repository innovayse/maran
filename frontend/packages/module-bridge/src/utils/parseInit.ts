import { BRIDGE_VERSION } from '../constants/bridgeLimits'
import type { ModuleInit } from '../types/moduleBridge'
import { isRecord } from './isRecord'

/**
 * Validates the shell's `init` message.
 *
 * @param data The message data received from the parent window.
 * @returns The init data, or null when the message is not a well-formed `init`.
 */
export const parseInit = (data: unknown): ModuleInit | null => {
  if (!isRecord(data) || data.v !== BRIDGE_VERSION || data.type !== 'init') {
    return null
  }

  const { locale, theme, roleName, route } = data

  if (
    typeof locale !== 'string' ||
    locale.length === 0 ||
    (theme !== 'dark' && theme !== 'light') ||
    typeof roleName !== 'string' ||
    typeof route !== 'string' ||
    !route.startsWith('/')
  ) {
    return null
  }

  return { locale, theme, roleName, route }
}
