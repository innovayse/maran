import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  DnsTemplate,
  DnsTemplateChoice,
  DnsTemplateDetail,
  DnsTemplatesApi,
  DnsTemplateVariable,
  SaveDnsTemplateRequest,
} from '../../types/dnsTemplate'

/** The endpoint templates are listed, read and changed through. */
const TEMPLATES_PATH = '/api/v1/dns-zone-templates'

/** The endpoint the create-zone form reads its template choices from. */
const CHOICES_PATH = '/api/v1/dns-zones/templates/choices'

/** The templates list takes no filters: only the search and the sort are sent. */
const NO_FILTER_PARAMS: Readonly<Record<string, string>> = {}

/**
 * Builds the templates API on top of the shared low-level client.
 * @returns The {@link DnsTemplatesApi} bound to the panel's template endpoints.
 */
export const useDnsTemplatesApi = (): DnsTemplatesApi => {
  const api = useApi()

  /**
   * Lists one page of the templates.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<DnsTemplate>> => {
    return api.get<Paged<DnsTemplate>>(`${TEMPLATES_PATH}${listQuery(request, NO_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one template.
   * @param id The template.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The template with its records.
   */
  const get = (id: string, signal?: AbortSignal): Promise<DnsTemplateDetail> => {
    return api.get<DnsTemplateDetail>(`${TEMPLATES_PATH}/${id}`, signal)
  }

  /**
   * Creates a template.
   * @param request The name, description and records.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The template as created.
   */
  const create = (request: SaveDnsTemplateRequest, signal?: AbortSignal): Promise<DnsTemplateDetail> => {
    return api.post<DnsTemplateDetail>(TEMPLATES_PATH, request, signal)
  }

  /**
   * Replaces a template whole.
   * @param id The template.
   * @param request The template as it should now be.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The template as saved.
   */
  const update = (id: string, request: SaveDnsTemplateRequest, signal?: AbortSignal): Promise<DnsTemplateDetail> => {
    return api.put<DnsTemplateDetail>(`${TEMPLATES_PATH}/${id}`, request, signal)
  }

  /**
   * Deletes a template.
   * @param id The template.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel deleted it.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${TEMPLATES_PATH}/${id}`, signal)
  }

  /**
   * Makes a template the default of zones created from a site.
   * @param id The template.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel changed the default.
   */
  const makeDefault = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.post<void>(`${TEMPLATES_PATH}/${id}/make-default`, undefined, signal)
  }

  /**
   * Reads the variable catalogue.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The tokens with their descriptions.
   */
  const variables = (signal?: AbortSignal): Promise<DnsTemplateVariable[]> => {
    return api.get<DnsTemplateVariable[]>(`${TEMPLATES_PATH}/variables`, signal)
  }

  /**
   * Lists the templates a new zone can start from.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The choices.
   */
  const choices = (signal?: AbortSignal): Promise<DnsTemplateChoice[]> => {
    return api.get<DnsTemplateChoice[]>(CHOICES_PATH, signal)
  }

  return { list, get, create, update, remove, makeDefault, variables, choices }
}
