{{/* Release-scoped names: <release>-docviewer-<component>, cut to 63 characters. */}}
{{- define "docviewer.fullname" -}}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 50 | trimSuffix "-" -}}
{{- end -}}

{{- define "docviewer.name" -}}
{{- printf "%s-%s" (include "docviewer.fullname" .root) .component | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "docviewer.labels" -}}
app.kubernetes.io/name: {{ .root.Chart.Name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
app.kubernetes.io/version: {{ .root.Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .root.Chart.Name .root.Chart.Version }}
{{- end -}}

{{- define "docviewer.selector" -}}
app.kubernetes.io/name: {{ .root.Chart.Name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{- define "docviewer.image" -}}
{{- $registry := .root.Values.image.registry -}}
{{- if $registry }}{{ printf "%s/%s:%s" $registry .image.repository .image.tag }}{{ else }}{{ printf "%s:%s" .image.repository .image.tag }}{{ end -}}
{{- end -}}

{{/* The chiselled .NET images run as uid 1654 with no shell; nothing needs to write outside /tmp. */}}
{{- define "docviewer.dotnetPodSecurity" -}}
runAsNonRoot: true
runAsUser: 1654
runAsGroup: 1654
seccompProfile: { type: RuntimeDefault }
{{- end -}}

{{- define "docviewer.dotnetContainerSecurity" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities: { drop: [ALL] }
{{- end -}}

{{- define "docviewer.secretName" -}}
{{- include "docviewer.name" (dict "root" . "component" "secrets") -}}
{{- end -}}

{{/* Fail early on settings the API would refuse anyway (D-029), and on missing source URLs. */}}
{{- define "docviewer.validate" -}}
{{- $salt := .Values.api.vinHashSalt | default "" -}}
{{- if and (not .Values.api.existingSaltSecret) (not $salt) -}}
{{- fail "api.vinHashSalt (or api.existingSaltSecret) is required." -}}
{{- end -}}
{{- if and (eq .Values.api.environment "Production") (not .Values.api.existingSaltSecret) (contains "change-me" $salt) -}}
{{- fail "api.environment=Production refuses the published demo salt (D-029): set api.vinHashSalt to a random secret or use api.existingSaltSecret." -}}
{{- end -}}
{{- if and (not .Values.mocks.enabled) (or (not .Values.sources.salesUrl) (not .Values.sources.serviceUrl)) -}}
{{- fail "mocks.enabled=false needs sources.salesUrl and sources.serviceUrl." -}}
{{- end -}}
{{- if and .Values.api.auth.enabled (or (not .Values.api.auth.authority) (not .Values.api.auth.audience)) -}}
{{- fail "api.auth.enabled needs api.auth.authority and api.auth.audience (D-034)." -}}
{{- end -}}
{{- end -}}
