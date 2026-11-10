import { inject, ref, type Ref } from 'vue'
import { SESSION_READ_ONLY_KEY } from '../utils/sessionReadOnlyKey'

/**
 * Whether the session the kit component is drawn in is read-only: an operator viewing as another
 * login (issue #515). A component outside the signed-in shell, or inside one that is not viewing,
 * reads `false` and draws its controls as usual.
 *
 * Advice only: the panel refuses every write of a view-as session (rules/security.md item 6), so a
 * control this leaves enabled costs a refused request, never a change.
 * @returns A read-only flag, `true` while the session is read-only.
 */
export const useSessionReadOnly = (): Readonly<Ref<boolean>> => {
  return inject(SESSION_READ_ONLY_KEY, ref(false))
}
