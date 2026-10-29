import { useApi } from '../useApi'
import type { ManagedPlan, PlanRequest, PlansApi } from '../../types/plan'

/**
 * The endpoint plans are managed through. Distinct from `/api/v1/accounts/plans`, the read the
 * account form's picker uses: this one is administrator-only and carries the account count.
 */
const PLANS_PATH = '/api/v1/plans'

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
   * Lists every plan.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plans with their limits and account counts.
   */
  const list = (signal?: AbortSignal): Promise<ManagedPlan[]> => {
    return api.get<ManagedPlan[]>(PLANS_PATH, signal)
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

  return { list, create, update, remove }
}
