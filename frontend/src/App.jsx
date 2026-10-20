import { useState } from 'react';
import { session } from './api';
import Login from './components/Login.jsx';
import Trips from './components/Trips.jsx';
import Expenses from './components/Expenses.jsx';
import Approvals from './components/Approvals.jsx';

export default function App() {
  const [user, setUser] = useState(session.load());
  const [tab, setTab] = useState('trips');

  if (!user) return <Login onLogin={(u) => { session.save(u); setUser(u); }} />;

  const isManager = user.role === 'Manager' || user.role === 'Admin';
  const logout = () => { session.clear(); setUser(null); };

  return (
    <div className="app">
      <header>
        <h1>TravelXpense</h1>
        <nav>
          <button className={tab === 'trips' ? 'active' : ''} onClick={() => setTab('trips')}>Trips &amp; Flights</button>
          <button className={tab === 'expenses' ? 'active' : ''} onClick={() => setTab('expenses')}>Expenses</button>
          {isManager && (
            <button className={tab === 'approvals' ? 'active' : ''} onClick={() => setTab('approvals')}>Approvals</button>
          )}
        </nav>
        <div className="who">{user.fullName} ({user.role}) <button onClick={logout}>Log out</button></div>
      </header>
      <main>
        {tab === 'trips' && <Trips />}
        {tab === 'expenses' && <Expenses />}
        {tab === 'approvals' && isManager && <Approvals />}
      </main>
    </div>
  );
}
