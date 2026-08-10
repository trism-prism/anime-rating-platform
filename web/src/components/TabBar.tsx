import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth'
import { usePending } from '../pendingRequests'
import Icon, { type IconName } from './Icon'

// main navigation on phones: fixed to the bottom edge, hidden from 640px up (see .tabbar in index.css)
export default function TabBar() {
  const { user, loading } = useAuth()
  const pending = usePending()
  const tabs: { to: string; label: string; icon: IconName; end?: boolean }[] = [
    { to: '/', label: 'Home', icon: 'home', end: true },
    { to: '/trending', label: 'Trending', icon: 'trending' },
    { to: '/rankings', label: 'Rankings', icon: 'rankings' },
    ...(user
      ? [{ to: '/my-list', label: 'My List', icon: 'list' as const }, { to: '/feed', label: 'Friends', icon: 'friends' as const }]
      : loading ? [] : [{ to: '/login', label: 'Log in', icon: 'login' as const }]),
  ]
  return (
    <nav className="tabbar" aria-label="Main">
      {tabs.map((t) => (
        <NavLink key={t.to} to={t.to} end={t.end}>
          <span className="tab-icon">
            <Icon name={t.icon} />
            {t.to === '/feed' && pending > 0 && <span className="count-badge" aria-hidden="true">{pending > 9 ? '9+' : pending}</span>}
          </span>
          <span>{t.label}</span>
          {t.to === '/feed' && pending > 0 && <span className="sr-only">, {pending} {pending === 1 ? 'request' : 'requests'} waiting</span>}
        </NavLink>
      ))}
    </nav>
  )
}
