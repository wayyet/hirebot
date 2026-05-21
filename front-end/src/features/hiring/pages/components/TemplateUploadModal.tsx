import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FileArchive, Loader2, UploadCloud, X } from "lucide-react";
import { api } from "@/infra/api";
import { ApiClientError } from "@/infra/api/httpClient";

interface TemplateUploadModalProps {
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

export default function TemplateUploadModal({
  open,
  onClose,
  onSuccess,
}: TemplateUploadModalProps) {
  const [dragOver, setDragOver] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const { t } = useTranslation();

  if (!open) return null;

  function handleClose() {
    if (uploading) return;
    setFile(null);
    setError(null);
    setDragOver(false);
    onClose();
  }

  function handleDragOver(e: React.DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(true);
  }

  function handleDragLeave(e: React.DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(false);
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    setDragOver(false);
    setError(null);

    const zipFile = Array.from(e.dataTransfer.files).find((f) =>
      f.name.toLowerCase().endsWith(".zip"),
    );
    if (zipFile) {
      setFile(zipFile);
    } else {
      setError(t('hiring.templateUpload.invalidFormat'));
    }
  }

  function handleFileSelect(e: React.ChangeEvent<HTMLInputElement>) {
    const selected = e.target.files?.[0];
    if (selected && selected.name.toLowerCase().endsWith(".zip")) {
      setFile(selected);
      setError(null);
    } else {
      setError(t('hiring.templateUpload.zipOnly'));
    }
  }

  function handleResetFile() {
    setFile(null);
    setError(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  }

  async function handleUpload() {
    if (!file) return;

    if (!file.name.toLowerCase().endsWith(".zip")) {
      setError(t('hiring.templateUpload.zipOnly'));
      return;
    }

    setUploading(true);
    setError(null);

    try {
      await api.employeeRuntime.quickCreateFromTemplate(file);
      setFile(null);
      setError(null);
      onSuccess();
    } catch (err) {
      const message =
        err instanceof ApiClientError
          ? err.message
          : t('hiring.templateUpload.uploadFailed');
      setError(message);
    } finally {
      setUploading(false);
    }
  }

  function formatSize(bytes: number) {
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  return (
    <div className="hb-modal-mask" onClick={handleClose}>
      <div className="hb-modal" onClick={(e) => e.stopPropagation()}>
        <button
          type="button"
          className="hb-modal-close"
          onClick={handleClose}
          aria-label={t('hiring.button.close')}
        >
          <X size={16} />
        </button>

        <div className="hb-modal-head">
          <h3 className="hb-modal-title">{t('hiring.templateUpload.title')}</h3>
          <p className="hb-modal-sub">
            {t('hiring.templateUpload.subtitle')}
          </p>
        </div>

        <div className="hb-modal-body">
          <div className="hb-upload-helper">
            <div className="hb-upload-helper-copy">
              <strong>{t('hiring.templateUpload.helperTitle')}</strong>
              <p>{t('hiring.templateUpload.helperDesc')}</p>
            </div>
            <a className="hb-upload-example-link" href="/template-pool">
              {t('hiring.templateUpload.helperLink')}
            </a>
          </div>

          {!file ? (
            <div
              className={`hb-upload-zone${dragOver ? " is-drag-over" : ""}`}
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                  fileInputRef.current?.click();
                }
              }}
              role="button"
              tabIndex={0}
            >
              <UploadCloud size={28} className="hb-upload-icon" />
              <p>{t('hiring.templateUpload.dragHint')}</p>
              <small>{t('hiring.templateUpload.clickHint')}</small>
              <input
                ref={fileInputRef}
                type="file"
                accept=".zip"
                hidden
                onChange={handleFileSelect}
              />
            </div>
          ) : (
            <div className="hb-upload-selected">
              <div className="flex items-center gap-3">
                <FileArchive size={20} className="text-[var(--hb-soft)]" />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">{file.name}</p>
                  <p className="text-xs text-[var(--hb-soft)]">
                    {formatSize(file.size)}
                  </p>
                </div>
                {!uploading && (
                  <button
                    type="button"
                    className="text-xs text-[var(--hb-soft)] hover:text-[var(--hb-near-black)]"
                    onClick={handleResetFile}
                  >
                    {t('hiring.templateUpload.selectAgain')}
                  </button>
                )}
              </div>
            </div>
          )}

          {error && (
            <div className="hb-alert hb-alert-error mt-4">
              <span>{error}</span>
            </div>
          )}
        </div>

        <div className="hb-modal-foot">
          <button
            type="button"
            className="hb-btn-ghost"
            onClick={handleClose}
            disabled={uploading}
          >
            {t('hiring.button.cancel')}
          </button>
          <button
            type="button"
            className="hb-btn-primary"
            onClick={handleUpload}
            disabled={!file || uploading}
          >
            {uploading ? (
              <>
                <Loader2 size={14} className="animate-spin" />
                {t('hiring.templateUpload.uploading')}
              </>
            ) : (
              t('hiring.templateUpload.confirmUpload')
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
