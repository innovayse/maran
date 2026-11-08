import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  CreateDnsZoneRequest,
  DnsRecordCatalog,
  DnsZone,
  DnsZonesApi,
} from '../../types/dns'

/** The endpoint zones are listed and created through. */
const ZONES_PATH = '/api/v1/dns-zones'

/** The list endpoint's parameter for each filter the zones page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
}

/**
 * Builds the zones API on top of the shared low-level client.
 * @returns The {@link DnsZonesApi} bound to the panel's DNS zone endpoints.
 */
export const useDnsZonesApi = (): DnsZonesApi => {
  const api = useApi()

  /**
   * Reads the record types and the bounds the server enforces.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The catalogue.
   */
  const catalog = (signal?: AbortSignal): Promise<DnsRecordCatalog> => {
    return api.get<DnsRecordCatalog>(`${ZONES_PATH}/record-types`, signal)
  }

  /**
   * Lists one page of the zones the caller may see.
   * @param request The page, size, search, sort and the account filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<DnsZone>> => {
    return api.get<Paged<DnsZone>>(`${ZONES_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one zone.
   * @param id The zone to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The zone.
   */
  const get = (id: string, signal?: AbortSignal): Promise<DnsZone> => {
    return api.get<DnsZone>(`${ZONES_PATH}/${id}`, signal)
  }

  /**
   * Creates a zone.
   * @param request The owning account and the apex name.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The zone as created.
   */
  const create = (request: CreateDnsZoneRequest, signal?: AbortSignal): Promise<DnsZone> => {
    return api.post<DnsZone>(ZONES_PATH, request, signal)
  }

  /**
   * Deletes a zone and its records.
   * @param id The zone to delete.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel deleted the zone.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${ZONES_PATH}/${id}`, signal)
  }

  return { catalog, list, get, create, remove }
}
