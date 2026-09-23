import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { Components } from 'react-markdown'

const BASE = import.meta.env.VITE_API_BASE ?? ''
const MAX_CHARS = 4000

// ── Types ──────────────────────────────────────────────────────────────────

type Role = 'user' | 'assistant'
type ServerStatus = 'checking' | 'online' | 'offline'

interface Usage { promptTokens: number; completionTokens: number; totalTokens: number }

interface Message {
  id: string
  role: Role
  content: string
  model?: string
  usage?: Usage
  isStreaming?: boolean
}

// ── Utilities ──────────────────────────────────────────────────────────────

const uid = () => Math.random().toString(36).slice(2, 9)

function pluralTokens(n: number): string {
  const mod10 = n % 10, mod100 = n % 100
  if (mod10 === 1 && mod100 !== 11) return 'токен'
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 'токена'
  return 'токенов'
}

// ── Markdown components ─────────────────────────────────────────────────────

const mdComponents: Components = {
  pre({ children }) {
    return <pre className="md-pre">{children}</pre>
  },
  code({ node: _node, className, children, ...props }) {
    const match = /language-(\w+)/.exec(className || '')
    if (match) {
      return (
        <>
          <span className="md-lang">{match[1]}</span>
          <code {...props}>{children}</code>
        </>
      )
    }
    return <code className="md-inline" {...props}>{children}</code>
  },
  a({ node: _node, href, children, ...props }) {
    return (
      <a href={href} target="_blank" rel="noopener noreferrer" {...props}>
        {children}
      </a>
    )
  },
}

// ── API ────────────────────────────────────────────────────────────────────

async function apiHealth(): Promise<boolean> {
  try {
    const r = await fetch(`${BASE}/api/health`, { signal: AbortSignal.timeout(4500) })
    if (!r.ok) return false
    const d = await r.json()
    return d.status === 'ok'
  } catch { return false }
}

async function apiModels(): Promise<string[]> {
  try {
    const r = await fetch(`${BASE}/api/models`)
    if (!r.ok) return []
    const d = await r.json()
    return Array.isArray(d.models) ? d.models : []
  } catch { return [] }
}

// ── Mist ───────────────────────────────────────────────────────────────────

function MistParticles() {
  const particles = useMemo(() =>
    Array.from({ length: 14 }, (_, i) => ({
      id: i,
      left: `${(i / 14) * 100 + (Math.random() - 0.5) * 7}%`,
      size: Math.random() * 90 + 28,
      duration: Math.random() * 28 + 18,
      delay: -(Math.random() * 24),
      opacity: Math.random() * 0.13 + 0.04,
    })), [])

  return (
    <div className="mist-layer" aria-hidden="true">
      {particles.map(p => (
        <div
          key={p.id}
          className="mist-orb"
          style={{
            left: p.left,
            width: p.size,
            height: p.size,
            animationDuration: `${p.duration}s`,
            animationDelay: `${p.delay}s`,
            opacity: p.opacity,
          }}
        />
      ))}
    </div>
  )
}

// ── Ziggurat Icon ──────────────────────────────────────────────────────────

function ZigguratIcon({ size = 28 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 30" fill="none" aria-hidden="true">
      <rect x="0"  y="24" width="32" height="6"  fill="currentColor" opacity="0.62"/>
      <rect x="4"  y="18" width="24" height="8"  fill="currentColor" opacity="0.72"/>
      <rect x="8"  y="12" width="16" height="8"  fill="currentColor" opacity="0.83"/>
      <rect x="12" y="6"  width="8"  height="8"  fill="currentColor" opacity="0.92"/>
      <rect x="14" y="0"  width="4"  height="8"  fill="currentColor"/>
      {/* Glowing rune windows */}
      <rect x="14" y="13" width="4" height="3"   fill="#a855f7" rx="0.5"/>
      <rect x="6"  y="19" width="3" height="2.5" fill="#7c3aed" rx="0.5"/>
      <rect x="23" y="19" width="3" height="2.5" fill="#7c3aed" rx="0.5"/>
      <rect x="14" y="13" width="4" height="3"   fill="#c084fc" opacity="0.45" rx="0.5"/>
    </svg>
  )
}

// ── Status Dot ─────────────────────────────────────────────────────────────

function StatusDot({ status }: { status: ServerStatus }) {
  const color = status === 'online' ? '#10b981' : status === 'offline' ? '#ef4444' : '#a855f7'
  return (
    <span
      className={`status-dot status-${status}`}
      style={{ background: color }}
      title={status === 'online' ? 'Сервер: в сети' : status === 'offline' ? 'Сервер: недоступен' : 'Сервер: проверка…'}
      aria-label={status === 'online' ? 'Сервер: в сети' : status === 'offline' ? 'Сервер: недоступен' : 'Сервер: проверка…'}
      role="status"
    />
  )
}

// ── Header ─────────────────────────────────────────────────────────────────

interface HeaderProps {
  models: string[]
  selected: string
  onModel: (m: string) => void
  serverStatus: ServerStatus
  onNew: () => void
}

function Header({ models, selected, onModel, serverStatus, onNew }: HeaderProps) {
  return (
    <header className="app-header">
      <div className="header-logo">
        <ZigguratIcon size={26} />
        <span className="logo-text">ZigguratHub</span>
      </div>
      <div className="header-controls">
        <StatusDot status={serverStatus} />
        <select
          className="model-select"
          value={selected}
          onChange={e => onModel(e.target.value)}
          disabled={models.length === 0}
          aria-label="Выберите модель"
        >
          {models.length === 0
            ? <option value="">Загрузка…</option>
            : models.map(m => <option key={m} value={m}>{m}</option>)
          }
        </select>
        <button className="btn-new-chat" onClick={onNew} aria-label="Новый чат">
          <svg width="12" height="12" viewBox="0 0 12 12" fill="none" aria-hidden="true">
            <path d="M6 1v10M1 6h10" stroke="currentColor" strokeWidth="1.8" strokeLinecap="square"/>
          </svg>
          Новый чат
        </button>
      </div>
    </header>
  )
}

// ── Empty State ─────────────────────────────────────────────────────────────

const PROMPTS = [
  "Какие тёмные чары оживляют легионы нежити Плети?",
  "Объясни трансформеры так, будто Кел'Тузад читает лекцию Королю-личу.",
  "Напиши хайку о Ледяном Троне в полночь.",
  "Как работает SSE-стриминг в современных веб-API?",
]

function EmptyState({ onPrompt }: { onPrompt: (p: string) => void }) {
  return (
    <div className="empty-state">
      <div className="empty-hero">
        {/* Ziggurat hero illustration */}
        <div className="hero-ziggurat">
          <svg viewBox="0 0 200 150" fill="none" className="hero-svg" aria-hidden="true">
            {/* Mist at base */}
            <ellipse cx="100" cy="144" rx="88" ry="9" fill="#7c3aed" opacity="0.1"/>
            <ellipse cx="100" cy="146" rx="70" ry="6" fill="#a855f7" opacity="0.07"/>

            {/* Ziggurat body — stepped pyramid */}
            <rect x="0"   y="120" width="200" height="30" fill="#1a1729" opacity="0.88"/>
            <rect x="16"  y="96"  width="168" height="32" fill="#1a1729" opacity="0.82"/>
            <rect x="32"  y="72"  width="136" height="32" fill="#1a1729" opacity="0.86"/>
            <rect x="48"  y="48"  width="104" height="32" fill="#1a1729" opacity="0.9"/>
            <rect x="64"  y="24"  width="72"  height="32" fill="#1a1729" opacity="0.94"/>
            <rect x="80"  y="0"   width="40"  height="32" fill="#1a1729"/>

            {/* Edge highlight lines (spiky facet effect) */}
            <line x1="0" y1="120" x2="16" y2="96"  stroke="rgba(124,58,237,0.22)" strokeWidth="0.8"/>
            <line x1="200" y1="120" x2="184" y2="96" stroke="rgba(124,58,237,0.22)" strokeWidth="0.8"/>
            <line x1="16" y1="96" x2="32" y2="72"   stroke="rgba(124,58,237,0.18)" strokeWidth="0.8"/>
            <line x1="184" y1="96" x2="168" y2="72" stroke="rgba(124,58,237,0.18)" strokeWidth="0.8"/>

            {/* Rune windows — top spire */}
            <rect x="88"  y="4"  width="24" height="16" fill="#a855f7" rx="1" opacity="0.85"/>
            <rect x="88"  y="4"  width="24" height="16" fill="#c084fc" rx="1" opacity="0.25"/>

            {/* Level 2 windows */}
            <rect x="72"  y="28" width="16" height="12" fill="#7c3aed" rx="1" opacity="0.8"/>
            <rect x="112" y="28" width="16" height="12" fill="#7c3aed" rx="1" opacity="0.8"/>

            {/* Level 3 */}
            <rect x="56"  y="52" width="12" height="10" fill="#6d28d9" rx="1" opacity="0.7"/>
            <rect x="132" y="52" width="12" height="10" fill="#6d28d9" rx="1" opacity="0.7"/>

            {/* Level 4 */}
            <rect x="40"  y="76" width="10" height="8" fill="#5b21b6" rx="1" opacity="0.6"/>
            <rect x="150" y="76" width="10" height="8" fill="#5b21b6" rx="1" opacity="0.6"/>

            {/* Glow halos over windows */}
            <rect x="84"  y="0"  width="32" height="22" fill="#c084fc" opacity="0.08" rx="2"/>

            {/* Floating rune glyphs */}
            <text x="12"  y="64" fill="#7c3aed" fontSize="12" opacity="0.5" fontFamily="serif">ᚠ</text>
            <text x="174" y="84" fill="#7c3aed" fontSize="10" opacity="0.45" fontFamily="serif">ᚢ</text>
            <text x="20"  y="110" fill="#6d28d9" fontSize="9" opacity="0.35" fontFamily="serif">ᚦ</text>
            <text x="170" y="44" fill="#6d28d9" fontSize="8" opacity="0.3" fontFamily="serif">ᚨ</text>
          </svg>
        </div>

        <h1 className="hero-title">ZigguratHub</h1>
        <p className="hero-sub">Врата Короля-лича к запретным знаниям</p>

        <div className="rune-row" aria-hidden="true">
          {['ᚠ','ᚢ','ᚦ','ᚨ','ᚱ'].map((r, i) => (
            <span key={i} style={{ animationDelay: `${i * 0.55}s` }}>{r}</span>
          ))}
        </div>
      </div>

      <div className="example-grid">
        {PROMPTS.map((p, i) => (
          <button key={i} className="example-card" onClick={() => onPrompt(p)}>
            <span className="example-rune" style={{ animationDelay: `${i * 0.8}s` }}>ᚱ</span>
            <span>{p}</span>
          </button>
        ))}
      </div>
    </div>
  )
}

// ── Message Bubble ─────────────────────────────────────────────────────────

interface MessageBubbleProps {
  msg: Message
  isEditing: boolean
  editText: string
  isStreaming: boolean
  onStartEdit: (msg: Message) => void
  onEditTextChange: (v: string) => void
  onSubmitEdit: () => void
  onCancelEdit: () => void
}

function MessageBubble({ msg, isEditing, editText, isStreaming, onStartEdit, onEditTextChange, onSubmitEdit, onCancelEdit }: MessageBubbleProps) {
  const isUser = msg.role === 'user'

  const handleEditKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); if (editText.trim()) onSubmitEdit() }
    if (e.key === 'Escape') { e.preventDefault(); onCancelEdit() }
  }

  return (
    <div className={`msg-row ${isUser ? 'msg-user' : 'msg-assistant'}`}>
      {!isUser && (
        <div className="msg-avatar" aria-hidden="true">
          <ZigguratIcon size={16} />
        </div>
      )}

      <div className={`bubble-wrap ${isUser ? 'bubble-wrap-user' : 'bubble-wrap-assistant'}`}>
        <div className={`msg-bubble ${isUser ? 'bubble-user' : 'bubble-assistant'}`}>
          {isEditing ? (
            <div className="edit-box">
              <textarea
                className="edit-textarea"
                value={editText}
                onChange={e => onEditTextChange(e.target.value)}
                onKeyDown={handleEditKey}
                autoFocus
                rows={2}
                maxLength={MAX_CHARS}
                aria-label="Редактирование сообщения"
              />
              <div className="edit-actions">
                <button type="button" className="btn-send" onClick={onSubmitEdit} disabled={!editText.trim() || editText.length > MAX_CHARS}>Отправить</button>
                <button type="button" className="btn-edit-cancel" onClick={onCancelEdit}>Отмена</button>
              </div>
            </div>
          ) : isUser ? (
            <p className="msg-user-text">{msg.content}</p>
          ) : (
            <div className="msg-md">
              <ReactMarkdown remarkPlugins={[remarkGfm]} components={mdComponents}>
                {msg.content}
              </ReactMarkdown>
            </div>
          )}
          {msg.isStreaming && <span className="stream-cursor" aria-hidden="true">▋</span>}
          {msg.usage && (
            <div className="token-badge">
              <span>{msg.usage.totalTokens.toLocaleString('ru-RU')} {pluralTokens(msg.usage.totalTokens)}</span>
              {msg.model && <span className="token-model">{msg.model.split('/').pop()}</span>}
            </div>
          )}
        </div>
      </div>

      {isUser && !isStreaming && !isEditing && (
        <button type="button" className="msg-edit-btn" onClick={() => onStartEdit(msg)} aria-label="Редактировать сообщение" title="Редактировать">✎</button>
      )}

      {isUser && (
        <div className="msg-avatar msg-avatar-user" aria-hidden="true">⚔</div>
      )}
    </div>
  )
}

// ── Input Bar ──────────────────────────────────────────────────────────────

interface InputBarProps {
  value: string
  onChange: (v: string) => void
  onSend: () => void
  onStop: () => void
  isStreaming: boolean
  disabled: boolean
}

function InputBar({ value, onChange, onSend, onStop, isStreaming, disabled }: InputBarProps) {
  const taRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    const ta = taRef.current
    if (!ta) return
    ta.style.height = 'auto'
    ta.style.height = Math.min(ta.scrollHeight, 200) + 'px'
  }, [value])

  const handleKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      if (!isStreaming && value.trim() && !disabled) onSend()
    }
  }

  const tooLong = value.length > MAX_CHARS
  const canSend = value.trim().length > 0 && !disabled && !tooLong && !isStreaming

  return (
    <form
      className="input-bar"
      onSubmit={e => { e.preventDefault(); if (canSend) onSend() }}
      aria-label="Форма сообщения"
    >
      <div className="input-frame">
        <div className={`input-wrapper${tooLong ? ' is-error' : ''}`}>
          <textarea
            ref={taRef}
            className="input-textarea"
            value={value}
            onChange={e => onChange(e.target.value)}
            onKeyDown={handleKey}
            placeholder="Изреки свою волю, слуга Плети…"
            rows={1}
            aria-label="Сообщение"
            disabled={disabled && !isStreaming}
          />
          <div className="input-footer">
            <span className={`char-count${tooLong ? ' char-over' : value.length > MAX_CHARS * 0.8 ? ' char-warn' : ''}`}>
              {value.length.toLocaleString()} / {MAX_CHARS.toLocaleString()}
            </span>
            {isStreaming
              ? (
                <button type="button" className="btn-stop" onClick={onStop} aria-label="Остановить генерацию">
                  ⬛ Стоп
                </button>
              )
              : (
                <button type="submit" className="btn-send" disabled={!canSend} aria-label="Отправить сообщение">
                  <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
                    <path d="M1.5 12.5L12.5 7 1.5 1.5v4l7 1.5-7 1.5z" fill="currentColor"/>
                  </svg>
                  Отправить
                </button>
              )
            }
          </div>
        </div>
      </div>
      <p className="input-hint">Enter — отправить · Shift+Enter — новая строка</p>
    </form>
  )
}

// ── Error Banner ───────────────────────────────────────────────────────────

const ERROR_ICONS: Record<string, string> = {
  auth: '🔐',
  quota_limit: '⚠️',
  timeout: '⏱️',
  network: '📡',
  server_unreachable: '💀',
}

function ErrorBanner({ type, message, onDismiss }: { type: string; message: string; onDismiss: () => void }) {
  return (
    <div className="error-banner" role="alert" aria-live="assertive">
      <span className="error-icon">{ERROR_ICONS[type] ?? '⚠️'}</span>
      <span className="error-msg">{message}</span>
      <button className="error-dismiss" onClick={onDismiss} aria-label="Закрыть">✕</button>
    </div>
  )
}

// ── App ────────────────────────────────────────────────────────────────────

export default function App() {
  const [messages, setMessages]         = useState<Message[]>([])
  const [input, setInput]               = useState('')
  const [isStreaming, setIsStreaming]   = useState(false)
  const [serverStatus, setServerStatus] = useState<ServerStatus>('checking')
  const [models, setModels]             = useState<string[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [error, setError]               = useState<{ type: string; message: string } | null>(null)
  const [editingId, setEditingId]       = useState<string | null>(null)
  const [editText, setEditText]         = useState('')

  const bottomRef  = useRef<HTMLDivElement>(null)
  const abortRef   = useRef<AbortController | null>(null)

  // Health polling
  useEffect(() => {
    const check = async () => {
      const ok = await apiHealth()
      setServerStatus(ok ? 'online' : 'offline')
      if (!ok) setError({ type: 'server_unreachable', message: 'Сервер недоступен — убедитесь, что бэкенд запущен.' })
      else setError(prev => (prev?.type === 'server_unreachable' ? null : prev))
    }
    check()
    const id = setInterval(check, 30_000)
    return () => clearInterval(id)
  }, [])

  // Load models once
  useEffect(() => {
    apiModels().then(ms => {
      if (ms.length) { setModels(ms); setSelectedModel(ms[0]) }
    })
  }, [])

  // Auto-scroll to bottom
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Esc — остановить генерацию из любого места интерфейса
  useEffect(() => {
    if (!isStreaming) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        abortRef.current?.abort()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [isStreaming])

  const handleSend = useCallback(async (opts?: { editId?: string; text?: string }) => {
    const text = (opts?.text ?? input).trim()
    if (!text || isStreaming) return
    const editId = opts?.editId
    const assistId = uid()
    let history: { role: Role; content: string }[]

    if (editId) {
      const idx = messages.findIndex(m => m.id === editId)
      if (idx === -1 || messages[idx].role !== 'user') return
      const edited: Message = { ...messages[idx], content: text }
      const truncated = [...messages.slice(0, idx), edited]
      setMessages([...truncated, { id: assistId, role: 'assistant', content: '', isStreaming: true }])
      history = truncated.map(m => ({ role: m.role, content: m.content }))
    } else {
      const userMsg: Message = { id: uid(), role: 'user', content: text }
      setMessages(prev => [...prev, userMsg, { id: assistId, role: 'assistant', content: '', isStreaming: true }])
      history = [...messages, userMsg].map(m => ({ role: m.role, content: m.content }))
    }

    setInput('')
    setEditingId(null)
    setEditText('')
    setIsStreaming(true)
    setError(null)

    const ctrl = new AbortController()
    abortRef.current = ctrl

    try {
      const resp = await fetch(`${BASE}/api/chat`, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ model: selectedModel, messages: history, stream: true }),
        signal:  ctrl.signal,
      })

      if (!resp.ok) {
        const data = await resp.json().catch(() => ({}))
        throw Object.assign(new Error(data.message || `HTTP ${resp.status}`), { errType: data.type || 'network' })
      }

      const reader  = resp.body!.getReader()
      const decoder = new TextDecoder()
      let buf = ''
      let finalModel: string | undefined
      let finalUsage: Usage | undefined
      let sawError = false

      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buf += decoder.decode(value, { stream: true })
        const lines = buf.split('\n')
        buf = lines.pop()!

        let eventName = ''
        for (const line of lines) {
          if (line.startsWith('event: ')) {
            eventName = line.slice(7).trim()
            continue
          }
          if (!line.startsWith('data: ')) continue
          const raw = line.slice(6).trim()
          if (!raw || raw === '[DONE]') continue
          try {
            const ev = JSON.parse(raw)
            if (eventName === 'error') {
              setError({ type: ev.type || 'error', message: ev.message || 'Ошибка сервера.' })
              sawError = true
              break
            }
            if (ev.content !== undefined) {
              setMessages(prev => prev.map(m =>
                m.id === assistId ? { ...m, content: m.content + (ev.content as string) } : m
              ))
            }
            if (ev.model) finalModel = ev.model
            if (ev.usage) finalUsage = ev.usage
          } catch { /* malformed SSE line */ }
        }
        if (sawError) break
      }

      setMessages(prev => prev.map(m =>
        m.id === assistId
          ? { ...m, isStreaming: false, model: finalModel, usage: finalUsage }
          : m
      ))

    } catch (err: any) {
      if (err.name === 'AbortError') {
        setMessages(prev => prev.map(m =>
          m.id === assistId ? { ...m, isStreaming: false, content: m.content ? m.content + ' ▪' : '[Остановлено]' } : m
        ))
      } else {
        const type = err.errType ?? (err.message?.toLowerCase().includes('fetch') ? 'network' : 'unknown')
        setError({ type, message: err.message || 'Произошла непредвиденная ошибка.' })
        setMessages(prev => prev.map(m =>
          m.id === assistId ? { ...m, isStreaming: false, content: m.content || '[Ошибка ответа]' } : m
        ))
      }
    } finally {
      setIsStreaming(false)
      abortRef.current = null
    }
  }, [input, isStreaming, messages, selectedModel])

  const handleStop = useCallback(() => { abortRef.current?.abort() }, [])

  const startEdit = useCallback((msg: Message) => {
    if (isStreaming) return
    setEditingId(msg.id)
    setEditText(msg.content)
  }, [isStreaming])

  const cancelEdit = useCallback(() => {
    setEditingId(null)
    setEditText('')
  }, [])

  const submitEdit = useCallback(() => {
    if (!editingId) return
    handleSend({ editId: editingId, text: editText })
  }, [editingId, editText, handleSend])

  const handleNew = useCallback(() => {
    if (isStreaming) abortRef.current?.abort()
    setMessages([])
    setError(null)
    setInput('')
    setEditingId(null)
    setEditText('')
  }, [isStreaming])

  const handlePrompt = useCallback((p: string) => { setInput(p) }, [])

  return (
    <div className="app-shell">
      <MistParticles />

      <Header
        models={models}
        selected={selectedModel}
        onModel={setSelectedModel}
        serverStatus={serverStatus}
        onNew={handleNew}
      />

      <main className="chat-main" aria-label="Переписка" aria-live="polite">
        {messages.length === 0
          ? <EmptyState onPrompt={handlePrompt} />
          : (
            <div className="msg-list">
              {messages.map(msg => (
                <MessageBubble
                  key={msg.id}
                  msg={msg}
                  isEditing={editingId === msg.id && msg.role === 'user'}
                  editText={editText}
                  isStreaming={isStreaming}
                  onStartEdit={startEdit}
                  onEditTextChange={setEditText}
                  onSubmitEdit={submitEdit}
                  onCancelEdit={cancelEdit}
                />
              ))}
              <div ref={bottomRef} />
            </div>
          )
        }
      </main>

      {error && (
        <ErrorBanner
          type={error.type}
          message={error.message}
          onDismiss={() => setError(null)}
        />
      )}

      <InputBar
        value={input}
        onChange={setInput}
        onSend={handleSend}
        onStop={handleStop}
        isStreaming={isStreaming}
        disabled={serverStatus === 'offline' || !selectedModel}
      />
    </div>
  )
}
