import { useApi } from '../useApi'
import type { DnsServerStatus, DnsSettings, DnsSettingsApi, SaveDnsSettingsRequest } from '../../types/dnsSettings'

/** The settings endpoint. */
const SETTINGS_PATH = '/api/v1/dns-settings'

/** The name server's endpoint. */
const SERVER_PATH = '/api/v1/dns-server'

/**
 * Builds the DNS settings API on top of the shared low-level client.
 * @returns The {@link DnsSettingsApi} bound to the panel's DNS settings and name server endpoints.
 */
export const useDnsSettingsApi = (): DnsSettingsApi => {
  const api = useApi()

  /**
   * Reads the settings.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings.
   */
  const get = (signal?: AbortSignal): Promise<DnsSettings> => {
    return api.get<DnsSettings>(SETTINGS_PATH, signal)
  }

  /**
   * Replaces the settings.
   * @param request The complete settings.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings as saved.
   */
  const save = (request: SaveDnsSettingsRequest, signal?: AbortSignal): Promise<DnsSettings> => {
    return api.put<DnsSettings>(SETTINGS_PATH, request, signal)
  }

  /**
   * Reads the name server's state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The state.
   */
  const server = (signal?: AbortSignal): Promise<DnsServerStatus> => {
    return api.get<DnsServerStatus>(SERVER_PATH, signal)
  }

  /**
   * Turns the name server on.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The state after turning it on.
   */
  const enableServer = (signal?: AbortSignal): Promise<DnsServerStatus> => {
    return api.post<DnsServerStatus>(`${SERVER_PATH}/enable`, undefined, signal)
  }

  /**
   * Turns the name server off.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The state after turning it off.
   */
  const disableServer = (signal?: AbortSignal): Promise<DnsServerStatus> => {
    return api.post<DnsServerStatus>(`${SERVER_PATH}/disable`, undefined, signal)
  }

  return { get, save, server, enableServer, disableServer }
}
