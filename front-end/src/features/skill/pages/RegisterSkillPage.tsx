import { useRef, useState } from 'react'
import { AlertCircle, Upload } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Breadcrumb } from '@/shared/components/Breadcrumb'

export default function RegisterSkillPage() {
  const navigate = useNavigate()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [dragOver, setDragOver] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [form, setForm] = useState({
    name: '',
    releaseNote: '',
    description: '',
  })

  function handleFile(nextFile: File) {
    if (nextFile.name.endsWith('.zip') || nextFile.name.endsWith('.tar.gz') || nextFile.name.endsWith('.gz')) {
      setFile(nextFile)
    }
  }

  function handleDrop(event: React.DragEvent) {
    event.preventDefault()
    setDragOver(false)
    const nextFile = event.dataTransfer.files[0]
    if (nextFile) handleFile(nextFile)
  }

  function handleSubmit() {
    navigate('/skill')
  }

  const canSubmit = Boolean(file && form.name.trim() && form.description.trim())

  return (
    <div className="hb-page">
      <Breadcrumb items={[{ label: 'Skill 列表', to: '/skill' }, { label: '注册新技能' }]} />

      <div className="hb-page-head mt-5">
        <div>
          <span className="hb-kicker">Skill Upload</span>
          <h1 className="hb-page-title">上传并登记新的技能包</h1>
          <p className="hb-page-copy">
            技能注册后默认进入可审阅状态。当前页只负责资料录入，不改变后端真实发布策略。
          </p>
        </div>
      </div>

      <div className="grid gap-5 xl:grid-cols-[1.2fr_0.8fr]">
        <section className="hb-section">
          <div className="space-y-5">
            <div className="hb-field">
              <label className="hb-field-label">技能包 *</label>
              <div
                className={`hb-upload-drop ${dragOver ? 'is-hover' : ''} ${file ? 'is-filled' : ''}`}
                onClick={() => fileInputRef.current?.click()}
                onDragOver={(event) => {
                  event.preventDefault()
                  setDragOver(true)
                }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
              >
                <Upload size={28} className={file ? 'text-[#4a6cf7]' : 'text-[#9ca3af]'} />
                {file ? (
                  <>
                    <div className="text-sm font-semibold text-[#0a0a0a]">{file.name}</div>
                    <div className="text-xs text-[#737373]">点击重新选择技能包</div>
                  </>
                ) : (
                  <>
                    <div className="text-sm font-semibold text-[#0a0a0a]">拖拽技能包到此处上传</div>
                    <div className="text-xs text-[#737373]">支持 .zip / .tar.gz / .gz</div>
                  </>
                )}
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".zip,.tar.gz,.gz"
                  className="hidden"
                  onChange={(event) => {
                    if (event.target.files?.[0]) handleFile(event.target.files[0])
                  }}
                />
              </div>
            </div>

            <div className="hb-field">
              <label className="hb-field-label">技能名称 *</label>
              <input
                type="text"
                value={form.name}
                onChange={(event) => setForm({ ...form, name: event.target.value })}
                placeholder="请输入技能名称"
                className="hb-input"
              />
            </div>

            <div className="hb-field">
              <label className="hb-field-label">版本说明</label>
              <textarea
                value={form.releaseNote}
                onChange={(event) => setForm({ ...form, releaseNote: event.target.value.slice(0, 500) })}
                placeholder="描述本次版本的更新内容"
                className="hb-textarea"
              />
              <div className="hb-field-help">建议控制在 500 字以内，便于审核和追溯。</div>
            </div>

            <div className="hb-field">
              <label className="hb-field-label">技能描述 *</label>
              <textarea
                value={form.description}
                onChange={(event) => setForm({ ...form, description: event.target.value.slice(0, 1000) })}
                placeholder="请输入技能用途、输入输出和主要限制"
                className="hb-textarea"
              />
              <div className="hb-field-help">建议包含适用业务场景、预期输入和产出形式。</div>
            </div>
          </div>
        </section>

        <section className="hb-section">
          <div className="hb-section-head">
            <div>
              <h2 className="hb-section-title">提交前提醒</h2>
              <p className="hb-section-copy">先确认交付物完整，再进入下一步审核或上架流程。</p>
            </div>
          </div>

          <div className="space-y-4">
            <div className="hb-alert hb-alert-warn">
              <AlertCircle size={14} />
              <span>Skill 注册后初始状态为「下架中」，需要后续流程确认后才能真正被 HireBot 挂载使用。</span>
            </div>

            <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
              <div className="text-sm font-semibold text-[#0a0a0a]">当前待提交内容</div>
              <div className="mt-3 space-y-2 text-sm text-[#404040]">
                <div>技能包：{file?.name || '未上传'}</div>
                <div>技能名称：{form.name.trim() || '未填写'}</div>
                <div>版本说明：{form.releaseNote.trim() || '未填写'}</div>
                <div>技能描述：{form.description.trim() || '未填写'}</div>
              </div>
            </div>

            <div className="flex flex-wrap justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => navigate('/skill')}
                className="hb-btn-ghost"
              >
                取消
              </button>
              <button
                type="button"
                onClick={handleSubmit}
                disabled={!canSubmit}
                className="hb-btn-primary"
              >
                提交登记
              </button>
            </div>
          </div>
        </section>
      </div>
    </div>
  )
}
