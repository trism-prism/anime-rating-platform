import { useState, type FormEvent } from 'react'
import { api } from '../api'
import PasswordInput from './PasswordInput'

const FORMATS = [
  { value: 'csv', label: 'Spreadsheets' },
  { value: 'json', label: 'Full backup' },
] as const

// download your profile (username, month joined, bio), ratings, reviews, list entries and custom lists; asks for the password like the other sensitive actions
export default function ExportForm() {
  const [password, setPassword] = useState('')
  const [format, setFormat] = useState<'json' | 'csv'>('csv')
  const [error, setError] = useState('')
  const [msg, setMsg] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setMsg('')
    setBusy(true)
    try {
      const blob = await api<Blob>('/me/export', { method: 'POST', body: JSON.stringify({ currentPassword: password, format }) }, true)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = format === 'json' ? 'anime-platform-export.json' : 'anime-platform-export.zip'
      a.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000) // some browsers read the blob after click()
      setPassword('')
      setMsg('Download started')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="settings-form" onSubmit={submit}>
      <PasswordInput label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
      <fieldset className="choice">
        <legend>File type</legend>
        {FORMATS.map((f) => (
          <label key={f.value} className="choice-opt">
            <input type="radio" name="export-format" value={f.value} checked={format === f.value} onChange={() => setFormat(f.value)} />
            {f.label}
          </label>
        ))}
        {format === 'json' && <p className="muted hint">Everything in one file you can keep.</p>}
      </fieldset>
      <p className="muted hint">Up to 5 downloads an hour.</p>
      {error && <p className="error" role="alert">{error}</p>}
      {msg && <p role="status">{msg}</p>}
      <div><button type="submit" className="btn-primary" disabled={busy || !password}>{busy ? 'Preparing' : 'Download my data'}</button></div>
    </form>
  )
}
