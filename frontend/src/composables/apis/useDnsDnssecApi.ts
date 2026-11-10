import { useApi } from '../useApi'
import type { DnsDnssecApi, FinishDnssecWithdrawalRequest, ZoneDnssec } from '../../types/dnsDnssec'

/**
 * Builds the DNSSEC API on top of the shared low-level client.
 * @returns The {@link DnsDnssecApi} bound to the panel's zone signing endpoints.
 */
export const useDnsDnssecApi = (): DnsDnssecApi => {
  const api = useApi()

  /**
   * Reads a zone's signing.
   * @param zoneId The zone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing.
   */
  const get = (zoneId: string, signal?: AbortSignal): Promise<ZoneDnssec> => {
    return api.get<ZoneDnssec>(`/api/v1/dns-zones/${zoneId}/dnssec`, signal)
  }

  /**
   * Starts signing the zone.
   * @param zoneId The zone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing as it stands afterwards.
   */
  const enable = (zoneId: string, signal?: AbortSignal): Promise<ZoneDnssec> => {
    return api.post<ZoneDnssec>(`/api/v1/dns-zones/${zoneId}/dnssec/enable`, undefined, signal)
  }

  /**
   * Begins withdrawing the signing.
   * @param zoneId The zone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing as it stands afterwards.
   */
  const withdraw = (zoneId: string, signal?: AbortSignal): Promise<ZoneDnssec> => {
    return api.post<ZoneDnssec>(`/api/v1/dns-zones/${zoneId}/dnssec/withdraw`, undefined, signal)
  }

  /**
   * Ends the withdrawal and deletes the key.
   * @param zoneId The zone.
   * @param request The owner's statement that the DS record is gone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing as it stands afterwards.
   */
  const finishWithdrawal = (
    zoneId: string,
    request: FinishDnssecWithdrawalRequest,
    signal?: AbortSignal,
  ): Promise<ZoneDnssec> => {
    return api.post<ZoneDnssec>(`/api/v1/dns-zones/${zoneId}/dnssec/finish-withdrawal`, request, signal)
  }

  return { get, enable, withdraw, finishWithdrawal }
}
