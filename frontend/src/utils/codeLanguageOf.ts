import type { CodeLanguage } from '../types/codeLanguage'

/** Extension (lower case, no dot) to language. A name that is not here is shown as plain text. */
const BY_EXTENSION: Record<string, CodeLanguage> = {
  php: 'php',
  phtml: 'php',
  js: 'javascript',
  mjs: 'javascript',
  cjs: 'javascript',
  jsx: 'jsx',
  ts: 'typescript',
  mts: 'typescript',
  cts: 'typescript',
  tsx: 'tsx',
  css: 'css',
  less: 'css',
  scss: 'scss',
  sass: 'scss',
  html: 'html',
  htm: 'html',
  vue: 'vue',
  json: 'json',
  webmanifest: 'json',
  yaml: 'yaml',
  yml: 'yaml',
  xml: 'xml',
  svg: 'xml',
  xsd: 'xml',
  xsl: 'xml',
  sql: 'sql',
  sh: 'shell',
  bash: 'shell',
  zsh: 'shell',
  ini: 'ini',
  cnf: 'ini',
  conf: 'ini',
  cfg: 'ini',
  env: 'ini',
  properties: 'ini',
  md: 'markdown',
  markdown: 'markdown',
  py: 'python',
}

/** Whole file names with no useful extension. */
const BY_NAME: Record<string, CodeLanguage> = {
  '.htaccess': 'ini',
  '.htpasswd': 'ini',
  '.env': 'ini',
  '.bashrc': 'shell',
  '.profile': 'shell',
  '.bash_profile': 'shell',
}

/**
 * Chooses the highlighting for a file from its name alone. The contents are never sniffed: the name
 * is what the person sees and chose, and reading the text to guess would make the colour change as
 * they type.
 * @param name The file's base name (not its path).
 * @returns The language to highlight with; `plain` when the name says nothing.
 */
export const codeLanguageOf = (name: string): CodeLanguage => {
  const lower = name.toLowerCase()
  // A web server's own configuration is the one place `.conf` means a specific dialect.
  if (lower.includes('nginx') && lower.endsWith('.conf')) {
    return 'nginx'
  }
  const whole = BY_NAME[lower]
  if (whole !== undefined) {
    return whole
  }
  const dot = lower.lastIndexOf('.')
  if (dot <= 0) {
    return 'plain'
  }
  return BY_EXTENSION[lower.slice(dot + 1)] ?? 'plain'
}
