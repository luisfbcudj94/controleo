#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   bash scripts/destroy-cloudrun.sh
# Optional env overrides:
#   PROJECT_ID, REGION, SERVICE, REPO

PROJECT_ID="${PROJECT_ID:-controleo-262c4}"
REGION="${REGION:-us-central1}"
SERVICE="${SERVICE:-controleo-api}"
REPO="${REPO:-cloud-run-source-deploy}"

echo "==> Configurando proyecto: ${PROJECT_ID}"
gcloud config set project "${PROJECT_ID}" >/dev/null

echo "==> Eliminando servicio Cloud Run (si existe): ${SERVICE}"
if gcloud run services describe "${SERVICE}" --region "${REGION}" >/dev/null 2>&1; then
  gcloud run services delete "${SERVICE}" --region "${REGION}" --quiet
else
  echo "Servicio no existe, se omite."
fi

echo "==> Eliminando repo Artifact Registry (si existe): ${REPO}"
if gcloud artifacts repositories describe "${REPO}" --location "${REGION}" >/dev/null 2>&1; then
  gcloud artifacts repositories delete "${REPO}" --location "${REGION}" --quiet
else
  echo "Repositorio no existe, se omite."
fi

echo "==> Verificación final"
echo "Cloud Run services (${REGION}):"
gcloud run services list --region "${REGION}"

echo
echo "Artifact Registry repos (${REGION}):"
gcloud artifacts repositories list --location "${REGION}"

echo "==> Limpieza finalizada"