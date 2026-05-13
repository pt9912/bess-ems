{{- define "bess-ems.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "bess-ems.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (include "bess-ems.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "bess-ems.labels" -}}
app.kubernetes.io/name: {{ include "bess-ems.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | quote }}
{{- end -}}

{{- define "bess-ems.selectorLabels" -}}
app.kubernetes.io/name: {{ include "bess-ems.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "bess-ems.assetConfigName" -}}
{{- printf "%s-assets" (include "bess-ems.fullname" .) -}}
{{- end -}}

{{- define "bess-ems.secretName" -}}
{{- printf "%s-runtime" (include "bess-ems.fullname" .) -}}
{{- end -}}

{{- define "bess-ems.postgresName" -}}
{{- printf "%s-postgres" (include "bess-ems.fullname" .) -}}
{{- end -}}

{{- define "bess-ems.mqttName" -}}
{{- printf "%s-mosquitto" (include "bess-ems.fullname" .) -}}
{{- end -}}

{{- define "bess-ems.optimizationCoreName" -}}
{{- printf "%s-optimization-core" (include "bess-ems.fullname" .) -}}
{{- end -}}

{{- define "bess-ems.image" -}}
{{- printf "%s:%s" .Values.image.repository .Values.image.tag -}}
{{- end -}}

{{- define "bess-ems.postgresConnectionString" -}}
{{- printf "Host=%s;Port=5432;Database=%s;Username=%s;Password=%s" (include "bess-ems.postgresName" .) .Values.postgres.database .Values.postgres.username .Values.postgres.password -}}{{- if .Values.persistence.includeErrorDetail -}};Include Error Detail=true{{- end -}}
{{- end -}}

{{- define "bess-ems.optimizationCoreEndpoint" -}}
{{- if .Values.optimizationCore.externalEndpoint -}}
{{- .Values.optimizationCore.externalEndpoint -}}
{{- else if .Values.optimizationCore.transport.uds.enabled -}}
{{- printf "unix://%s/optimization-core.sock" .Values.optimizationCore.transport.uds.mountPath -}}
{{- else -}}
{{- printf "http://%s:%v" (include "bess-ems.optimizationCoreName" .) .Values.optimizationCore.grpcPort -}}
{{- end -}}
{{- end -}}
