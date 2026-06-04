import { useState, useRef } from 'react'
import { Upload, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { SkillUploadPayload } from '../hiringPageTypes'

export function SkillUploadModal({
  open,
  disabled,
  onClose,
  onSubmit,
}: {
  open: boolean
  disabled: boolean
  onClose: () => void
  onSubmit: (payload: SkillUploadPayload) => void
}) {
  const { t } = useTranslation()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [dragOver, setDragOver] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [form, setForm] = useState({ name: '', releaseNote: '', description: '' })

  if (!open) return null

  function handleFile(nextFile: File) {
    const lowerName = nextFile.name.toLowerCase()
    if (lowerName.endsWith('.zip') || lowerName.endsWith('.tar.gz') || lowerName.endsWith('.gz')) {
      setFile(nextFile)
    }
  }

  function handleDrop(event: React.DragEvent<HTMLDivElement>) {
    event.preventDefault()
    setDragOver(false)
    if (disabled) return
    const dropped = event.dataTransfer.files[0]
    if (dropped) handleFile(dropped)
  }

  const canSubmit = Boolean(file && form.name.trim() && form.description.trim() && !disabled)

  return (
    <div className="hb-modal-mask">
      <div className="hb-modal hb-hiring-modal">
        <div className="hb-modal-head hb-hiring-modal-head">
          <div>
            <h2 className="hb-modal-title">{t('hiring.skillUpload.title')}</h2>
            <p className="hb-modal-sub">{t('hiring.skillUpload.desc')}</p>
          </div>
          <button onClick={onClose} disabled={disabled} className="hb-modal-close" aria-label={t('hiring.skillUpload.title')}>
            <X size={16} />
          </button>
        </div>

        <div className="hb-modal-body space-y-5">
          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">{t('hiring.skillUpload.package')} <span className="text-red-500">*</span></label>
            <div
              className={`hb-hiring-dropzone ${dragOver ? 'is-active' : file ? 'is-filled' : ''}`}
              onClick={() => { if (!disabled) fileInputRef.current?.click() }}
              onDragOver={(event) => { event.preventDefault(); if (!disabled) setDragOver(true) }}
              onDragLeave={() => setDragOver(false)}
              onDrop={handleDrop}
            >
              <Upload size={22} className={`mx-auto mb-2 ${file ? 'text-violet-500' : 'text-slate-400'}`} />
              {file ? (
                <>
                  <p className="hb-hiring-dropzone-file text-sm font-medium">{file.name}</p>
                  <p className="hb-hiring-dropzone-sub mt-1 text-xs">{t('hiring.skillUpload.selectAgain')}</p>
                </>
              ) : (
                <>
                  <p className="hb-hiring-dropzone-copy text-sm">{t('hiring.skillUpload.dragHint')}</p>
                  <p className="hb-hiring-dropzone-sub mt-1 text-xs">{t('hiring.skillUpload.supportedFormats')}</p>
                </>
              )}
              <input
                ref={fileInputRef}
                type="file"
                accept=".zip,.tar.gz,.gz"
                className="hidden"
                disabled={disabled}
                onChange={(event) => {
                  const selected = event.target.files?.[0]
                  if (selected) handleFile(selected)
                }}
              />
            </div>
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">{t('hiring.skillUpload.name')} <span className="text-red-500">*</span></label>
            <input
              type="text"
              disabled={disabled}
              value={form.name}
              onChange={(event) => setForm(prev => ({ ...prev, name: event.target.value }))}
              placeholder={t('hiring.skillUpload.namePlaceholder')}
              className="hb-hiring-form-input"
            />
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">
              {t('hiring.skillUpload.releaseNote')}
              <span className="hb-hiring-hint ml-1 font-normal">{t('hiring.skillUpload.releaseNoteOptional')}</span>
            </label>
            <textarea
              disabled={disabled}
              value={form.releaseNote}
              onChange={(event) => setForm(prev => ({ ...prev, releaseNote: event.target.value.slice(0, 500) }))}
              placeholder={t('hiring.skillUpload.releaseNotePlaceholder')}
              rows={3}
              className="hb-hiring-form-textarea"
            />
          </div>

          <div className="hb-hiring-form-field">
            <label className="hb-hiring-form-label">{t('hiring.skillUpload.description')} <span className="text-red-500">*</span></label>
            <textarea
              disabled={disabled}
              value={form.description}
              onChange={(event) => setForm(prev => ({ ...prev, description: event.target.value.slice(0, 1000) }))}
              placeholder={t('hiring.skillUpload.descriptionPlaceholder')}
              rows={4}
              className="hb-hiring-form-textarea"
            />
          </div>
        </div>

        <div className="hb-modal-foot">
          <button onClick={onClose} disabled={disabled} className="hb-btn-ghost">{t('hiring.button.cancel')}</button>
          <button
            disabled={!canSubmit}
            onClick={() => {
              if (!file) return
              onSubmit({
                file,
                name: form.name.trim(),
                releaseNote: form.releaseNote.trim(),
                description: form.description.trim(),
              })
            }}
            className="hb-btn-primary"
          >
            {t('hiring.button.submit')}
          </button>
        </div>
      </div>
    </div>
  )
}
