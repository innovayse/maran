import { inject, ref, type Ref } from 'vue'
import { FORM_LOADING_KEY } from '../utils/formLoadingKey'

/**
 * Whether the {@link UiForm} around a kit field is still loading the record it edits. A field
 * outside any form, or inside a form that is not loading, reads `false` and renders its control.
 * @returns A read-only flag, `true` while the enclosing form is loading.
 */
export const useFormLoading = (): Readonly<Ref<boolean>> => {
  return inject(FORM_LOADING_KEY, ref(false))
}
