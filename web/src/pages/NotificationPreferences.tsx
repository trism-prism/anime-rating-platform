import { useCallback, useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import type { NotificationPref } from '../types'

const LABELS: Record<string, string> = {
  FriendRequest: 'Someone sends me a friend request',
  FriendAccepted: 'Someone accepts my friend request',
  Reply: 'Someone replies to my review',
  Reaction: 'Someone reacts to my review',
}

export default function NotificationPreferences() {
  const { user } = useAuth()
  const [prefs, setPrefs] = useState<NotificationPref[] | null>(null)
  const [error, setError] = useState('')
  const [saveError, setSaveError] = useState('')
  const [saving, setSaving] = useState<string[]>([])

  const load = useCallback(() => {
    api<NotificationPref[]>('/me/notification-preferences')
      .then((p) => { setPrefs(p); setError('') })
      .catch((e: Error) => { setPrefs(null); setError(e.message) }) // no list rather than a guessed checkbox
  }, [])

  useEffect(() => {
    if (user) load()
  }, [user, load])

  if (!user) return <Navigate to="/login" replace />

  // shown at once; the box stays disabled until the server answers, so two quick clicks can't finish out of order,
  // and on failure it goes back to what the server has
  async function toggle(p: NotificationPref) {
    if (saving.includes(p.type)) return
    setSaveError('')
    setSaving((l) => [...l, p.type])
    const set = (enabled: boolean) => setPrefs((l) => l && l.map((x) => (x.type === p.type ? { ...x, enabled } : x)))
    set(!p.enabled)
    try {
      await api(`/me/notification-preferences/${p.type}`, { method: 'PUT', body: JSON.stringify({ enabled: !p.enabled }) })
    } catch (e) {
      setSaveError((e as Error).message)
      load() // the server's value, not a guess
    } finally {
      setSaving((l) => l.filter((t) => t !== p.type))
    }
  }

  return (
    <div className="settings">
      <h1>Notification preferences</h1>
      <p className="muted">Notify me when:</p>
      {error && <p className="error" role="alert">{error} <button onClick={() => { setError(''); load() }}>Retry</button></p>}
      {!prefs && !error && <p className="muted">Loading...</p>}
      {prefs && (
        <ul className="prefs">
          {prefs.map((p) => (
            <li key={p.type}>
              <label>
                <input type="checkbox" checked={p.enabled} disabled={saving.includes(p.type)} onChange={() => toggle(p)} /> {LABELS[p.type] ?? p.type}
              </label>
            </li>
          ))}
        </ul>
      )}
      {saveError && <p className="error" role="alert">{saveError}</p>}
    </div>
  )
}
