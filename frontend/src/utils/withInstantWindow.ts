import { localDayBoundary } from './localDayBoundary'
import type { ListRequest } from '../types/list'

/**
 * A list request as an endpoint with a date window takes it: the window is picked as calendar days,
 * and the panel filters instants, so `from` becomes the first instant of its day and `to` the last
 * of its own, in the reader's time zone. Every other field travels as the URL holds it.
 * @param request The page the URL describes.
 * @returns The same request with the window's days turned into instants.
 */
export const withInstantWindow = (request: ListRequest): ListRequest => {
  const filters = { ...request.filters }
  if ('from' in filters) {
    filters.from = localDayBoundary(filters.from, 'start')
  }
  if ('to' in filters) {
    filters.to = localDayBoundary(filters.to, 'end')
  }
  return { ...request, filters }
}
