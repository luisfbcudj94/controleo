#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   bash scripts/deploy-cloudrun-dev.sh
# Optional env overrides:
#   PROJECT_ID, REGION, SERVICE, REPO_URL, BRANCH, WORKDIR

PROJECT_ID="${PROJECT_ID:-controleo-262c4}"
REGION="${REGION:-us-central1}"
SERVICE="${SERVICE:-controleo-api}"
REPO_URL="${REPO_URL:-https://github.com/luisfbcudj94/controleo.git}"
BRANCH="${BRANCH:-dev}"
WORKDIR="${WORKDIR:-$HOME/controleo}"

echo "==> Configurando proyecto: ${PROJECT_ID}"
gcloud config set project "${PROJECT_ID}" >/dev/null

echo "==> Habilitando APIs necesarias"
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com >/dev/null

echo "==> Clonando/actualizando repo (${BRANCH}) en ${WORKDIR}"
if [[ -d "${WORKDIR}/.git" ]]; then
  cd "${WORKDIR}"
  git fetch origin
  git checkout "${BRANCH}"
  git pull --ff-only origin "${BRANCH}"
else
  rm -rf "${WORKDIR}"
  git clone --branch "${BRANCH}" "${REPO_URL}" "${WORKDIR}"
  cd "${WORKDIR}"
fi

if [[ ! -f "./Controleo.Api/Controleo.Api.csproj" ]]; then
  echo "ERROR: No se encontró ./Controleo.Api/Controleo.Api.csproj"
  exit 1
fi

echo "==> Desplegando Cloud Run (${SERVICE}) solo backend"
gcloud run deploy "${SERVICE}" \
  --source ./Controleo.Api \
  --region "${REGION}" \
  --platform managed \
  --allow-unauthenticated \
  --min-instances=0 \
  --max-instances=1 \
  --cpu=1 \
  --memory=512Mi \
  --concurrency=80 \
  --timeout=30 \
  --cpu-throttling

echo "==> Verificación"
gcloud run services describe "${SERVICE}" --region "${REGION}" \
  --format="table(metadata.name,status.url,status.latestReadyRevisionName,spec.template.metadata.annotations.\"autoscaling.knative.dev/minScale\",spec.template.metadata.annotations.\"autoscaling.knative.dev/maxScale\",spec.template.spec.containers[0].resources.limits.memory,spec.template.spec.containerConcurrency,spec.template.spec.timeoutSeconds)"

SERVICE_URL="$(gcloud run services describe "${SERVICE}" --region "${REGION}" --format='value(status.url)')"
echo "==> Smoke test: ${SERVICE_URL}/api/catalogs"
curl -i "${SERVICE_URL}/api/catalogs" || true

echo "==> Deploy finalizado"