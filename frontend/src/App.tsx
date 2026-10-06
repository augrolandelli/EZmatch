import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import LoginPage from './features/auth/LoginPage'
import { RequireAuth, RequireRole } from './features/auth/RequireAuth'
import { AppLayout } from './shared/components/AppLayout'
import AgendaPage from './features/agenda/AgendaPage'
import WeekPage from './features/agenda/WeekPage'
import FixedBookingsPage from './features/agenda/FixedBookingsPage'
import DashboardPage from './features/dashboard/DashboardPage'
import AccountPage from './features/account/AccountPage'
import ConfigPage from './features/config/ConfigPage'
import CustomersPage from './features/customers/CustomersPage'
import ClubsPage from './features/admin/ClubsPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<RequireAuth />}>
          <Route element={<AppLayout />}>
            <Route index element={<DashboardPage />} />
            <Route path="agenda" element={<AgendaPage />} />
            <Route path="agenda/semana" element={<WeekPage />} />
            <Route path="agenda/fijos" element={<FixedBookingsPage />} />
            <Route path="clientes" element={<CustomersPage />} />
            <Route path="cuenta" element={<AccountPage />} />
            <Route element={<RequireRole roles={['Owner', 'SuperAdmin']} />}>
              <Route path="configuracion" element={<ConfigPage />} />
            </Route>
            <Route element={<RequireRole roles={['SuperAdmin']} />}>
              <Route path="admin/clubes" element={<ClubsPage />} />
            </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
