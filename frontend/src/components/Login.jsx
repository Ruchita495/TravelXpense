import { useState } from 'react';
import { api } from '../api';

export default function Login({ onLogin }) {
  const [mode, setMode] = useState('login');
  const [form, setForm] = useState({ email: 'employee@demo.com', fullName: '', password: 'Password123!' });
  const [error, setError] = useState('');
  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value });

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    try {
      const user = mode === 'login'
        ? await api.login(form.email, form.password)
        : await api.register(form.email, form.fullName, form.password);
      onLogin(user);
    } catch (err) { setError(err.message); }
  };

  return (
    <div className="login card">
      <h1>TravelXpense</h1>
      <p className="muted">Corporate travel booking &amp; expense management</p>
      <form onSubmit={submit}>
        <input placeholder="Email" value={form.email} onChange={set('email')} required />
        {mode === 'register' && <input placeholder="Full name" value={form.fullName} onChange={set('fullName')} required />}
        <input type="password" placeholder="Password (min 8 chars)" value={form.password} onChange={set('password')} required />
        <button className="primary">{mode === 'login' ? 'Log in' : 'Create account'}</button>
      </form>
      {error && <p className="error">{error}</p>}
      <p className="muted">
        Demo logins: employee@demo.com or manager@demo.com / Password123!
      </p>
      <button className="link" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
        {mode === 'login' ? 'Need an account? Register' : 'Have an account? Log in'}
      </button>
    </div>
  );
}
