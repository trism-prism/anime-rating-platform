import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Browse from './pages/Browse'
import SeriesPage from './pages/SeriesPage'
import TitleDetail from './pages/TitleDetail'
import ReviewPage from './pages/ReviewPage'
import Trending from './pages/Trending'
import Rankings from './pages/Rankings'
import MyList from './pages/MyList'
import Login from './pages/Login'
import Register from './pages/Register'
import ForgotPassword from './pages/ForgotPassword'
import Settings from './pages/Settings'
import Profile from './pages/Profile'
import NotificationPreferences from './pages/NotificationPreferences'
import Verify from './pages/Verify'
import Admin from './pages/Admin'
import Feed from './pages/Feed'
import ConfirmEmailChange from './pages/ConfirmEmailChange'
import Privacy from './pages/Privacy'
import GoogleCallback from './pages/GoogleCallback'
import { AuthProvider } from './auth'

export default function App() {
  return (
    <AuthProvider>
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<Browse />} />
          <Route path="/series/:id" element={<SeriesPage />} />
          <Route path="/titles/:id" element={<TitleDetail />} />
          <Route path="/reviews/:id" element={<ReviewPage />} />
          <Route path="/trending" element={<Trending />} />
          <Route path="/rankings" element={<Rankings />} />
          <Route path="/my-list" element={<MyList />} />
          <Route path="/feed" element={<Feed />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/forgot-password" element={<ForgotPassword />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/settings/notifications" element={<NotificationPreferences />} />
          <Route path="/u/:username" element={<Profile />} />
          <Route path="/verify" element={<Verify />} />
          <Route path="/admin" element={<Admin />} />
          <Route path="/confirm-email-change" element={<ConfirmEmailChange />} />
          <Route path="/privacy" element={<Privacy />} />
          <Route path="/auth/callback" element={<GoogleCallback />} />
          <Route path="*"element={<h1>Page not found</h1>} />
        </Route>
      </Routes>
    </BrowserRouter>
    </AuthProvider>
  )
}
