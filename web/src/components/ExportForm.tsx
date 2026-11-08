import { useState, type FormEvent } from 'react'
import { api } from '../api'
import PasswordInput from './PasswordInput'
import Select from './Select'

const FORMATS = [{ value: 'json', label: 'JSON' }, { value: 'csv', label: 'CSV (zip)' }]

// download your ratings, reviews, list entries and custom lists; asks for the password like the other sensitive actions
export default function ExportForm() {
  const [password, setPassword] = useState('')
  const [format, setFormat] = useState<'json' | 'csv'>('json')
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
      URL.revokeObjectURL(url)
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
      <p className="muted hint">Your ratings, reviews, list entries and custom lists with their dates. CSV comes as a zip with one file per table. Up to 5 downloads an hour.</p>
      <PasswordInput label="Current password" value={password} onChange={setPassword} autoComplete="current-password" />
      <label className="row">
        <span>Format</span>
        <Select label="Format" value={format} options={FORMATS} onChange={(v) => setFormat(v as 'json' | 'csv')} />
      </label>
      {error && <p className="error" role="alert">{error}</p>}
      {msg && <p role="status">{msg}</p>}
      <button type="submit" disabled={busy || !password}>Download my data</button>
    </form>
  )
}
