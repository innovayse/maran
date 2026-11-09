import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { DnsRecord, DnsRecordRequest, DnsRecordsApi, SaveDnsRecordRequest } from '../../types/dnsRecord'
import type { ListRequest, Paged } from '../../types/list'

/** The list endpoint's parameter for each filter the records tab reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  type: 'type',
}

/**
 * Builds the records API on top of the shared low-level client.
 * @returns The {@link DnsRecordsApi} bound to the panel's DNS record endpoints.
 */
export const useDnsRecordsApi = (): DnsRecordsApi => {
  const api = useApi()

  /**
   * Lists one page of a zone's records.
   * @param zoneId The zone.
   * @param request The page, size, search, sort and the type filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (zoneId: string, request: ListRequest, signal?: AbortSignal): Promise<Paged<DnsRecord>> => {
    return api.get<Paged<DnsRecord>>(`/api/v1/dns-zones/${zoneId}/records${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Adds a record to a zone.
   * @param zoneId The zone.
   * @param request The record.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The record as created.
   */
  const create = (zoneId: string, request: DnsRecordRequest, signal?: AbortSignal): Promise<DnsRecord> => {
    const body: SaveDnsRecordRequest = { record: request }
    return api.post<DnsRecord>(`/api/v1/dns-zones/${zoneId}/records`, body, signal)
  }

  /**
   * Replaces a record's fields.
   * @param zoneId The zone.
   * @param recordId The record.
   * @param request The record as it should now be.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The record as changed.
   */
  const update = (
    zoneId: string,
    recordId: string,
    request: DnsRecordRequest,
    signal?: AbortSignal,
  ): Promise<DnsRecord> => {
    const body: SaveDnsRecordRequest = { record: request }
    return api.put<DnsRecord>(`/api/v1/dns-zones/${zoneId}/records/${recordId}`, body, signal)
  }

  /**
   * Deletes a record.
   * @param zoneId The zone.
   * @param recordId The record.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel deleted the record.
   */
  const remove = (zoneId: string, recordId: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`/api/v1/dns-zones/${zoneId}/records/${recordId}`, signal)
  }

  return { list, create, update, remove }
}
