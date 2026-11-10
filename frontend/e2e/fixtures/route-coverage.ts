/**
 * Reads the router's source text and the 390 px sweep specs' source text and works out which
 * routed screens no sweep row opens. Pure string work, no browser: the census spec
 * (`e2e/ui-kit/route-coverage.spec.ts`) feeds it the real files, and its positive and inverse
 * controls feed it planted ones.
 *
 * Why it reads text rather than importing the router: the router module pulls in every page
 * component and the Vue runtime, none of which a Node-side spec can load, and the question is
 * about what the file DECLARES, which is exactly what its text says.
 */

/** One screen the router can show. */
export interface RouteEntry {
  /** The full path, parents joined, e.g. `/accounts/:id`. A catch-all keeps its `(.*)*` spelling. */
  path: string
  /** True when the route only redirects and so draws no screen of its own. */
  redirect: boolean
  /** True for the catch-all that draws the not-found screen. */
  catchAll: boolean
}

/** A route deliberately left out of the sweep, with the reason it may be. */
export interface RouteExemption {
  /** The full route path, as {@link RouteEntry.path} spells it. */
  path: string
  /** Why no sweep row opens it. */
  reason: string
}

/** One lexical token of the router source that the route reader cares about. */
interface Token {
  /** `str` is a quoted string, `id` an identifier, `p` one punctuation character. */
  kind: 'str' | 'id' | 'p'
  /** The string's content, the identifier, or the punctuation character. */
  value: string
}

/** The object or array being read while the tokens are walked. */
interface Frame {
  /** Whether the frame opened with `{` or `[`. */
  kind: 'obj' | 'arr'
  /** For an array: the key it was assigned to (`routes`, `children`, ...). */
  key: string
  /** For an object: its `path` value, once seen. */
  path: string | undefined
  /** For an object: true once it holds a `redirect` key. */
  redirect: boolean
  /** For an object: true once it holds a `children` array. */
  hasChildren: boolean
}

/**
 * Splits TypeScript source into the tokens the route reader needs, dropping comments and
 * whitespace and keeping quoted strings whole, so a `{` inside a string (a regular expression
 * quantifier in a route segment) is not mistaken for punctuation.
 * @param source The source text.
 * @returns The tokens in order.
 */
const tokenize = (source: string): Token[] => {
  const tokens: Token[] = []
  let index = 0
  while (index < source.length) {
    const char = source.charAt(index)
    const next = source.charAt(index + 1)
    if (char === '/' && next === '/') {
      while (index < source.length && source.charAt(index) !== '\n') {
        index += 1
      }
    } else if (char === '/' && next === '*') {
      const end = source.indexOf('*/', index + 2)
      index = end === -1 ? source.length : end + 2
    } else if (char === "'" || char === '"' || char === '`') {
      let value = ''
      index += 1
      while (index < source.length && source.charAt(index) !== char) {
        if (source.charAt(index) === '\\') {
          index += 1
        }
        value += source.charAt(index)
        index += 1
      }
      index += 1
      tokens.push({ kind: 'str', value })
    } else if (/[A-Za-z_$]/.test(char)) {
      let value = ''
      while (index < source.length && /[A-Za-z0-9_$]/.test(source.charAt(index))) {
        value += source.charAt(index)
        index += 1
      }
      tokens.push({ kind: 'id', value })
    } else {
      if (/[{}[\]:]/.test(char)) {
        tokens.push({ kind: 'p', value: char })
      }
      index += 1
    }
  }
  return tokens
}

/**
 * Finds the string constants the router names its segments with (`const SITE_ID_SEGMENT = '...'`).
 * @param tokens The router's tokens.
 * @returns Constant name to its string value.
 */
const stringConstants = (tokens: Token[]): Map<string, string> => {
  const constants = new Map<string, string>()
  tokens.forEach((token, position) => {
    const following = tokens[position + 1]
    // `=` is not tokenized, so `const NAME = 'value'` reads as the three tokens `const`, `NAME`, `value`.
    if (token.value === 'const' && following?.kind === 'id' && tokens[position + 2]?.kind === 'str') {
      constants.set(following.value, tokens[position + 2]?.value ?? '')
    }
  })
  return constants
}

/**
 * Joins a child route's path onto its parent's the way vue-router does.
 * @param parent The parent's full path; empty for a top-level route.
 * @param child The child's own path.
 * @returns The child's full path.
 */
const joinPath = (parent: string, child: string): string => {
  if (child.startsWith('/')) {
    return child
  }
  if (child === '') {
    return parent
  }
  return `${parent.replace(/\/$/, '')}/${child}`
}

/**
 * Reads every leaf route the router declares: a route that has a `path` and no `children`.
 * Parent routes (the layout wrappers) are not screens, but their paths prefix their children's.
 * @param routerSource The text of `src/router/index.ts`.
 * @returns The routes, in declaration order.
 */
export const routesOf = (routerSource: string): RouteEntry[] => {
  const tokens = tokenize(routerSource)
  const constants = stringConstants(tokens)
  const stack: Frame[] = []
  const routes: RouteEntry[] = []

  tokens.forEach((token, position) => {
    const previous = tokens[position - 1]
    const key = tokens[position - 2]
    const top = stack[stack.length - 1]

    if (token.kind === 'p' && (token.value === '{' || token.value === '[')) {
      const keyName = previous?.value === ':' && key?.kind === 'id' ? key.value : ''
      if (token.value === '[' && keyName === 'children' && top !== undefined) {
        top.hasChildren = true
      }
      stack.push({
        kind: token.value === '{' ? 'obj' : 'arr',
        key: keyName,
        path: undefined,
        redirect: false,
        hasChildren: false,
      })
      return
    }

    if (token.kind === 'p' && (token.value === '}' || token.value === ']')) {
      const closed = stack.pop()
      const owner = stack[stack.length - 1]
      const isRoute =
        closed?.kind === 'obj' &&
        closed.path !== undefined &&
        owner?.kind === 'arr' &&
        (owner.key === 'routes' || owner.key === 'children')
      if (closed !== undefined && isRoute && !closed.hasChildren) {
        // A route's full path is every enclosing route's path joined, outermost first.
        const chain = stack.filter((frame) => {
          return frame.kind === 'obj' && frame.path !== undefined
        })
        let full = ''
        for (const frame of chain) {
          full = joinPath(full, frame.path ?? '')
        }
        full = joinPath(full, closed.path ?? '')
        routes.push({ path: full, redirect: closed.redirect, catchAll: full.includes('(.*)*') })
      }
      return
    }

    if (token.kind === 'id' && tokens[position + 1]?.value === ':' && top?.kind === 'obj') {
      if (token.value === 'redirect') {
        top.redirect = true
      }
      if (token.value === 'path') {
        const value = tokens[position + 2]
        if (value?.kind === 'str') {
          top.path = value.value
        } else if (value?.kind === 'id') {
          top.path = constants.get(value.value) ?? value.value
        }
      }
    }
  })

  return routes
}

/**
 * Turns a route path into a pattern a concrete path can be tested against: a parameter segment
 * (`:id`, `:id(regex)`) matches any one segment, the catch-all matches anything.
 * @param route The route.
 * @returns The pattern.
 */
const patternOf = (route: RouteEntry): RegExp => {
  if (route.catchAll) {
    return /^\/.*$/
  }
  const segments = route.path
    .split('/')
    .slice(1)
    .map((segment) => {
      return segment.startsWith(':') ? '[^/]+' : segment.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    })
  return new RegExp(`^/${segments.join('/')}$`)
}

/**
 * How many parameter segments a route has; the fewer, the more specific, as in vue-router's own
 * ranking where a literal segment beats a parameter at the same position.
 * @param route The route.
 * @returns The count.
 */
const parameterCount = (route: RouteEntry): number => {
  return route.path.split('/').filter((segment) => {
    return segment.startsWith(':')
  }).length
}

/**
 * Collects every address the sweep specs open: each `path: '...'` or ``path: `...` `` literal that
 * starts with a slash, with a template placeholder replaced by `x` and the query dropped.
 * @param specSources The text of the sweep specs.
 * @returns The concrete paths, duplicates kept out.
 */
export const sweptPathsOf = (specSources: string[]): string[] => {
  const found = new Set<string>()
  for (const source of specSources) {
    // The opening quote must be followed by the slash of an address. Without that, prose that names
    // the key in backticks (a comment reading "the `path:` rows below") opens a template string at
    // its own backtick, which runs on to the next backtick in the file and swallows the real rows
    // between them: the spec then yields no address at all while still containing its rows.
    for (const match of source.matchAll(/\bpath:\s*(['"`])(\/(?:\\.|(?!\1)[^\\])*)\1/g)) {
      const raw = (match[2] ?? '').replace(/\$\{[^}]*\}/g, 'x').split('?')[0] ?? ''
      found.add(raw)
    }
  }
  return [...found]
}

/**
 * Names the routes no sweep row opens and no exemption excuses. A swept address counts for the
 * ONE route vue-router would resolve it to (a literal segment beats a parameter), so a row for
 * `/accounts/new` does not also cover `/accounts/:id`.
 * @param routes The router's routes.
 * @param swept The addresses the sweeps open.
 * @param exemptions The routes deliberately left out.
 * @returns The uncovered routes' paths; empty when every screen is swept or excused.
 */
export const uncoveredRoutes = (routes: RouteEntry[], swept: string[], exemptions: RouteExemption[]): string[] => {
  const screens = routes.filter((route) => {
    return !route.redirect && !route.catchAll
  })
  const claimed = new Set<string>()
  for (const address of swept) {
    const candidates = screens
      .filter((route) => {
        return patternOf(route).test(address)
      })
      .sort((left, right) => {
        return parameterCount(left) - parameterCount(right)
      })
    const winner = candidates[0]
    if (winner !== undefined) {
      claimed.add(winner.path)
    }
  }
  const excused = new Set(
    exemptions.map((exemption) => {
      return exemption.path
    }),
  )
  return routes
    .filter((route) => {
      return !route.redirect && !claimed.has(route.path) && !excused.has(route.path)
    })
    .map((route) => {
      return route.path
    })
}
