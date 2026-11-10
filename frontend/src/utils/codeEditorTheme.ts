import { syntaxHighlighting } from '@codemirror/language'
import type { Extension } from '@codemirror/state'
import { EditorView } from '@codemirror/view'
import { tagHighlighter, tags } from '@lezer/highlight'

/**
 * Names each kind of token with a fixed class (`tok-keyword`, `tok-string`, ...) instead of the
 * generated one, so the colour of a token lives in the theme below and nowhere else, and a test can
 * ask whether a file was highlighted at all.
 */
const tokenClasses = tagHighlighter([
  { tag: [tags.keyword, tags.operatorKeyword, tags.controlKeyword, tags.moduleKeyword, tags.definitionKeyword], class: 'tok-keyword' },
  { tag: [tags.string, tags.special(tags.string), tags.character, tags.docString], class: 'tok-string' },
  { tag: [tags.number, tags.bool, tags.atom, tags.null, tags.regexp, tags.escape], class: 'tok-number' },
  { tag: [tags.comment, tags.lineComment, tags.blockComment], class: 'tok-comment' },
  { tag: [tags.tagName, tags.standard(tags.tagName), tags.angleBracket], class: 'tok-tag' },
  { tag: [tags.attributeName, tags.propertyName, tags.definition(tags.propertyName)], class: 'tok-property' },
  { tag: [tags.typeName, tags.className, tags.namespace, tags.definition(tags.typeName)], class: 'tok-type' },
  { tag: [tags.function(tags.variableName), tags.definition(tags.variableName), tags.labelName], class: 'tok-name' },
  { tag: [tags.meta, tags.processingInstruction, tags.documentMeta, tags.annotation], class: 'tok-meta' },
  { tag: [tags.heading, tags.strong], class: 'tok-strong' },
  { tag: tags.emphasis, class: 'tok-emphasis' },
  { tag: [tags.link, tags.url], class: 'tok-link' },
  { tag: tags.invalid, class: 'tok-invalid' },
])

/**
 * The editor's look, taken from the panel's design tokens only (rules/vue.md "Styling"): the same
 * tokens switch with the light and dark theme, so one theme object serves both. No hex value is
 * written here.
 *
 * Contrast, measured with the WCAG relative-luminance formula against the field fill `--s2` with
 * the active-line wash (3 % of `--t1`) over it, dark / light:
 * primary text 12.7 / 14.5 or better; `--act` keywords 5.34 / 5.79; `--gr` strings 6.28 / 4.62;
 * `--am` numbers 7.27 / 4.84; `--t3` comments and line numbers 4.72 / 4.61; `--t2` meta and
 * punctuation 6.24 / 5.11. Without the wash every figure is higher (`--gr` light 4.92). An invalid
 * token keeps primary text and a wavy `--rd` underline, because `--rd` as text on the wash is 4.22
 * in dark. The selection is 18 % of `--ac` over the fill; text on it is 3.6 or better, which is
 * the operating system's own selection contrast class. The editor's edge is `--bc` and the focus
 * edge `--ac`, as for every field (the wrapper draws them).
 */
const theme: Extension = EditorView.theme({
  '&': {
    color: 'var(--t1)',
    backgroundColor: 'var(--s2)',
    height: '100%',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': {
    fontFamily: 'var(--font-mono)',
    lineHeight: '1.6',
    overflow: 'auto',
  },
  '.cm-content': { caretColor: 'var(--t1)', padding: '0.5rem 0' },
  '.cm-line': { padding: '0 0.75rem' },
  // A text that cannot be edited yet reads as muted, like a disabled field (UiTextarea).
  '.cm-content[aria-disabled=true]': { color: 'var(--t3)' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--t1)' },
  '.cm-activeLine': { backgroundColor: 'color-mix(in srgb, var(--t1) 3%, transparent)' },
  '.cm-gutters': {
    backgroundColor: 'var(--s2)',
    color: 'var(--t3)',
    border: 'none',
    borderRight: '1px solid var(--b1)',
  },
  '.cm-activeLineGutter': {
    backgroundColor: 'color-mix(in srgb, var(--t1) 3%, transparent)',
    color: 'var(--t1)',
  },
  '.cm-selectionBackground, &.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, ::selection': {
    backgroundColor: 'color-mix(in srgb, var(--ac) 18%, transparent)',
  },
  '&.cm-focused .cm-matchingBracket': {
    backgroundColor: 'var(--acs)',
    outline: '1px solid var(--ac)',
  },
  '&.cm-focused .cm-nonmatchingBracket': {
    backgroundColor: 'transparent',
    outline: '1px solid var(--rd)',
  },
  '.cm-searchMatch': {
    backgroundColor: 'color-mix(in srgb, var(--am) 28%, transparent)',
    outline: '1px solid var(--am)',
  },
  '.cm-searchMatch.cm-searchMatch-selected': {
    backgroundColor: 'color-mix(in srgb, var(--ac) 28%, transparent)',
    outline: '2px solid var(--ac)',
  },
  '.cm-panels': {
    backgroundColor: 'var(--s1)',
    color: 'var(--t1)',
  },
  '.cm-panels-top': { borderBottom: '1px solid var(--b1)' },
  '.cm-panel.cm-search, .cm-panel.cm-gotoLine': {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: '0.5rem',
    padding: '0.5rem 2rem 0.5rem 0.75rem',
    fontSize: 'var(--text-sm)',
  },
  '.cm-panel.cm-search br': { display: 'none' },
  '.cm-panel.cm-search label, .cm-panel.cm-gotoLine label': {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '0.25rem',
  },
  '.cm-textfield': {
    backgroundColor: 'var(--s2)',
    color: 'var(--t1)',
    border: '1px solid var(--bc)',
    borderRadius: 'var(--r-inset)',
    padding: '0.25rem 0.5rem',
    minHeight: '2rem',
    fontSize: 'var(--text-base)',
    margin: '0',
  },
  '.cm-textfield:hover': { borderColor: 'var(--bch)' },
  '.cm-textfield:focus-visible': { outline: 'none', borderColor: 'var(--ac)', boxShadow: 'var(--shadow-field-focus)' },
  '.cm-button': {
    backgroundImage: 'none',
    backgroundColor: 'var(--s3)',
    color: 'var(--t1)',
    border: '1px solid var(--bc)',
    borderRadius: 'var(--r-inset)',
    padding: '0.25rem 0.75rem',
    minHeight: '2rem',
    minWidth: '2rem',
    fontSize: 'var(--text-sm)',
    cursor: 'pointer',
    margin: '0',
  },
  '.cm-button:hover': { borderColor: 'var(--bch)' },
  '.cm-button:focus-visible': { outline: 'none', boxShadow: 'var(--shadow-focus)' },
  '.cm-panel.cm-search [name=close]': {
    position: 'absolute',
    top: '0.25rem',
    right: '0.25rem',
    backgroundColor: 'transparent',
    color: 'var(--t2)',
    border: 'none',
    fontSize: 'var(--text-lg)',
  },
  '.tok-keyword': { color: 'var(--act)' },
  '.tok-tag': { color: 'var(--act)' },
  '.tok-string': { color: 'var(--gr)' },
  '.tok-number': { color: 'var(--am)' },
  '.tok-comment': { color: 'var(--t3)', fontStyle: 'italic' },
  '.tok-meta': { color: 'var(--t2)' },
  '.tok-property': { color: 'var(--t1)', fontWeight: '600' },
  '.tok-type': { color: 'var(--t1)', textDecoration: 'underline', textDecorationColor: 'var(--bc)' },
  '.tok-name': { color: 'var(--t1)', fontWeight: '600' },
  '.tok-strong': { fontWeight: '700' },
  '.tok-emphasis': { fontStyle: 'italic' },
  '.tok-link': { color: 'var(--act)', textDecoration: 'underline' },
  '.tok-invalid': { textDecoration: 'underline wavy var(--rd)' },
})

/** The theme plus the token colouring, as one extension. */
export const codeEditorTheme: Extension = [theme, syntaxHighlighting(tokenClasses)]
