import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type { ManagedPlan, PlanRequest, PlansApi } from '../../types/plan'

/**
 * The endpoint plans are managed through: administrator-only, and the rows carry the account count.
 * The plan pickers read `/api/v1/plans/choices` instead, through the accounts API.
 */
const PLANS_PATH = '/api/v1/plans'

/** The plans list takes no typed filter; its only parameters are the page, search and sort. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {}

/**
 * Builds the plans API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 * @returns The {@link PlansApi} bound to the panel's plan endpoints.
 */
export const usePlansApi = (): PlansApi => {
  const api = useApi()

  /**
   * Lists one page of plans.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, each plan with its limits and account count.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<ManagedPlan>> => {
    return api.get<Paged<ManagedPlan>>(`${PLANS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one plan.
   * @param id The plan's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plan with its limits and account count.
   */
  const get = (id: string, signal?: AbortSignal): Promise<ManagedPlan> => {
    return api.get<ManagedPlan>(`${PLANS_PATH}/${id}`, signal)
  }

  /**
   * Defines a plan.
   * @param request The plan's name and limits.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The created plan.
   */
  const create = (request: PlanRequest, signal?: AbortSignal): Promise<ManagedPlan> => {
    return api.post<ManagedPlan>(PLANS_PATH, request, signal)
  }

  /**
   * Replaces a plan's name and limits.
   * @param id The plan to edit.
   * @param request The plan's new name and limits.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plan as it now stands.
   */
  const update = (id: string, request: PlanRequest, signal?: AbortSignal): Promise<ManagedPlan> => {
    return api.put<ManagedPlan>(`${PLANS_PATH}/${id}`, request, signal)
  }

  /**
   * Deletes a plan.
   * @param id The plan to delete.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once the plan was deleted.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${PLANS_PATH}/${id}`, signal)
  }

  return { list, get, create, update, remove }
}
