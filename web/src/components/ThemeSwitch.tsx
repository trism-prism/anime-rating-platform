import { useRef, type KeyboardEvent } from 'react'
import { THEMES, nextTheme, themeLabel, useTheme } from '../theme'

// Light / Dark / System as one radio group; arrow keys move the choice, Tab enters and leaves the group
export function ThemeSwitch() {
  const [theme, setTheme] = useTheme()
  const group = useRef<HTMLDivElement>(null)

  function onKeyDown(e: KeyboardEvent) {
    const step = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0
    if (!step) return
    e.preventDefault()
    const i = THEMES.findIndex((t) => t.value === theme)
    const next = THEMES[(i + step + THEMES.length) % THEMES.length].value
    setTheme(next)
    group.current?.querySelector<HTMLElement>(`[data-value="${next}"]`)?.focus()
  }

  return (
    <div className="segmented" role="radiogroup" aria-label="Theme" ref={group} onKeyDown={onKeyDown}>
      {THEMES.map((t) => (
        <button key={t.value} type="button" role="radio" aria-checked={theme === t.value} tabIndex={theme === t.value ? 0 : -1} data-value={t.value} onClick={() => setTheme(t.value)}>
          {t.label}
        </button>
      ))}
    </div>
  )
}

// one button that steps through the choices, for the account menu and the footer
export function ThemeCycle({ menu }: { menu?: boolean }) {
  const [theme, setTheme] = useTheme()
  return (
    <button type="button" className={menu ? undefined : 'link-button'} role={menu ? 'menuitem' : undefined} onClick={() => setTheme(nextTheme(theme))}>
      Theme: {themeLabel(theme)}
    </button>
  )
}
