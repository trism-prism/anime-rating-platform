import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { api } from '../api'
import { useAuth } from '../auth'
import { refreshAvatars } from '../avatarVersion'
import type { Profile } from '../types'
import Avatar from './Avatar'

const MAX_BIO = 300
const TYPES = ['image/png', 'image/jpeg', 'image/webp']
const MAX_FILE = 2 * 1024 * 1024
const SIZE = 256

// center-crop to a square and scale to 256x256, so what is stored is small whatever was chosen
async function square(file: File): Promise<Blob> {
  const img = await createImageBitmap(file)
  const side = Math.min(img.width, img.height)
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = SIZE
  canvas.getContext('2d')!.drawImage(img, (img.width - side) / 2, (img.height - side) / 2, side, side, 0, 0, SIZE, SIZE)
  img.close()
  // webp, or png where the browser can't write webp; the API looks at the bytes either way
  return new Promise((res, rej) => canvas.toBlob((b) => (b ? res(b) : rej(new Error('Could not read that image.'))), 'image/webp', 0.9))
}

// your bio and profile picture (both optional, both public on your profile)
export default function ProfileForm() {
  const { user } = useAuth()
  const [bio, setBio] = useState('')
  const [hasAvatar, setHasAvatar] = useState(false)
  const [loaded, setLoaded] = useState(false)
  const [bioMsg, setBioMsg] = useState('')
  const [picMsg, setPicMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const file = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!user) return
    api<Profile>(`/users/${encodeURIComponent(user.userName)}`)
      .then((p) => { setBio(p.bio ?? ''); setHasAvatar(p.hasAvatar); setLoaded(true) })
      .catch((e: Error) => setError(e.message))
  }, [user])

  async function run(fn: () => Promise<void>) {
    setBioMsg('')
    setPicMsg('')
    setError('')
    setBusy(true)
    try {
      await fn()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
      if (file.current) file.current.value = ''
    }
  }

  const saveBio = (e: FormEvent) => {
    e.preventDefault()
    return run(async () => {
      const r = await api<{ bio: string | null }>('/me/bio', { method: 'PUT', body: JSON.stringify({ bio }) })
      setBio(r.bio ?? '') // the censored text
      setBioMsg('Bio saved')
    })
  }

  const pick = (e: ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0]
    if (!f) return
    return run(async () => {
      if (!TYPES.includes(f.type)) throw new Error('Choose a png, jpeg or webp image.')
      if (f.size > MAX_FILE) throw new Error('The picture must be 2 MB or smaller.')
      const blob = await square(f)
      await api('/me/avatar', { method: 'PUT', body: blob, headers: { 'Content-Type': blob.type } })
      setHasAvatar(true)
      refreshAvatars()
      setPicMsg('Picture saved')
    })
  }

  const remove = () => run(async () => {
    await api('/me/avatar', { method: 'DELETE' })
    setHasAvatar(false)
    refreshAvatars()
    setPicMsg('Picture removed')
  })

  if (!user) return null
  return (
    <>
      <div className="settings-form">
        <div className="profile-head">
          <span className="profile-avatar"><Avatar userName={user.userName} /></span>
          <div>
            <label>Picture
              <input ref={file} type="file" accept={TYPES.join(',')} disabled={busy} onChange={pick} />
            </label>
            {hasAvatar && <button type="button" disabled={busy} onClick={remove}>Remove picture</button>}
          </div>
        </div>
        <p className="muted hint">Optional. A png, jpeg or webp image up to 2 MB, cropped to a square. Your picture is public: anyone can load it, including people you have blocked.</p>
        {picMsg && <p role="status">{picMsg}</p>}
      </div>

      <form className="settings-form" onSubmit={saveBio}>
        <label>Bio
          <textarea rows={4} maxLength={MAX_BIO} value={bio} disabled={!loaded} onChange={(e) => setBio(e.target.value)} />
        </label>
        <p className="muted hint">Optional, plain text, shown on your profile. {bio.length} / {MAX_BIO}</p>
        {error && <p className="error" role="alert">{error}</p>}
        {bioMsg && <p role="status">{bioMsg}</p>}
        <button type="submit" disabled={busy || !loaded}>Save bio</button>
      </form>
    </>
  )
}
