import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Browse from './pages/Browse'
import TitleDetail from './pages/TitleDetail'
import Trending from './pages/Trending'
import Rankings from './pages/Rankings'
import MyList from './pages/MyList'
import Login from './pages/Login'
import Register from './pages/Register'
import ForgotPassword from './pages/ForgotPassword'
import ResetPassword from './pages/ResetPassword'
import Settings from './pages/Settings'
import Admin from './pages/Admin'
import ConfirmEmail from './pages/ConfirmEmail'
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
          <Route path="/titles/:id" element={<TitleDetail />} />
          <Route path="/trending" element={<Trending />} />
          <Route path="/rankings" element={<Rankings />} />
          <Route path="/my-list" element={<MyList />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/forgot-password" element={<ForgotPassword />} />
          <Route path="/reset-password" element={<ResetPassword />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/admin" element={<Admin />} />
          <Route path="/confirm-email" element={<ConfirmEmail />} />
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
