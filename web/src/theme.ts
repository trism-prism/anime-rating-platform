import { useSyncExternalStore } from 'react'

export type Theme = 'light' | 'dark' | 'system'
export const THEMES: { value: Theme; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
]

const KEY = 'theme'
const osLight = () => window.matchMedia('(prefers-color-scheme: light)')

function read(): Theme {
  try {
    const v = localStorage.getItem(KEY)
    if (v === 'light' || v === 'dark') return v
  } catch { /* storage blocked: system */ }
  return 'system'
}

// kept in memory too, so the choice holds for the visit when storage fails
let current: Theme = read()
const listeners = new Set<() => void>()

// sets data-theme (what the CSS reads) and the browser bar colour from --bg
function apply() {
  const resolved = current === 'system' ? (osLight().matches ? 'light' : 'dark') : current
  const root = document.documentElement
  root.setAttribute('data-theme', resolved)
  const bg = getComputedStyle(root).getPropertyValue('--bg').trim()
  if (bg) document.querySelector('meta[name=theme-color]')?.setAttribute('content', bg)
}

// module scope so React sees one stable function
function subscribe(cb: () => void) {
  listeners.add(cb)
  return () => { listeners.delete(cb) }
}

function set(next: Theme) {
  current = next
  apply()
  listeners.forEach((l) => l())
}

export function setTheme(next: Theme) {
  try {
    if (next === 'system') localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, next)
  } catch { /* storage blocked: the choice lasts until reload */ }
  set(next)
}

if (typeof window !== 'undefined') {
  apply()
  osLight().addEventListener('change', () => { if (current === 'system') apply() })
  window.addEventListener('storage', (e) => { if (e.key === KEY || e.key === null) set(read()) }) // another tab changed it
}

export function useTheme(): [Theme, (t: Theme) => void] {
  const theme = useSyncExternalStore(subscribe, () => current)
  return [theme, setTheme]
}

export const nextTheme = (t: Theme): Theme => THEMES[(THEMES.findIndex((x) => x.value === t) + 1) % THEMES.length].value
export const themeLabel = (t: Theme) => THEMES.find((x) => x.value === t)!.label
