import type { InjectionKey, Ref } from 'vue'

/**
 * What a {@link UiDescriptionList} provides to its items: whether the record it describes is still
 * loading. Each `UiDescriptionItem` keeps its real term and draws a value line while it is `true`,
 * so the list's own template is the only description of its pairs, loading or loaded.
 */
export const DESCRIPTION_LIST_LOADING_KEY: InjectionKey<Readonly<Ref<boolean>>> = Symbol('description-list-loading')
