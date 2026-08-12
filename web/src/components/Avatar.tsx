import { useState, useSyncExternalStore } from 'react'
import { BASE } from '../api'
import { avatarVersion, subscribeAvatars } from '../avatarVersion'
import { DELETED_USER } from '../types'

// Round avatar: the user's picture, else (no picture, deleted user, load error) the first letter of the name.
export default function Avatar({ userName, small }: { userName: string; small?: boolean }) {
  const v = useSyncExternalStore(subscribeAvatars, avatarVersion)
  const src = `${BASE}/users/${encodeURIComponent(userName)}/avatar?v=${v}`
  const [failed, setFailed] = useState('') // the src that answered 404, so a new picture is tried again
  return (
    <span className={small ? 'avatar small' : 'avatar'} aria-hidden="true">
      {userName !== DELETED_USER && failed !== src
        ? <img src={src} alt="" onError={() => setFailed(src)} onLoad={(e) => { if (e.currentTarget.naturalWidth === 0) setFailed(src) }} />
        : userName.charAt(0).toUpperCase()}
    </span>
  )
}
