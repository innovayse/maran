import type { InjectionKey, Ref } from 'vue'

/**
 * What a {@link UiForm} provides to the fields inside it: whether the record the form edits is
 * still loading. Each kit field reads it through `useFormLoading` and, while it is `true`, keeps its
 * real label and draws a placeholder of its control's footprint instead of the control. The form's
 * own template is therefore the only description of its fields, loading or loaded.
 */
export const FORM_LOADING_KEY: InjectionKey<Readonly<Ref<boolean>>> = Symbol('form-loading')
